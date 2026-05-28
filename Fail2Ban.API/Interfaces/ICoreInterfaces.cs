using Fail2Ban.API.Models;

namespace Fail2Ban.API.Interfaces;

public interface IDatabaseService
{
    Task InitializeDatabaseAsync();
    Task<bool> IsIpBannedAsync(string ip);
    Task<bool> IsIpWhitelistedAsync(string ip);
    Task<BanRecord?> BanIpAsync(string ip, string reason, int count, int durationMinutes);
    Task UnbanIpAsync(string ip);
    Task<int> RemoveExpiredBansAsync();
    Task<List<BanRecord>> GetActiveBansAsync();
    
    // Whitelist Operations
    Task<List<WhitelistedIp>> GetWhitelistedIpsAsync();
    Task<WhitelistedIp?> AddWhitelistIpAsync(string ip, string description);
    Task RemoveWhitelistIpAsync(string ip);
    
    // Log Pointer Operations
    Task<long> GetLogPointerAsync(string filePath);
    Task UpdateLogPointerAsync(string filePath, long position);
    
    // Failed Attempts Operations
    Task<int> RecordFailedAttemptAsync(string ip, string source);
    Task ResetFailedAttemptsAsync(string ip, string source);
}

public interface IFirewallManager
{
    Task InitializeAsync();
    Task SyncFirewallRulesAsync(IEnumerable<string> bannedIps);
    Task AddIpToBlocklistAsync(string ip);
    Task RemoveIpFromBlocklistAsync(string ip);
}

public interface IAbuseIPDBService
{
    Task<bool> CheckIpSpamScoreAsync(string ipAddress);
    Task ReportIpAsync(string ipAddress, string source, int attemptCount);
}

public interface IOTXService
{
    /// <summary>
    /// OTX AlienVault'ta IP'nin kaç pulse tarafından tehdit olarak işaretlendiğini kontrol eder.
    /// true → tehdit tespit edildi, ban uygulanmalı.
    /// </summary>
    Task<bool> CheckIpThreatAsync(string ipAddress);
}

public interface IRedisService
{
    Task<bool> IsCachedSpamIpAsync(string ip);
    Task CacheSpamIpAsync(string ip, TimeSpan expiration);
    Task<bool> CacheSafeIpAsync(string ip, TimeSpan expiration); // Safe IP'leri de cacheleyelim gereksiz abuse'ye çıkmamak için
    Task<bool> AcquireLockAsync(string resource, TimeSpan duration);
    Task ReleaseLockAsync(string resource);
}
