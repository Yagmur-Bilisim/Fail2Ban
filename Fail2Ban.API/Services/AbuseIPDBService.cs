using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fail2Ban.API.Configuration;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

public class AbuseIPDBService : IAbuseIPDBService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AbuseIPDBService> _logger;
    private readonly AppSettings _settings;

    public AbuseIPDBService(
        HttpClient httpClient, 
        ILogger<AbuseIPDBService> logger, 
        IOptions<AppSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;

        _httpClient.DefaultRequestHeaders.Add("Key", _settings.AbuseIPDBSettings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<bool> CheckIpSpamScoreAsync(string ipAddress)
    {
        if (!_settings.AbuseIPDBSettings.CheckAktif) return false;

        try
        {
            var apiUrl = _settings.AbuseIPDBSettings.ApiUrl;
            if(!apiUrl.EndsWith("/")) apiUrl += "/";
            var requestUrl = $"{apiUrl}check?ipAddress={ipAddress}&maxAgeInDays=90";

            var response = await _httpClient.GetAsync(requestUrl);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AbuseCheckResponse>();
                if (result?.Data != null)
                {
                    _logger.LogInformation("IP {IpAddress} AbuseIPDB Skoru: {Score}", ipAddress, result.Data.AbuseConfidenceScore);
                    return result.Data.AbuseConfidenceScore >= _settings.AbuseIPDBSettings.MinSpamSkoruSarti;
                }
            }
            else
            {
                var errContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("AbuseIPDB Kontrol Hatası: {StatusCode} - {Content}", response.StatusCode, errContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AbuseIPDB Kontrolünde hata oluştu. IP: {IpAddress} Detay: {Message}", ipAddress, ex.Message);
        }

        return false;
    }

    public async Task ReportIpAsync(string ipAddress, string source, int attemptCount)
    {
        if (!_settings.AbuseIPDBSettings.ReportAktif) return;

        try
        {
            var msgTemplate = _settings.AbuseIPDBSettings.SistemMesajlari.GetValueOrDefault("Default");
            if (string.IsNullOrEmpty(msgTemplate)) return;

            var message = string.Format(msgTemplate, ipAddress, _settings.Fail2BanSettings.EngellemeZamaniDakika, DateTime.Now, attemptCount);
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("ip", ipAddress),
                new KeyValuePair<string, string>("categories", _settings.AbuseIPDBSettings.Kategori.ToString()),
                new KeyValuePair<string, string>("comment", message)
            });

            var apiUrl = _settings.AbuseIPDBSettings.ApiUrl;
            if(!apiUrl.EndsWith("/")) apiUrl += "/";
            var requestUrl = $"{apiUrl}report";

            var response = await _httpClient.PostAsync(requestUrl, content);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("IP {IpAddress} AbuseIPDB'ye başarıyla raporlandı.", ipAddress);
            }
            else
            {
                var errContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("AbuseIPDB Raporlama Hatası: {StatusCode} - {Content}", response.StatusCode, errContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AbuseIPDB Raporlanırken hata oluştu. IP: {IpAddress}", ipAddress);
        }
    }
}

public class AbuseCheckResponse
{
    public AbuseData Data { get; set; } = new();
}

public class AbuseData
{
    public int AbuseConfidenceScore { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}
