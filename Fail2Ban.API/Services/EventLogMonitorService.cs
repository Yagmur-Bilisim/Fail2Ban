using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Fail2Ban.API.Configuration;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

/// <summary>
/// Sistem kaynaklarını (CPU/RAM) neredeyse hiç kullanmadan, sıfır maliyetle
/// Windows Event Log'ları üzerinden belirtilen XPath sorgularını dinleyen servis.
/// Eski System.Diagnostics.EventLog yerine daha modern ve yüksek performanslı olan 
/// System.Diagnostics.Eventing.Reader alt yapısını kullanır.
/// </summary>
public class EventLogMonitorService : BackgroundService
{
    private readonly ILogger<EventLogMonitorService> _logger;
    private readonly AppSettings _settings;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<EventLogWatcher> _watchers = new();

    public EventLogMonitorService(
        ILogger<EventLogMonitorService> logger, 
        IOptions<AppSettings> settings,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _settings = settings.Value;
        _serviceProvider = serviceProvider;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EventLogIzleme.Aktif)
        {
            _logger.LogInformation("Event Log İzleme (EventLogWatcher) Kapalı.");
            return Task.CompletedTask;
        }

        foreach (var cfg in _settings.EventLogIzleme.IzlenenLoglar)
        {
            try
            {
                var query = new EventLogQuery(cfg.LogAdi, PathType.LogName, cfg.XPathSorgusu);
                var watcher = new EventLogWatcher(query);

                watcher.EventRecordWritten += (sender, e) =>
                {
                    if (e.EventException == null)
                    {
                        Task.Run(() => HandleEventRecordAsync(e.EventRecord, cfg.Aciklama, stoppingToken), stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(e.EventException, "Event okuma sırasında bir XPath hatası meydana geldi.");
                    }
                };

                watcher.Enabled = true;
                _watchers.Add(watcher);
                _logger.LogInformation("Yüksek performanslı Event Log Dinleyicisi Hazır - {LogAdi}", cfg.LogAdi);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "XPath Event Log {LogAdi} başlatılamadı.", cfg.LogAdi);
            }
        }

        return Task.CompletedTask;
    }

    private async Task HandleEventRecordAsync(EventRecord record, string reason, CancellationToken token)
    {
        try
        {
            ExtractIpFromEventData(record, out string ip);
            if (string.IsNullOrEmpty(ip)) return;

            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
            var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
            var fwManager = scope.ServiceProvider.GetRequiredService<IFirewallManager>();
            var abuseService = scope.ServiceProvider.GetRequiredService<IAbuseIPDBService>();

            if (await dbService.IsIpWhitelistedAsync(ip))
            {
                // Beyaz listedeki IP'ye dokunmuyoruz.
                return;
            }

            if (await dbService.IsIpBannedAsync(ip)) return;

            var tryCount = await dbService.RecordFailedAttemptAsync(ip, reason);
            _logger.LogWarning("IP {Ip} için başarısız giriş sayısı: {Count} ({Reason})", ip, tryCount, reason);

            if (tryCount >= _settings.Fail2BanSettings.MaxHataliGiris)
            {
                // AbuseIPDB (Spam List) Kontrolü
                bool isSpamOk = false;
                if(await redisService.IsCachedSpamIpAsync(ip)) 
                {
                    isSpamOk = true; 
                    _logger.LogWarning("IP Redis'den doğrudan (Spam=1) olarak alındı: {IP}", ip);
                }
                else
                {
                    isSpamOk = await abuseService.CheckIpSpamScoreAsync(ip);
                    if(isSpamOk) await redisService.CacheSpamIpAsync(ip, TimeSpan.FromDays(1)); // Spam ise 1 gün Redis Cache'de tut     
                    else await redisService.CacheSafeIpAsync(ip, TimeSpan.FromMinutes(5)); // Safe ise 5 dk redis'te tut.
                }
                
                // IP Banla (Block)
                var newBan = await dbService.BanIpAsync(ip, reason + (isSpamOk ? " - (AbuseIPDB Spam Onaylı)" : ""), tryCount, _settings.Fail2BanSettings.EngellemeZamaniDakika);
                if (newBan != null)
                {
                    // Firewall Toplu (Batch) Listesini Güncelle
                    var allBans = await dbService.GetActiveBansAsync();
                    await fwManager.SyncFirewallRulesAsync(allBans.Select(b => b.IpAddress).ToList());
                    
                    // Raporla
                    await abuseService.ReportIpAsync(ip, reason, tryCount);
                    newBan.IsAbuseReported = true;
                    await dbService.ResetFailedAttemptsAsync(ip, reason);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Olay kaydedilirken bir hata oluştu.");
        }
        finally
        {
            record.Dispose();
        }
    }

    private void ExtractIpFromEventData(EventRecord record, out string ip)
    {
        ip = string.Empty;
        var msg = record.FormatDescription();
        if (string.IsNullOrEmpty(msg)) return;

        // Windows Log Extract (IP Bulma)
        var match = Regex.Match(msg, @"(?:Source Network Address|Client Address|IP Address):\s*([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            ip = match.Groups[1].Value;
        }
        else
        {
            match = Regex.Match(msg, @"\[CLIENT:\s*([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})\]", RegexOptions.IgnoreCase);
            if(match.Success) ip = match.Groups[1].Value;
        }

        // Validate IP limits
        if (ip.StartsWith("127.") || ip == "0.0.0.0" || string.IsNullOrWhiteSpace(ip))
        {
            ip = string.Empty; // Skip local/empty
        }
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Enabled = false;
            watcher.Dispose();
        }
        base.Dispose();
    }
}
