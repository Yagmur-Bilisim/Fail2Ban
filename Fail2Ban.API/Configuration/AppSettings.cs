namespace Fail2Ban.API.Configuration;

public class AppSettings
{
    public Fail2BanSettings Fail2BanSettings { get; set; } = new();
    public LogIzlemeSettings LogIzleme { get; set; } = new();
    public EventLogIzlemeSettings EventLogIzleme { get; set; } = new();
    public AbuseIPDBSettings AbuseIPDBSettings { get; set; } = new();
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
