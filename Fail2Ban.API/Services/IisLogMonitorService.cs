using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Fail2Ban.API.Configuration;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

/// <summary>
/// IIS Loglarını tarar. Program her başladığında baştan okumasını engellemek için,
/// SQLite veritabanındaki LogPointer tablosunu kullanarak en son okuduğu byte sırasından devam eder.
/// </summary>
public class IisLogMonitorService : BackgroundService
{
    private readonly ILogger<IisLogMonitorService> _logger;
    private readonly AppSettings _settings;
    private readonly IServiceProvider _serviceProvider;

    public IisLogMonitorService(
        ILogger<IisLogMonitorService> logger, 
        IOptions<AppSettings> settings,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _settings = settings.Value;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.LogIzleme.Aktif || string.IsNullOrWhiteSpace(_settings.LogIzleme.IISLogDizini))
        {
            _logger.LogInformation("IIS Log İzleme Kapalı veya Dizin Belirtilmedi.");
            return;
        }

        if (!Directory.Exists(_settings.LogIzleme.IISLogDizini))
        {
            _logger.LogWarning("IIS Log Dizini Bulunamadı: {Path}", _settings.LogIzleme.IISLogDizini);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var files = Directory.GetFiles(_settings.LogIzleme.IISLogDizini, "*.log", SearchOption.AllDirectories)
                                     .OrderByDescending(f => File.GetLastWriteTime(f))
                                     .Take(2) // Sadece en güncel 2 log dosyasına bak (Genelde günlük tutulur)
                                     .ToList();

                foreach (var logFile in files)
                {
                    await ProcessLogFileAsync(logFile, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IIS Logları okunurken hata meydana geldi.");
            }

            // Bekleme (Kontrol Aralığı)
            await Task.Delay(_settings.LogIzleme.GecikmeMs, stoppingToken);
        }
    }

    private async Task ProcessLogFileAsync(string logFilePath, CancellationToken stoppingToken)
    {
        try
        {
            // Yeni bir scope oluştur (Singleton BackgroundService içinde Scoped servisleri çağırmak için)
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
            var fwManager = scope.ServiceProvider.GetRequiredService<IFirewallManager>();
            var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
            var abuseService = scope.ServiceProvider.GetRequiredService<IAbuseIPDBService>();

            // En son nerede kaldık? (Pointer)
            long lastPointer = await dbService.GetLogPointerAsync(logFilePath);

            // Log dosyasını FileShare.ReadWrite ile aç ki IIS yazmaya devam edebilsin
            using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            // Eğer dosya boyutu pointer'dan küçükse (Log silinmiş/truncate edilmiş demektir)
            // Pointer'ı sıfırla
            if (fs.Length < lastPointer) lastPointer = 0;

            // Eğer sistem ilk kez çalışıyorsa veya yeni bir log dosyasına geçtiyse ve 
            // eski log çok büyükse (5-6 GB gibi), en başından başlayıp günlerce okumaması için 
            // pointer'ı dosyanın direkt sonuna alıyoruz (Sadece o andan itibaren gelen CANLI saldırıları yakalaması için)
            if (lastPointer == 0 && fs.Length > 0)
            {
                lastPointer = fs.Length;
                await dbService.UpdateLogPointerAsync(logFilePath, lastPointer);
            }

            if (fs.Length == lastPointer) 
                return; // Yeni log yok
                
            fs.Seek(lastPointer, SeekOrigin.Begin);
            
            using var reader = new StreamReader(fs);
            string? line;
            
            while ((line = await reader.ReadLineAsync(stoppingToken)) != null)
            {
                // IIS Log Analizi:
                // Örnek IIS Log: "2023-10-09 15:43:21 192.168.1.10 GET /auth/login - 443 - 85.122.x.x Mozilla..."
                // Regex ile Client-IP'yi ve HTTP Status'u bul (Örn: 401 Unauthorized, 403 Forbidden vb.)
                
                if (line.StartsWith("#")) continue; // Yorum Satırı
                
                    // Yeni Log Yapısına göre Uri Path ve IP eşleştirme (Go-http-client vs engellemesi dahil)
                    // Örnek Log: 2024-04-26 11:55:58 212.64.223.219 GET /.ssh/id_rsa - 443 - 109.202.99.41 Go-http-client/1.1 - 404 0 2 93
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    
                    if (parts.Length >= 14)
                    {
                        var method = parts[3]; // GET, POST vs.
                        var uriPath = parts[4]; // /.ssh/id_rsa vb.
                        var clientIp = parts[8]; // Client IP c-ip (standartta 8 veya 9 olabilir. Verilen loga göre indeks 8 de C-IP: 109.202.99.41 görünüyor. Not: s-ip var c-ip var.
                        var userAgent = parts.Length > 9 ? parts[9] : ""; // Go-http-client/1.1
                        var httpStatus = parts.Length > 11 ? parts[11] : ""; // 404 vb.
                        
                        // User Agent Tanınmayanları Doğrudan Blokla
                        bool isBadUserAgent = userAgent == "-" || userAgent.Contains("Go-http-client") || userAgent.Contains("libredtail") || userAgent.Contains("curl") || userAgent.Contains("wget") || userAgent.Contains("python-requests") || userAgent.Contains("zgrab");
                        
                        // Kötü niyetli path kontrolleri (Örn: /php_info.php, /hello.world, .php uzantılı riskli yollar ekstra eklenebilir)
                        bool isBadUri = uriPath.StartsWith("/.git") || uriPath.StartsWith("/.ssh") || uriPath.EndsWith(".env") || 
                                        uriPath.EndsWith(".sql") || uriPath.EndsWith(".tar.gz") || 
                                        uriPath.Contains("wp-admin");
                                        
                        // 401 ve 403 yetkilendirme / yasaklı içerik hataları genelde normal kullanıcılardan da kaynaklanabildiği için dışarıda bırakılır.
                        bool isLegitimateError = httpStatus == "401" || httpStatus == "403";
                        bool isSuspicious = (isBadUserAgent || isBadUri) && !isLegitimateError;
                        
                        if (isSuspicious) 
                        {
                            if(clientIp == "127.0.0.1" || clientIp == "::1") continue;

                            // Beyaz Liste veya zaten banlıysa atla
                            if(await dbService.IsIpWhitelistedAsync(clientIp)) continue;
                            if(await dbService.IsIpBannedAsync(clientIp)) continue;

                            string catchReason = isBadUserAgent ? "Zararlı Yazılım / Bot" : "Şüpheli Dizin Taraması";
                            // Sadece log kaydı tutmak (DB'de istatistik için) sayacı artırıyoruz ama ban için sayacı beklemiyoruz.
                            var tryCount = await dbService.RecordFailedAttemptAsync(clientIp, "IIS Log - " + catchReason);
                            
                            _logger.LogWarning("IIS Kritik Tehdit Tespit Edildi (Anında Ban): IP: {ClientIP} | Path: {Uri} | Status: {Status}", clientIp, uriPath, httpStatus);

                            // Direkt Ban Sürecini Başlat (Sayaç beklemeden)
                            bool isSpamOk = false;
                            if(await redisService.IsCachedSpamIpAsync(clientIp)) 
                                isSpamOk = true; 
                            else
                            {
                                isSpamOk = await abuseService.CheckIpSpamScoreAsync(clientIp);
                                if(isSpamOk) await redisService.CacheSpamIpAsync(clientIp, TimeSpan.FromDays(1));
                                else await redisService.CacheSafeIpAsync(clientIp, TimeSpan.FromMinutes(5));
                            }

                            var newBan = await dbService.BanIpAsync(clientIp, catchReason, tryCount, _settings.Fail2BanSettings.EngellemeZamaniDakika);
                            if(newBan != null)
                            {
                                await fwManager.AddIpToBlocklistAsync(clientIp); // Tüm kuralları yenilemek yerine sadece bunu listeye pasla (Opsiyonel: SyncFilewall)
                                var allBans = await dbService.GetActiveBansAsync();
                                await fwManager.SyncFirewallRulesAsync(allBans.Select(b => b.IpAddress).ToList());
                                await dbService.ResetFailedAttemptsAsync(clientIp, "IIS Log - " + catchReason);
                                
                                // Sırf Report aktifse atıyoruz
                                _logger.LogInformation("IP {ClientIP} Anında Banlandı ({Reason}).", clientIp, catchReason);
                                await abuseService.ReportIpAsync(clientIp, catchReason, tryCount);
                            }
                        }
                    }
            }

            // Dosya okuma bitti, Pointer'ı kaydet
            await dbService.UpdateLogPointerAsync(logFilePath, fs.Position);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Log dosyası işlenirken bir hata oluştu: {File}", logFilePath);
        }
    }
}
