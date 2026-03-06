using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fail2Ban.API.Configuration;
using Fail2Ban.API.Data;
using Fail2Ban.API.Interfaces;
using Fail2Ban.API.Models;

namespace Fail2Ban.API.Services;

public class DatabaseService : IDatabaseService
{
    private readonly AppDbContext _context;
    private readonly ILogger<DatabaseService> _logger;
    private readonly AppSettings _settings;

    public DatabaseService(
        AppDbContext context, 
        ILogger<DatabaseService> logger,
        IOptions<AppSettings> settings)
    {
        _context = context;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task InitializeDatabaseAsync()
    {
        await _context.Database.MigrateAsync();
        
        // Add default whitelisted IPs if none exists
        if (!await _context.WhitelistedIps.AnyAsync())
        {
            var defaultWhitelist = _settings.Fail2BanSettings.BeyazListe.Select(ip => new WhitelistedIp
            {
                IpAddress = ip,
                Description = "Default Configuration Whitelist",
                AddedAt = DateTime.Now
            });

            await _context.WhitelistedIps.AddRangeAsync(defaultWhitelist);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Added default whitelisted IPs to database.");
        }
    }

    public async Task<bool> IsIpBannedAsync(string ip)
    {
        return await _context.BanRecords
            .AnyAsync(b => b.IpAddress == ip && b.IsActive && 
                          (b.ExpiresAt == null || b.ExpiresAt > DateTime.Now));
    }

    public async Task<bool> IsIpWhitelistedAsync(string ip)
    {
        return await _context.WhitelistedIps.AnyAsync(w => w.IpAddress == ip);
    }

    public async Task<BanRecord?> BanIpAsync(string ip, string reason, int count, int durationMinutes)
    {
        var existing = await _context.BanRecords.FirstOrDefaultAsync(b => b.IpAddress == ip && b.IsActive);
        
        if (existing != null)
        {
            existing.ExpiresAt = DateTime.Now.AddMinutes(durationMinutes);
            existing.FailedAttemptCount = count;
            existing.Reason = reason;
            await _context.SaveChangesAsync();
            return existing;
        }

        var newRecord = new BanRecord
        {
            IpAddress = ip,
            BannedAt = DateTime.Now,
            ExpiresAt = DateTime.Now.AddMinutes(durationMinutes),
            Reason = reason,
            FailedAttemptCount = count,
            IsActive = true
        };

        await _context.BanRecords.AddAsync(newRecord);
        await _context.SaveChangesAsync();
        return newRecord;
    }

    public async Task UnbanIpAsync(string ip)
    {
        var records = await _context.BanRecords.Where(b => b.IpAddress == ip && b.IsActive).ToListAsync();
        foreach (var record in records)
        {
            record.IsActive = false;
        }
        await _context.SaveChangesAsync();
    }

    public async Task<int> RemoveExpiredBansAsync()
    {
        var expiredBans = await _context.BanRecords
            .Where(b => b.IsActive && b.ExpiresAt != null && b.ExpiresAt <= DateTime.Now)
            .ToListAsync();

        foreach (var ban in expiredBans)
        {
            ban.IsActive = false;
        }

        if (expiredBans.Any())
        {
            await _context.SaveChangesAsync();
        }

        return expiredBans.Count;
    }

    public async Task<List<BanRecord>> GetActiveBansAsync()
    {
        return await _context.BanRecords
            .Where(b => b.IsActive && (b.ExpiresAt == null || b.ExpiresAt > DateTime.Now))
            .ToListAsync();
    }

    public async Task<List<WhitelistedIp>> GetWhitelistedIpsAsync()
    {
        return await _context.WhitelistedIps.OrderByDescending(w => w.Id).ToListAsync();
    }

    public async Task<WhitelistedIp?> AddWhitelistIpAsync(string ip, string description)
    {
        if (await IsIpWhitelistedAsync(ip)) return null;

        var entity = new WhitelistedIp
        {
            IpAddress = ip,
            Description = description,
            AddedAt = DateTime.Now
        };
        await _context.WhitelistedIps.AddAsync(entity);
        await _context.SaveChangesAsync();
        
        // Eğer beyaz listeye ekleniyorsa aktif banı da kaldıralım
        await UnbanIpAsync(ip);
        
        return entity;
    }

    public async Task RemoveWhitelistIpAsync(string ip)
    {
        var entity = await _context.WhitelistedIps.FirstOrDefaultAsync(w => w.IpAddress == ip);
        if (entity != null)
        {
            _context.WhitelistedIps.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<long> GetLogPointerAsync(string filePath)
    {
        var pointer = await _context.LogPointers.FirstOrDefaultAsync(p => p.FilePath == filePath);
        return pointer?.LastReadPosition ?? 0;
    }

    public async Task UpdateLogPointerAsync(string filePath, long position)
    {
        var pointer = await _context.LogPointers.FirstOrDefaultAsync(p => p.FilePath == filePath);
        if (pointer == null)
        {
            await _context.LogPointers.AddAsync(new LogPointer 
            { 
                FilePath = filePath, 
                LastReadPosition = position,
                LastReadAt = DateTime.Now
            });
        }
        else
        {
            pointer.LastReadPosition = position;
            pointer.LastReadAt = DateTime.Now;
        }
        await _context.SaveChangesAsync();
    }

    public async Task<int> RecordFailedAttemptAsync(string ip, string source)
    {
        var attempt = await _context.FailedAttempts
            .FirstOrDefaultAsync(f => f.IpAddress == ip && f.Source == source);

        if (attempt == null)
        {
            attempt = new FailedAttempt
            {
                IpAddress = ip,
                Source = source,
                Count = 1,
                FirstAttemptAt = DateTime.Now,
                LastAttemptAt = DateTime.Now
            };
            await _context.FailedAttempts.AddAsync(attempt);
        }
        else
        {
            // Reset count if it was a very long time ago 
            // (e.g. earlier than ban threshold time)
            if((DateTime.Now - attempt.LastAttemptAt).TotalMinutes > _settings.Fail2BanSettings.EngellemeZamaniDakika)
            {
                attempt.Count = 1;
                attempt.FirstAttemptAt = DateTime.Now;
            }
            else
            {
                attempt.Count++;
            }
            
            attempt.LastAttemptAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();
        return attempt.Count;
    }

    public async Task ResetFailedAttemptsAsync(string ip, string source)
    {
       var attempt = await _context.FailedAttempts
            .FirstOrDefaultAsync(f => f.IpAddress == ip && f.Source == source);

        if (attempt != null)
        {
            _context.FailedAttempts.Remove(attempt);
            await _context.SaveChangesAsync();
        }
    }
}
