using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fail2Ban.API.Configuration;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

/// <summary>
/// Windows Firewall işlemlerini yönetir. 1000'lerce kural oluşturmak yerine, 
/// var olan bir Kural (Rule) nesnesinin içine virgülle (,) ayrılarak binlerce IP bloğu 
/// gömülmesi sağlanır. 
/// Böylece işletim sisteminin Firewall katmanında herhangi bir yavaşlama yaşanmaz.
/// </summary>
public class FirewallManager : IFirewallManager
{
    private readonly ILogger<FirewallManager> _logger;
    private readonly AppSettings _settings;
    
    // Windows Firewall COM Referansları
    private readonly Type _fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2") 
            ?? throw new Exception("Firewall COM objesine erişilemedi.");
            
    private readonly Type _fwRuleType = Type.GetTypeFromProgID("HNetCfg.FwRule") 
            ?? throw new Exception("Firewall Rule COM objesine erişilemedi.");

    public FirewallManager(ILogger<FirewallManager> logger, IOptions<AppSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public Task InitializeAsync()
    {
        // Temizleme ya da boot hazırlığı eklenebilir. Şimdilik senkronize metodu çağırıyoruz gerektiğinde.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Aktif banlı IP listesini alır ve bunları "Fail2Ban_Blocklist_1", "Fail2Ban_Blocklist_2" şeklinde
    /// gruplara ayırarak Windows Firewall üzerine sadece gerekli sayıda kural oluşturarak paketler halinde yazar.
    /// Kural içeriğindeki IP'leri de virgülle birleştirir.
    /// </summary>
    public async Task SyncFirewallRulesAsync(IEnumerable<string> bannedIps)
    {
        try
        {
            await Task.Run(() =>
            {
                var policy2 = Activator.CreateInstance(_fwPolicy2Type);
                if (policy2 == null) return;
                
                dynamic fwPolicy2 = policy2;
                dynamic rules = fwPolicy2.Rules;

                var ipList = bannedIps.ToList();
                var maxPerGroup = _settings.Fail2BanSettings.Firewall.GrupBasinaIpSayisi;
                if (maxPerGroup <= 0) maxPerGroup = 1000;
                
                // Eski Fail2Ban_Blocklist_ kurallarını tamamen temizle ve 0'dan temiz oluştur
                CleanAllFail2BanRules(rules);

                if (!ipList.Any()) 
                {
                    _logger.LogInformation("Silinecek kural yok, tüm blocklist temizlendi.");
                    return;    
                }

                // IP Listesini Paketlere (Chunk) Böl
                var totalGroups = (int)Math.Ceiling(ipList.Count / (double)maxPerGroup);
                
                for (int i = 0; i < totalGroups; i++)
                {
                    var chunk = ipList.Skip(i * maxPerGroup).Take(maxPerGroup).ToList();
                    var ruleName = $"{_settings.Fail2BanSettings.Firewall.GrupKuralOnEki}{i + 1}";
                    var remoteAddresses = string.Join(",", chunk);

                    dynamic newRule = Activator.CreateInstance(_fwRuleType)!;
                    newRule.Name = ruleName;
                    newRule.Description = $"Fail2Ban Otomatik Blocklist Grubu #{i + 1}";
                    newRule.Action = 0; // 0 = Block
                    newRule.Direction = 1; // 1 = Inbound
                    newRule.Enabled = true;
                    newRule.Protocol = 256; // 256 = Any
                    newRule.RemoteAddresses = remoteAddresses;

                    rules.Add(newRule);
                    _logger.LogInformation("Firewall Kuralı: {RuleName} oluşturuldu. İçinde {Count} adet IP bloklandı.", ruleName, chunk.Count);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firewall kuralları senkronize edilirken bir hata oluştu.");
        }
    }

    public Task AddIpToBlocklistAsync(string ip)
    {
        // Anında Firewall'a yazmak performansı bozabileceğinden
        // Bu yapı genel olarak BackgroundTask üzerinden SyncFirewallRulesAsync kullanılarak 
        // veritabanındaki (DatabaseService) listeyle eşitlenecektir.
        _logger.LogInformation("Kuyruğa yeni block talebi geldi: {IP}. Bir sonraki senkronizasyon döngüsünde eklenecek.", ip);
        return Task.CompletedTask;
    }

    public Task RemoveIpFromBlocklistAsync(string ip)
    {
        _logger.LogInformation("Kuyruğa yeni kural kaldırma talebi geldi: {IP}. Bir sonraki senkronizasyon döngüsünde yansıyacak.", ip);
        return Task.CompletedTask;
    }

    private void CleanAllFail2BanRules(dynamic rules)
    {
        var rulePrefix = _settings.Fail2BanSettings.Firewall.GrupKuralOnEki;
        
        // C# dynamic iterasyonu için IEnumerator kullanmak daha güvenli COM objelerinde
        var activeRules = new List<string>();
        foreach (dynamic rule in rules)
        {
            string name = rule.Name;
            if (name.StartsWith(rulePrefix, StringComparison.OrdinalIgnoreCase))
            {
                activeRules.Add(name);
            }
        }

        foreach (var ruleName in activeRules)
        {
            try
            {
                rules.Remove(ruleName);
                _logger.LogDebug("{RuleName} silindi.", ruleName);
            }
            catch(Exception ex)
            {
                _logger.LogWarning("Eski Fail2Ban kuralı ({RuleName}) silinemedi: {Msg}", ruleName, ex.Message);
            }
        }
    }
}
