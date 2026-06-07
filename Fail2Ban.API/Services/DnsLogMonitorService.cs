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
/// DNS log dosyasını izleyerek şüpheli sorguları (cache reddedilen, zone transfer denemeleri vb.)
/// tespit eder ve ban sistemine entegre eder.
/// 
/// Desteklenen formatlar:
///   - Windows DNS Server (dns.log)
///   - BIND / named (syslog veya named.log)
/// 
/// appsettings.json → DnsLogIzleme bölümünden yapılandırılır.
/// </summary>
public class DnsLogMonitorService : BackgroundService
{
    private readonly ILogger<DnsLogMonitorService> _logger;
    private readonly AppSettings _settings;
    private readonly IServiceProvider _serviceProvider;

    public DnsLogMonitorService(
        ILogger<DnsLogMonitorService> logger,
        IOptions<AppSettings> settings,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _settings = settings.Value;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = _settings.DnsLogIzleme;

        if (!cfg.Aktif)
        {
            _logger.LogInformation("DNS Log İzleme kapalı. (DnsLogIzleme.Aktif = false)");
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.LogDosyaYolu))
        {
            _logger.LogWarning("DNS Log dosya yolu belirtilmemiş. (DnsLogIzleme.LogDosyaYolu)");
            return;
        }

        var aktifFiltreler = cfg.Filtreler.Where(f => f.Aktif && !string.IsNullOrWhiteSpace(f.Pattern)).ToList();
        if (aktifFiltreler.Count == 0)
        {
            _logger.LogWarning("DNS Log İzleme için aktif filtre tanımlanmamış.");
            return;
        }

        // Regex'leri bir kez derle
        var derlenmisFiltreleri = aktifFiltreler.Select(f => new
        {
            f.Ad,
            Regex = new Regex(f.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase)
        }).ToList();

        _logger.LogInformation("DNS Log İzleme Başlatıldı → {Path}", cfg.LogDosyaYolu);

        long lastPointer = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!File.Exists(cfg.LogDosyaYolu))
                {
                    _logger.LogWarning("DNS log dosyası bulunamadı: {Path}", cfg.LogDosyaYolu);
                    await Task.Delay(cfg.GecikmeMs, stoppingToken);
                    continue;
                }

                using var scope = _serviceProvider.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                var abuseService = scope.ServiceProvider.GetRequiredService<IAbuseIPDBService>();
                var otxService = scope.ServiceProvider.GetRequiredService<IOTXService>();
                var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
                var fwManager = scope.ServiceProvider.GetRequiredService<IFirewallManager>();

                using var fs = new FileStream(cfg.LogDosyaYolu, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                if (fs.Length < lastPointer) lastPointer = 0; // Dosya rotate edilmiş

                // İlk çalışmada mevcut log kütlesini atla, sadece yeni logları izle
                if (lastPointer == 0 && fs.Length > 0)
                {
                    lastPointer = fs.Length;
                    await dbService.UpdateLogPointerAsync(cfg.LogDosyaYolu, lastPointer);
                    await Task.Delay(cfg.GecikmeMs, stoppingToken);
                    continue;
                }

                if (fs.Length == lastPointer)
                {
                    await Task.Delay(cfg.GecikmeMs, stoppingToken);
                    continue;
                }

                fs.Seek(lastPointer, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                string? line;

                while ((line = await reader.ReadLineAsync(stoppingToken)) != null)
                {
                    foreach (var filtre in derlenmisFiltreleri)
                    {
                        var match = filtre.Regex.Match(line);
                        if (!match.Success) continue;

                        // IP'yi "ip" adlı named group'tan al
                        var ip = match.Groups["ip"].Value;
                        if (string.IsNullOrWhiteSpace(ip)) continue;
                        if (ip == "127.0.0.1" || ip == "::1" || ip.StartsWith("127.")) continue;

                        if (await dbService.IsIpWhitelistedAsync(ip)) continue;
                        if (await dbService.IsIpBannedAsync(ip)) continue;

                        var catchReason = $"DNS Saldırısı ({filtre.Ad})";
                        var tryCount = await dbService.RecordFailedAttemptAsync(ip, catchReason);

                        _logger.LogWarning("DNS Tehdit Tespit: IP: {IP} | Kural: {Rule} | Sayaç: {Count}/{Max}",
                            ip, filtre.Ad, tryCount, cfg.MaxHataliIstek);

                        if (tryCount >= cfg.MaxHataliIstek)
                        {
                            bool isSpamOk = false;
                            if (await redisService.IsCachedSpamIpAsync(ip))
                            {
                                isSpamOk = true;
                            }
                            else
                            {
                                var abuseTask = abuseService.CheckIpSpamScoreAsync(ip);
                                var otxTask = otxService.CheckIpThreatAsync(ip);
                                await Task.WhenAll(abuseTask, otxTask);
                                isSpamOk = abuseTask.Result || otxTask.Result;

                                if (isSpamOk) await redisService.CacheSpamIpAsync(ip, TimeSpan.FromDays(1));
                                else await redisService.CacheSafeIpAsync(ip, TimeSpan.FromMinutes(5));
                            }

                            var banNedeni = catchReason + (isSpamOk ? " - (Tehdit İstihbaratı Onaylı)" : "");
                            var newBan = await dbService.BanIpAsync(ip, banNedeni, tryCount, cfg.BanSuresiDakika);

                            if (newBan != null)
                            {
                                await fwManager.AddIpToBlocklistAsync(ip);
                                var allBans = await dbService.GetActiveBansAsync();
                                await fwManager.SyncFirewallRulesAsync(allBans.Select(b => b.IpAddress).ToList());
                                await dbService.ResetFailedAttemptsAsync(ip, catchReason);

                                _logger.LogInformation("IP {IP} DNS saldırısı nedeniyle banlandı ({Rule}).", ip, filtre.Ad);
                                await abuseService.ReportIpAsync(ip, catchReason, tryCount);
                            }
                        }
                        break; // Bir satır birden fazla filtreyle eşleşse de tek sayılsın
                    }
                }

                lastPointer = fs.Position;
                await dbService.UpdateLogPointerAsync(cfg.LogDosyaYolu, lastPointer);
            }
            catch (Exception ex)
            {
                _logger.LogTrace("DNS Monitor geçici hata (file-lock olabilir): {Msg}", ex.Message);
            }

            await Task.Delay(cfg.GecikmeMs, stoppingToken);
        }
    }
}
