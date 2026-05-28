using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Fail2Ban.API.Configuration;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

public class SmtpLogMonitorService : BackgroundService
{
    private readonly ILogger<SmtpLogMonitorService> _logger;
    private readonly AppSettings _settings;
    private readonly IServiceProvider _serviceProvider;

    public SmtpLogMonitorService(
        ILogger<SmtpLogMonitorService> logger,
        IOptions<AppSettings> settings,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _settings = settings.Value;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mailConfig = _settings.Fail2BanSettings;
        var sablon = mailConfig.LogDosyaYolSablonu;

        if (string.IsNullOrEmpty(sablon) || mailConfig.LogFiltreler == null || mailConfig.LogFiltreler.Count == 0)
        {
            _logger.LogWarning("MailEnable SMTP Log yolu (LogDosyaYolSablonu) veya filtreler ayarlanmadığı için Mail izleme servisi çalışmıyor.");
            return;
        }

        _logger.LogInformation("SMTP (Mail Enable) Log Monitor Service Başlatıldı.");

        string currentLogFile = "";
        long lastPointer = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // MailEnable genelde tarih formatına göre log açar: SMTP-Activity-260306.log  (YYMMDD)
                // Appsettings içerisindeki {0} parametresi "yyMMdd" formatıyla değiştirilir.
                var dateStr = DateTime.Now.ToString("yyMMdd");
                string logFilePath = string.Format(sablon, dateStr);

                if (!File.Exists(logFilePath))
                {
                    await Task.Delay(mailConfig.KontrolAraligiMs, stoppingToken);
                    continue;
                }

                if (currentLogFile != logFilePath)
                {
                    currentLogFile = logFilePath;
                    // Yeni dosyaya geçtiyse, pointer'ı ya db'den al yada sondan başla
                    using var s2 = _serviceProvider.CreateScope();
                    var dbS2 = s2.ServiceProvider.GetRequiredService<IDatabaseService>();
                    lastPointer = await dbS2.GetLogPointerAsync(logFilePath);
                }

                using var scope = _serviceProvider.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                var abuseService = scope.ServiceProvider.GetRequiredService<IAbuseIPDBService>();
                var otxService = scope.ServiceProvider.GetRequiredService<IOTXService>();
                var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
                var fwManager = scope.ServiceProvider.GetRequiredService<IFirewallManager>();

                // Paylaşımlı okuma kilidi ile açıyoruz
                using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                
                if (fs.Length < lastPointer) lastPointer = 0; // Dosya temizlenmiş

                // Sistem devasa eski logları (5-6gb ise) ilk kez okumaya kalkmasın, direkt bugünden başlasın
                if (lastPointer == 0 && fs.Length > 0)
                {
                    lastPointer = fs.Length;
                    await dbService.UpdateLogPointerAsync(logFilePath, lastPointer);
                }

                if (fs.Length == lastPointer) 
                {
                    await Task.Delay(mailConfig.KontrolAraligiMs, stoppingToken);
                    continue; // Yeni log yok
                }
                    
                fs.Seek(lastPointer, SeekOrigin.Begin);
                
                using var reader = new StreamReader(fs);
                string? line;
                bool dbNeedsUpdate = false;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    foreach (var filter in mailConfig.LogFiltreler)
                    {
                        if (!filter.Aktif) continue;

                        var match = Regex.Match(line, filter.Pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string clientIp = match.Groups[filter.IpGrupAdi].Value;

                            if (string.IsNullOrEmpty(clientIp) || clientIp == "127.0.0.1" || clientIp == "::1") 
                                continue;

                            // Beyaz liste kontrolü
                            if (await dbService.IsIpWhitelistedAsync(clientIp)) continue;
                            if (await dbService.IsIpBannedAsync(clientIp)) continue;

                            string catchReason = $"SMTP Saldırısı ({filter.Ad})";
                            var maxLimit = filter.OzelMaxHata ?? mailConfig.MaxHataliGiris;
                            var banDuration = filter.OzelEngellemeSuresi ?? mailConfig.EngellemeZamaniDakika;

                            var tryCount = await dbService.RecordFailedAttemptAsync(clientIp, catchReason);
                            
                            _logger.LogWarning("SMTP Mail Tehdit Tespit: IP: {ClientIP} | Reason: {Rule} | Sayaç: {Count}/{MaxLimit}", clientIp, filter.Ad, tryCount, maxLimit);

                            if (tryCount >= maxLimit)
                            {
                                bool isSpamOk = false;
                                if(await redisService.IsCachedSpamIpAsync(clientIp)) 
                                    isSpamOk = true; 
                                else
                                {
                                    // AbuseIPDB ve OTX paralel sorgula — ikisinden biri tehdit derse yeterli
                                    var abuseTask = abuseService.CheckIpSpamScoreAsync(clientIp);
                                    var otxTask   = otxService.CheckIpThreatAsync(clientIp);
                                    await Task.WhenAll(abuseTask, otxTask);
                                    isSpamOk = abuseTask.Result || otxTask.Result;

                                    if(isSpamOk) await redisService.CacheSpamIpAsync(clientIp, TimeSpan.FromDays(1));
                                    else await redisService.CacheSafeIpAsync(clientIp, TimeSpan.FromMinutes(5));
                                }

                                var newBan = await dbService.BanIpAsync(clientIp, catchReason, tryCount, banDuration);
                                if (newBan != null)
                                {
                                    await fwManager.AddIpToBlocklistAsync(clientIp);
                                    var allBans = await dbService.GetActiveBansAsync();
                                    await fwManager.SyncFirewallRulesAsync(allBans.Select(b => b.IpAddress).ToList());
                                    await dbService.ResetFailedAttemptsAsync(clientIp, catchReason);
                                    
                                    _logger.LogInformation("IP {ClientIP} ({Rule}) sebebiyle banlandı.", clientIp, filter.Ad);
                                    await abuseService.ReportIpAsync(clientIp, catchReason, tryCount);
                                }
                            }
                        }
                    }
                }

                lastPointer = fs.Position;
                await dbService.UpdateLogPointerAsync(logFilePath, lastPointer);
            }
            catch (Exception ex)
            {
                 // Dosya diğer process tarafından tutuluyor vs hatalarını direkt yutuyoruz, döngü tekrar deneyecek
                _logger.LogTrace("SMTP Monitor hatası (Geçici file-lock olabilir): {Msg}", ex.Message);
            }

            await Task.Delay(mailConfig.KontrolAraligiMs, stoppingToken);
        }
    }
}
