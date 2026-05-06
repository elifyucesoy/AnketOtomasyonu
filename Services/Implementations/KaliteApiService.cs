using AnketOtomasyonu.Models;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnketOtomasyonu.Services.Implementations
{
    public class KaliteApiService : IKaliteApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IBirimService _birimService;          // appsettings fallback
        private readonly IMemoryCache _cache;
        private readonly ILogger<KaliteApiService> _logger;

        private const string TOKEN_CACHE_KEY      = "kalite_token";
        private const string FAKULTE_CACHE_KEY   = "kalite_fakulteler";
        private const string TUM_BIRIM_CACHE_KEY  = "kalite_tum_birimler";
        private const string BOLUM_CACHE_PREFIX   = "kalite_bolum_";

        public KaliteApiService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IBirimService birimService,
            IMemoryCache cache,
            ILogger<KaliteApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration     = configuration;
            _birimService      = birimService;
            _cache             = cache;
            _logger            = logger;
        }

        // ─── FAKÜLTE LİSTESİ ─────────────────────────────────────────────────────

        public async Task<List<string>> GetFakulteNamesAsync()
        {
            if (_cache.TryGetValue(FAKULTE_CACHE_KEY, out List<string>? cached) && cached != null)
                return cached;

            try
            {
                var token = await GetTokenAsync();
                if (string.IsNullOrEmpty(token))
                    return FallbackFakulteNames();

                var client = _httpClientFactory.CreateClient();
                var baseUrl = _configuration["KaliteApi:BaseUrl"]?.TrimEnd('/');
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var response = await client.GetAsync($"{baseUrl}/api/kalite/fakulte-bazli");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[KaliteApi] fakulte-bazli {S}", response.StatusCode);
                    return FallbackFakulteNames();
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<KaliteFakulteResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var names = result?.Data?.Fakulteler?
                    .Select(f => f.FakulteAdi?.Trim().ToUpperInvariant() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList() ?? FallbackFakulteNames();

                var cacheMin = _configuration.GetValue<int>("KaliteApi:CacheMinutes", 60);
                _cache.Set(FAKULTE_CACHE_KEY, names, TimeSpan.FromMinutes(cacheMin));
                _logger.LogInformation("[KaliteApi] {N} fakülte yüklendi.", names.Count);
                return names;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KaliteApi] Fakülte listesi alınamadı, fallback kullanılıyor.");
                return FallbackFakulteNames();
            }
        }

        // ─── TÜM BİRİMLER (FAKÜLTELER + İDARİ + DİĞER) ─────────────────────────

        public async Task<List<string>> GetAllBirimlerAsync()
        {
            if (_cache.TryGetValue(TUM_BIRIM_CACHE_KEY, out List<string>? cached) && cached != null)
                return cached;

            try
            {
                var token = await GetTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("[KaliteApi] GetAllBirimlerAsync: Token alınamadı.");
                    return new List<string>();
                }

                var client  = _httpClientFactory.CreateClient();
                var baseUrl = _configuration["KaliteApi:BaseUrl"]?.TrimEnd('/');
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                // fakulte-bazli → Fakülte + MYO + YO + Tıp/Veteriner (tüm aktif akademik birimler)
                var response = await client.GetAsync($"{baseUrl}/api/kalite/fakulte-bazli");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[KaliteApi] GetAllBirimlerAsync: {S}", response.StatusCode);
                    return new List<string>();
                }

                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<KaliteFakulteResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var list = result?.Data?.Fakulteler?
                    .Select(f => f.FakulteAdi?.Trim().ToUpperInvariant() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList() ?? new List<string>();

                var cacheMin = _configuration.GetValue<int>("KaliteApi:CacheMinutes", 60);
                _cache.Set(TUM_BIRIM_CACHE_KEY, list, TimeSpan.FromMinutes(cacheMin));
                _logger.LogInformation("[KaliteApi] GetAllBirimlerAsync: {N} birim alındı (Fakülte+MYO+YO).", list.Count);
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KaliteApi] GetAllBirimlerAsync başarısız.");
                return new List<string>();
            }
        }

        // ─── BÖLÜM LİSTESİ ───────────────────────────────────────────────────────

        public async Task<List<string>> GetBolumNamesAsync(string fakulteAdi)
        {
            if (string.IsNullOrWhiteSpace(fakulteAdi)) return new List<string>();

            var cacheKey = BOLUM_CACHE_PREFIX + fakulteAdi.ToUpperInvariant();
            if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached != null)
                return cached;

            try
            {
                var token = await GetTokenAsync();
                if (string.IsNullOrEmpty(token))
                    return new List<string>();

                // Önce fakülteler listesinden bu fakültenin No'sunu bul
                var fakulteNo = await GetFakulteNoAsync(token, fakulteAdi);
                if (fakulteNo == null)
                    return new List<string>();

                var client = _httpClientFactory.CreateClient();
                var baseUrl = _configuration["KaliteApi:BaseUrl"]?.TrimEnd('/');
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var response = await client.GetAsync(
                    $"{baseUrl}/api/kalite/bolum-bazli?fakulteNo={fakulteNo}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[KaliteApi] bolum-bazli {S}", response.StatusCode);
                    return new List<string>();
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<KaliteBolumResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var names = result?.Data?.Bolumler?
                    .Select(b => b.BolumAdi?.Trim().ToUpperInvariant() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList() ?? new List<string>();

                var cacheMin = _configuration.GetValue<int>("KaliteApi:CacheMinutes", 60);
                _cache.Set(cacheKey, names, TimeSpan.FromMinutes(cacheMin));
                _logger.LogInformation("[KaliteApi] {F} → {N} bölüm yüklendi.", fakulteAdi, names.Count);
                return names;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KaliteApi] Bölüm listesi alınamadı: {F}", fakulteAdi);
                return new List<string>();
            }
        }

        // ─── TOKEN YÖNETİMİ ───────────────────────────────────────────────────────

        private async Task<string?> GetTokenAsync()
        {
            if (_cache.TryGetValue(TOKEN_CACHE_KEY, out string? cachedToken) && cachedToken != null)
                return cachedToken;

            try
            {
                var authUrl  = _configuration["KaliteApi:AuthUrl"];
                var username = _configuration["KaliteApi:Username"];
                var password = _configuration["KaliteApi:Password"];

                if (string.IsNullOrEmpty(authUrl) || string.IsNullOrEmpty(username))
                    return null;

                var client = _httpClientFactory.CreateClient();
                var body = JsonSerializer.Serialize(new
                {
                    userName    = username,
                    password    = password,
                    deviceToken = "string",
                    channel     = 0
                });

                var response = await client.PostAsync(authUrl,
                    new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[KaliteApi] Token alınamadı: {S}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var tokenResp = JsonSerializer.Deserialize<KaliteTokenResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var token = tokenResp?.Value?.AccessToken;
                if (!string.IsNullOrEmpty(token))
                {
                    // Token'ı 23 saat cache'le (genellikle 24 saat geçerli)
                    _cache.Set(TOKEN_CACHE_KEY, token, TimeSpan.FromHours(23));
                    _logger.LogInformation("[KaliteApi] Token alındı.");
                }
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KaliteApi] Token isteği başarısız.");
                return null;
            }
        }

        private async Task<int?> GetFakulteNoAsync(string token, string fakulteAdi)
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _configuration["KaliteApi:BaseUrl"]?.TrimEnd('/');
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"{baseUrl}/api/kalite/fakulte-bazli");
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<KaliteFakulteResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result?.Data?.Fakulteler?
                .FirstOrDefault(f => string.Equals(
                    f.FakulteAdi?.Trim(), fakulteAdi?.Trim(),
                    StringComparison.OrdinalIgnoreCase))?.FakulteNo;
        }

        // ─── FALLBACK ─────────────────────────────────────────────────────────────

        private List<string> FallbackFakulteNames()
        {
            _logger.LogWarning("[KaliteApi] appsettings.json fallback kullanılıyor.");
            return _birimService.GetAllNames();
        }

        // ─── JSON DTO'LARI ────────────────────────────────────────────────────────

        private class KaliteTokenResponse
        {
            [JsonPropertyName("value")]
            public TokenValue? Value { get; set; }
        }
        private class TokenValue
        {
            [JsonPropertyName("accessToken")]
            public string? AccessToken { get; set; }
        }

        private class KaliteFakulteResponse
        {
            public FakulteData? Data { get; set; }
        }
        private class FakulteData
        {
            public List<FakulteItem>? Fakulteler { get; set; }
        }
        private class FakulteItem
        {
            public int FakulteNo { get; set; }
            public string? FakulteAdi { get; set; }
        }

        private class KaliteBolumResponse
        {
            public BolumData? Data { get; set; }
        }
        private class BolumData
        {
            public List<BolumItemDto>? Bolumler { get; set; }
        }
        private class BolumItemDto
        {
            public string? BolumAdi { get; set; }
        }
    }
}
