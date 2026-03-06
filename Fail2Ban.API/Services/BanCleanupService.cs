using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

public class BanCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BanCleanupService> _logger;

    public BanCleanupService(IServiceProvider serviceProvider, ILogger<BanCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Zaman aşımına uğramış (Expired) Banları Temizleme Servisi Başlatıldı.");

        // 10 dakikada bir kontrol et
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                var fwManager = scope.ServiceProvider.GetRequiredService<IFirewallManager>();

                int deletedCount = await dbService.RemoveExpiredBansAsync();

                if (deletedCount > 0)
                {
                    _logger.LogInformation("{Count} adet süresi dolmuş IP adresi ban listesinden silindi. Firewall senkronize ediliyor.", deletedCount);
                    
                    var activeBans = await dbService.GetActiveBansAsync();
                    await fwManager.SyncFirewallRulesAsync(activeBans.Select(x => x.IpAddress).ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ban temizleme servisinde hata oluştu.");
            }

            // 10 Dakika bekle
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
