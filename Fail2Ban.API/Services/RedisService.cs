using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

public class RedisService : IRedisService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;

    public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<bool> IsCachedSpamIpAsync(string ip)
    {
        var result = await _db.StringGetAsync($"spam_ip:{ip}");
        return result.HasValue && result == "1";
    }

    public async Task CacheSpamIpAsync(string ip, TimeSpan expiration)
    {
        await _db.StringSetAsync($"spam_ip:{ip}", "1", expiration);
    }
    
    public async Task<bool> CacheSafeIpAsync(string ip, TimeSpan expiration)
    {
        // Safe Olarak Bulduğumuzu 5 Dakika vs tutabiliriz. 
        return await _db.StringSetAsync($"safe_ip:{ip}", "1", expiration);
    }

    public async Task<bool> AcquireLockAsync(string resource, TimeSpan duration)
    {
        return await _db.LockTakeAsync($"lock:{resource}", Environment.MachineName, duration);
    }

    public async Task ReleaseLockAsync(string resource)
    {
        await _db.LockReleaseAsync($"lock:{resource}", Environment.MachineName);
    }
}
