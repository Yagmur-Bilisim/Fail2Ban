namespace Fail2Ban.API.Configuration;

public class AppSettings
{
    public Fail2BanSettings Fail2BanSettings { get; set; } = new();
    public LogIzlemeSettings LogIzleme { get; set; } = new();
    public EventLogIzlemeSettings EventLogIzleme { get; set; } = new();
    public AbuseIPDBSettings AbuseIPDBSettings { get; set; } = new();
    public OTXSettings OTXSettings { get; set; } = new();
    public DnsLogIzlemeSettings DnsLogIzleme { get; set; } = new();
}

public class Fail2BanSettings
{
    public int MaxHataliGiris { get; set; } = 3;
    public int EngellemeZamaniDakika { get; set; } = 1440; // 24 saat
    public int KontrolAraligiMs { get; set; } = 5000;
    public string LogDosyaYolSablonu { get; set; } = "";
    public List<LogFiltre> LogFiltreler { get; set; } = new();
    public List<string> BeyazListe { get; set; } = new();
    public FirewallSettings Firewall { get; set; } = new();
}

public class LogFiltre
{
    public string Ad { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string IpGrupAdi { get; set; } = "";
    public bool Aktif { get; set; } = true;
    public int? OzelMaxHata { get; set; }
    public int? OzelEngellemeSuresi { get; set; }
}

public class FirewallSettings
{
    public string GrupKuralOnEki { get; set; } = "Fail2Ban_Blocklist_";
    public int GrupBasinaIpSayisi { get; set; } = 1000;
}

public class LogIzlemeSettings
{
    public string IISLogDizini { get; set; } = "";
    public int GecikmeMs { get; set; } = 10000;
    public bool Aktif { get; set; } = true;
}

public class EventLogIzlemeSettings
{
    public bool Aktif { get; set; } = true;
    public List<IzlenenLog> IzlenenLoglar { get; set; } = new();
}

public class IzlenenLog
{
    public string LogAdi { get; set; } = "";
    public string XPathSorgusu { get; set; } = "";
    public string Aciklama { get; set; } = "";
}

public class AbuseIPDBSettings
{
    public string ApiKey { get; set; } = "";
    public string ApiUrl { get; set; } = "https://api.abuseipdb.com/api/v2";
    public int Kategori { get; set; } = 18;
    public bool CheckAktif { get; set; } = true;
    public bool ReportAktif { get; set; } = true;
    public int MinSpamSkoruSarti { get; set; } = 50; // Abuse Confidence Score 
    public Dictionary<string, string> SistemMesajlari { get; set; } = new();
}

public class OTXSettings
{
    public string ApiKey { get; set; } = "";
    public string ApiUrl { get; set; } = "https://otx.alienvault.com/api/v1/indicators/IPv4";
    public bool CheckAktif { get; set; } = true;
    /// <summary>
    /// OTX'te kaç farklı pulse (tehdit istihbaratı kaynağı) bu IP'yi işaretlemişse tehdit sayılsın.
    /// Varsayılan: 1 (herhangi bir pulse yeterliyse ban)
    /// </summary>
    public int MinPulseCount { get; set; } = 1;
}

public class DnsLogIzlemeSettings
{
    public bool Aktif { get; set; } = false;

    /// <summary>
    /// DNS log dosyasının tam yolu.
    /// Windows DNS Server: C:\Windows\System32\dns\dns.log
    /// BIND (named) Windows port: log dosya yolunu buraya girin.
    /// </summary>
    public string LogDosyaYolu { get; set; } = @"C:\Windows\System32\dns\dns.log";

    public int GecikmeMs { get; set; } = 10000;
    public int MaxHataliIstek { get; set; } = 10;
    public int BanSuresiDakika { get; set; } = 1440;

    /// <summary>
    /// Eşleştirilecek regex desenleri. Her biri bir tehdit türünü temsil eder.
    /// named (BIND) ve Windows DNS Server formatları desteklenir.
    /// </summary>
    public List<DnsLogFiltre> Filtreler { get; set; } = new();
}

public class DnsLogFiltre
{
    public string Ad { get; set; } = "";
    /// <summary>
    /// Regex deseni. IP adresi için (?&lt;ip&gt;...) adlı grubu kullanın.
    /// </summary>
    public string Pattern { get; set; } = "";
    public bool Aktif { get; set; } = true;
}
