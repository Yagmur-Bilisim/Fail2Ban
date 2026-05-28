using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fail2Ban.API.Configuration;
using Fail2Ban.API.Interfaces;

namespace Fail2Ban.API.Services;

/// <summary>
/// AlienVault OTX (Open Threat Exchange) entegrasyonu.
/// Bir IP adresinin OTX'teki tehdit istihbaratı pulse sayısını sorgular.
/// Endpoint: GET /api/v1/indicators/IPv4/{ip}/reputation
/// </summary>
public class OTXService : IOTXService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OTXService> _logger;
    private readonly OTXSettings _settings;

    public OTXService(
        HttpClient httpClient,
        ILogger<OTXService> logger,
        IOptions<AppSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value.OTXSettings;

        _httpClient.DefaultRequestHeaders.Add("X-OTX-API-KEY", _settings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<bool> CheckIpThreatAsync(string ipAddress)
    {
        if (!_settings.CheckAktif || string.IsNullOrWhiteSpace(_settings.ApiKey))
            return false;

        try
        {
            // /api/v1/indicators/IPv4/{ip}/reputation — genel itibar + pulse sayısı
            var url = $"{_settings.ApiUrl.TrimEnd('/')}/{ipAddress}/reputation";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OTX sorgu hatası: {StatusCode} — IP: {IP}", response.StatusCode, ipAddress);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OTXReputationResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null) return false;

            var pulseCount = result.PulseInfo?.Count ?? 0;
            var threatScore = result.ThreatScore ?? 0;

            _logger.LogInformation(
                "OTX Sonucu — IP: {IP} | Pulse: {Pulse} | ThreatScore: {Score}",
                ipAddress, pulseCount, threatScore);

            // MinPulseCount eşiğini geçiyorsa tehdit say
            return pulseCount >= _settings.MinPulseCount;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("OTX isteği zaman aşımına uğradı. IP: {IP}", ipAddress);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTX kontrolünde beklenmeyen hata. IP: {IP}", ipAddress);
            return false;
        }
    }
}

// OTX /reputation endpoint yanıt modeli
public class OTXReputationResponse
{
    [JsonPropertyName("pulse_info")]
    public OTXPulseInfo? PulseInfo { get; set; }

    [JsonPropertyName("threat_score")]
    public int? ThreatScore { get; set; }
}

public class OTXPulseInfo
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
}
