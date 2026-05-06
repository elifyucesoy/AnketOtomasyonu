using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace AnketOtomasyonu.Services.Implementations
{
    /// <summary>
    /// apiservices.selcuk.edu.tr üzerinden Unit ve UnitType verilerini çeker,
    /// 30 günlük MemoryCache'de tutar. Tüm okuma işlemleri cache üzerinden yapılır.
    /// </summary>
    public class UnitApiService : IUnitApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UnitApiService> _logger;

        private const string UNIT_CACHE_KEY      = "unit_api_all_units";
        private const string UNIT_TYPE_CACHE_KEY = "unit_api_all_unit_types";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public UnitApiService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<UnitApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration     = configuration;
            _cache             = cache;
            _logger            = logger;
        }

        // ─── PUBLIC API ───────────────────────────────────────────────────────────

        public async Task<List<UnitDto>> GetAllUnitsAsync(string? bearerToken = null)
        {
            if (_cache.TryGetValue(UNIT_CACHE_KEY, out List<UnitDto>? cached) && cached != null)
                return cached;

            var token = bearerToken ?? await GetServiceTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("[UnitApiService] Token yok, birim listesi döndürülemiyor.");
                return new List<UnitDto>();
            }

            return await FetchAndCacheUnitsAsync(token);
        }

        public async Task<List<UnitTypeDto>> GetAllUnitTypesAsync(string? bearerToken = null)
        {
            if (_cache.TryGetValue(UNIT_TYPE_CACHE_KEY, out List<UnitTypeDto>? cached) && cached != null)
                return cached;

            var token = bearerToken ?? await GetServiceTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("[UnitApiService] Token yok, bölüm listesi döndürülemiyor.");
                return new List<UnitTypeDto>();
            }

            return await FetchAndCacheUnitTypesAsync(token);
        }

        public async Task<List<UnitDto>> GetUnitsByIdsAsync(IEnumerable<int> unitIds, string? bearerToken = null)
        {
            var ids = unitIds.ToHashSet();
            if (!ids.Any()) return new List<UnitDto>();

            var all = await GetAllUnitsAsync(bearerToken);
            return all.Where(u => ids.Contains(u.Id)).ToList();
        }

        public async Task<List<string>> GetAllUnitNamesAsync(string? bearerToken = null)
        {
            var units = await GetAllUnitsAsync(bearerToken);
            return units
                .Where(u => !string.IsNullOrWhiteSpace(u.Name))
                .Select(u => u.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), false))
                .ToList();
        }

        public async Task<(int unitCount, int unitTypeCount)> ForceRefreshAsync(string bearerToken)
        {
            _logger.LogInformation("[UnitApiService] Cache zorla yenileniyor...");
            _cache.Remove(UNIT_CACHE_KEY);
            _cache.Remove(UNIT_TYPE_CACHE_KEY);

            var units     = await FetchAndCacheUnitsAsync(bearerToken);
            var unitTypes = await FetchAndCacheUnitTypesAsync(bearerToken);

            _logger.LogInformation("[UnitApiService] Yenilendi: {U} birim, {T} bölüm.", units.Count, unitTypes.Count);
            return (units.Count, unitTypes.Count);
        }

        public bool IsCached()
            => _cache.TryGetValue(UNIT_CACHE_KEY, out _) &&
               _cache.TryGetValue(UNIT_TYPE_CACHE_KEY, out _);

        // ─── API ÇAĞRILARI ────────────────────────────────────────────────────────

        private async Task<List<UnitDto>> FetchAndCacheUnitsAsync(string bearerToken)
        {
            try
            {
                var baseUrl = GetBaseUrl();
                var client  = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization",
                    bearerToken.StartsWith("Bearer ") ? bearerToken : $"Bearer {bearerToken}");

                // Tüm birimleri tek seferde çek (pageSize büyük tutuldu)
                var body = new
                {
                    orderBy          = new { key = "Id", value = true },
                    pageSize         = 11000,
                    currentPage      = 0,
                    isPagingEnabled  = true,
                    isActive         = true
                };

                var resp = await client.PostAsJsonAsync($"{baseUrl}/api/v1/Unit/UnitList", body);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[UnitApiService] UnitList {S}", resp.StatusCode);
                    return new List<UnitDto>();
                }

                var raw    = await resp.Content.ReadAsStringAsync();
                var result = TryDeserializeItems<UnitDto>(raw, "items");
                if (result == null || result.Count == 0)
                {
                    _logger.LogWarning("[UnitApiService] UnitList boş yanıt döndü.");
                    return new List<UnitDto>();
                }

                _cache.Set(UNIT_CACHE_KEY, result, CacheDuration);
                _logger.LogInformation("[UnitApiService] {N} birim cache'lendi (30 gün).", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UnitApiService] UnitList çekilemedi.");
                return new List<UnitDto>();
            }
        }

        private async Task<List<UnitTypeDto>> FetchAndCacheUnitTypesAsync(string bearerToken)
        {
            try
            {
                var baseUrl = GetBaseUrl();
                var client  = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization",
                    bearerToken.StartsWith("Bearer ") ? bearerToken : $"Bearer {bearerToken}");

                var body = new
                {
                    orderBy         = new { key = "Id", value = true },
                    pageSize        = 500,
                    currentPage     = 0,
                    isPagingEnabled = true
                };

                var resp = await client.PostAsJsonAsync($"{baseUrl}/api/v1/Unit/UnitTypeList", body);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[UnitApiService] UnitTypeList {S}", resp.StatusCode);
                    return new List<UnitTypeDto>();
                }

                var raw    = await resp.Content.ReadAsStringAsync();
                var result = TryDeserializeItems<UnitTypeDto>(raw, "items");
                if (result == null || result.Count == 0)
                {
                    _logger.LogWarning("[UnitApiService] UnitTypeList boş yanıt döndü.");
                    return new List<UnitTypeDto>();
                }

                _cache.Set(UNIT_TYPE_CACHE_KEY, result, CacheDuration);
                _logger.LogInformation("[UnitApiService] {N} bölüm tipi cache'lendi (30 gün).", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UnitApiService] UnitTypeList çekilemedi.");
                return new List<UnitTypeDto>();
            }
        }

        // ─── SERVICE ACCOUNT TOKEN ────────────────────────────────────────────────

        /// <summary>
        /// Background job için servis hesabı ile token alır.
        /// appsettings.json → ServiceAccount:Username + ServiceAccount:Password
        /// </summary>
        private async Task<string?> GetServiceTokenAsync()
        {
            const string SERVICE_TOKEN_KEY = "unit_api_service_token";
            if (_cache.TryGetValue(SERVICE_TOKEN_KEY, out string? cachedToken) && cachedToken != null)
                return cachedToken;

            try
            {
                var username = _configuration["ServiceAccount:Username"];
                var password = _configuration["ServiceAccount:Password"];
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("[UnitApiService] ServiceAccount yapılandırılmamış.");
                    return null;
                }

                var baseUrl = GetBaseUrl();
                var client  = _httpClientFactory.CreateClient();
                var resp    = await client.PostAsJsonAsync($"{baseUrl}/api/v1/Auth/Login",
                    new { username, password });

                if (!resp.IsSuccessStatusCode) return null;

                var raw  = await resp.Content.ReadAsStringAsync();
                var doc  = JsonSerializer.Deserialize<JsonElement>(raw, JsonOpts);
                string?  token = null;

                // Olası yanıt formatları: { "value": "token..." } veya düz string
                if (doc.ValueKind == JsonValueKind.String)
                    token = doc.GetString();
                else if (doc.TryGetProperty("value", out var val))
                    token = val.ValueKind == JsonValueKind.String ? val.GetString() : null;

                if (!string.IsNullOrEmpty(token))
                {
                    _cache.Set(SERVICE_TOKEN_KEY, token, TimeSpan.FromHours(23));
                    _logger.LogInformation("[UnitApiService] Servis hesabı token'ı alındı.");
                }
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UnitApiService] Servis hesabı token alınamadı.");
                return null;
            }
        }

        // ─── YARDIMCILAR ──────────────────────────────────────────────────────────

        private string GetBaseUrl()
            => (_configuration["PermissionService:BaseUrl"] ?? "https://apiservices.selcuk.edu.tr").TrimEnd('/');

        /// <summary>
        /// Farklı API sarmalama formatlarını destekler:
        /// { "value": { "items": [...] } }  veya  { "items": [...] }  veya  [...]
        /// </summary>
        private static List<T>? TryDeserializeItems<T>(string raw, string itemsKey)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(raw, JsonOpts);

                // { "value": { "items": [...] } }
                if (doc.TryGetProperty("value", out var valProp) &&
                    valProp.TryGetProperty(itemsKey, out var itemsInVal))
                    return itemsInVal.Deserialize<List<T>>(JsonOpts);

                // { "items": [...] }
                if (doc.TryGetProperty(itemsKey, out var itemsDirect))
                    return itemsDirect.Deserialize<List<T>>(JsonOpts);

                // Düz dizi
                if (doc.ValueKind == JsonValueKind.Array)
                    return doc.Deserialize<List<T>>(JsonOpts);

                return null;
            }
            catch { return null; }
        }
    }
}
