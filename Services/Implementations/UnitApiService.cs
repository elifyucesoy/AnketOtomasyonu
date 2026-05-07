using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AnketOtomasyonu.Services.Implementations
{
    /// <summary>
    /// Birim listesi önce bellek önbelleği, sonra <see cref="CachedUnits"/> tablosu (haftalık job ile dolar),
    /// ikisi de boşsa apiservices + System User. Böylece rutin isteklerde sürekli sistem kullanıcısı ile API çağrılmaz.
    /// </summary>
    public class UnitApiService : IUnitApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UnitApiService> _logger;
        private readonly ApplicationDbContext _db;

        private const string UNIT_CACHE_KEY      = "unit_api_all_units";
        private const string UNIT_TYPE_CACHE_KEY = "unit_api_all_unit_types";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(7);  // Haftalık cache

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public UnitApiService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<UnitApiService> logger,
            ApplicationDbContext db)
        {
            _httpClientFactory = httpClientFactory;
            _configuration     = configuration;
            _cache             = cache;
            _logger            = logger;
            _db                = db;
        }

        // ─── PUBLIC API ───────────────────────────────────────────────────────────

        public async Task<List<UnitDto>> GetAllUnitsAsync(string? bearerToken = null)
        {
            if (_cache.TryGetValue(UNIT_CACHE_KEY, out List<UnitDto>? cached) && cached != null)
                return cached;

            var fromDb = await LoadUnitsFromCachedTableAsync();
            if (fromDb.Count > 0)
            {
                _cache.Set(UNIT_CACHE_KEY, fromDb, CacheDuration);
                _logger.LogInformation("[UnitApiService] {N} birim CachedUnits tablosundan okundu (System User API çağrısı yok).", fromDb.Count);
                return fromDb;
            }

            var token = bearerToken ?? await GetServiceTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("[UnitApiService] CachedUnits boş ve token yok, birim listesi döndürülemiyor.");
                return new List<UnitDto>();
            }

            return await FetchAndCacheUnitsAsync(token);
        }

        private async Task<List<UnitDto>> LoadUnitsFromCachedTableAsync()
        {
            try
            {
                return await _db.CachedUnits.AsNoTracking()
                    .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Name))
                    .OrderBy(u => u.Name)
                    .Select(u => new UnitDto
                    {
                        Id = u.Id,
                        Name = u.Name,
                        ParentId = u.ParentId,
                        UnitTypeId = u.UnitTypeId,
                        UnitTypeName = u.UnitTypeName,
                        IsActive = u.IsActive
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[UnitApiService] CachedUnits okunamadı.");
                return new List<UnitDto>();
            }
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

        public async Task<UnitDto?> GetUnitByIdAsync(int unitId, string? bearerToken = null)
        {
            // 1. Cache'den ara
            var all = await GetAllUnitsAsync(bearerToken);
            var fromCache = all.FirstOrDefault(u => u.Id == unitId);
            if (fromCache != null) return fromCache;

            // 2. Cache'de yoksa UnitById endpoint'i doğrudan çağır
            try
            {
                var token = bearerToken ?? await GetServiceTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("[UnitApiService] GetUnitByIdAsync: token yok, Id={Id}", unitId);
                    return null;
                }

                var baseUrl = GetBaseUrl();
                var path    = (_configuration["ApiServices:UnitByIdPath"] ?? "/api/v1/Unit/UnitById").TrimStart('/');
                var client  = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization",
                    token.StartsWith("Bearer ") ? token : $"Bearer {token}");

                var resp = await client.GetAsync($"{baseUrl}/{path}?id={unitId}");
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[UnitApiService] UnitById HTTP {S} Id={Id}", resp.StatusCode, unitId);
                    return null;
                }

                var raw = await resp.Content.ReadAsStringAsync();
                // Yanıt: { "value": {...} } veya düz nesne
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;

                System.Text.Json.JsonElement unitEl;
                if (root.TryGetProperty("value", out var valProp) && valProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                    unitEl = valProp;
                else if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                    unitEl = dataProp;
                else
                    unitEl = root;

                var result = System.Text.Json.JsonSerializer.Deserialize<UnitDto>(unitEl.GetRawText(), JsonOpts);
                _logger.LogInformation("[UnitApiService] UnitById başarılı. Id={Id} Name={Name}", unitId, result?.Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UnitApiService] GetUnitByIdAsync hata. Id={Id}", unitId);
                return null;
            }
        }

        public async Task<UnitDto?> GetParentUnitAsync(int unitId, string? bearerToken = null)
        {
            // Adım 1: unitId'nin birimini bul
            var unit = await GetUnitByIdAsync(unitId, bearerToken);
            if (unit == null)
            {
                _logger.LogWarning("[UnitApiService] GetParentUnitAsync: unitId={Id} bulunamadı.", unitId);
                return null;
            }

            // Adım 2: parentId var mı?
            if (unit.ParentId == null || unit.ParentId == 0)
            {
                _logger.LogInformation("[UnitApiService] UnitId={Id} ({Name}) için parentId yok; zaten kök birim.", unitId, unit.Name);
                return null;
            }

            // Adım 3: parentId ile üst birimi getir
            _logger.LogInformation("[UnitApiService] UnitId={Id} → parentId={Pid}", unitId, unit.ParentId);
            return await GetUnitByIdAsync(unit.ParentId.Value, bearerToken);
        }

        public async Task<(int unitCount, int unitTypeCount)> ForceRefreshAsync(string bearerToken)
        {
            _logger.LogInformation("[UnitApiService] Cache zorla yenileniyor (System User ile)...");
            _cache.Remove(UNIT_CACHE_KEY);
            _cache.Remove(UNIT_TYPE_CACHE_KEY);

            // Token boşsa System User ile al
            var token = string.IsNullOrWhiteSpace(bearerToken)
                ? (await GetServiceTokenAsync() ?? string.Empty)
                : bearerToken;

            var units     = await FetchAndCacheUnitsAsync(token);
            var unitTypes = await FetchAndCacheUnitTypesAsync(token);

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
                var unitPath = (_configuration["ApiServices:UnitListPath"] ?? "/api/v1/Unit/UnitList").TrimStart('/');
                var authHeader = bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? bearerToken
                    : $"Bearer {bearerToken}";

                List<UnitDto>? result = null;

                // 1) GET — apiservices tarafında UnitList genelde GET ile tam liste döner (UnitCatalogService ile uyumlu)
                try
                {
                    var clientGet = _httpClientFactory.CreateClient();
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{unitPath}");
                    req.Headers.Authorization = AuthenticationHeaderValue.Parse(authHeader);
                    using var respGet = await clientGet.SendAsync(req);
                    if (respGet.IsSuccessStatusCode)
                    {
                        var rawGet = await respGet.Content.ReadAsStringAsync();
                        result = ParseUnitListPayload(rawGet);
                        if (result != null && result.Count > 0)
                            _logger.LogInformation("[UnitApiService] UnitList GET ile {N} birim alındı.", result.Count);
                    }
                    else
                        _logger.LogWarning("[UnitApiService] UnitList GET HTTP {S}", respGet.StatusCode);
                }
                catch (Exception exGet)
                {
                    _logger.LogWarning(exGet, "[UnitApiService] UnitList GET istisna.");
                }

                // 2) POST (sayfalı gövde) — GET boş veya başarısızsa
                if (result == null || result.Count == 0)
                {
                    var client  = _httpClientFactory.CreateClient();
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);

                    var body = new
                    {
                        orderBy          = new { key = "Id", value = true },
                        pageSize         = 11000,
                        currentPage      = 0,
                        isPagingEnabled  = true,
                        isActive         = true
                    };

                    var resp = await client.PostAsJsonAsync($"{baseUrl}/{unitPath}", body);
                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("[UnitApiService] UnitList POST {S}", resp.StatusCode);
                        return new List<UnitDto>();
                    }

                    var rawPost = await resp.Content.ReadAsStringAsync();
                    result = ParseUnitListPayload(rawPost);
                }

                if (result == null || result.Count == 0)
                {
                    _logger.LogWarning("[UnitApiService] UnitList boş yanıt (GET ve POST sonrası).");
                    return new List<UnitDto>();
                }

                _cache.Set(UNIT_CACHE_KEY, result, CacheDuration);
                _logger.LogInformation("[UnitApiService] {N} birim cache'lendi (7 gün).", result.Count);
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

                var typePath = (_configuration["ApiServices:UnitTypeListPath"] ?? "/api/v1/Unit/UnitTypeList").TrimStart('/');
                var resp = await client.PostAsJsonAsync($"{baseUrl}/{typePath}", body);
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
                _logger.LogInformation("[UnitApiService] {N} bölüm tipi cache'lendi (7 gün).", result.Count);
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
        /// System User (anket.api.system.user) ile /api/v1/Auth/Login endpoint'ine istek atar,
        /// elde edilen access token'ı 23 saat cache'de tutar.
        /// appsettings.json → ServiceAccount:Username + ServiceAccount:Password (birincil)
        ///                   → ApiServices:SystemUsername + ApiServices:SystemPassword (fallback)
        /// </summary>
        private async Task<string?> GetServiceTokenAsync()
        {
            const string SERVICE_TOKEN_KEY = "unit_api_service_token";
            if (_cache.TryGetValue(SERVICE_TOKEN_KEY, out string? cachedToken) && cachedToken != null)
                return cachedToken;

            try
            {
                // Birincil: ServiceAccount bölümü
                var userName = _configuration["ServiceAccount:Username"]
                    ?? _configuration["ApiServices:SystemUsername"];
                var password = _configuration["ServiceAccount:Password"]
                    ?? _configuration["ApiServices:SystemPassword"];

                if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("[UnitApiService] System User yapılandırılmamış (ServiceAccount / ApiServices:SystemUsername).");
                    return null;
                }

                var deviceToken = _configuration["ApiServices:DeviceToken"] ?? "AnketOtomasyonu";
                var channel     = _configuration.GetValue<int>("ApiServices:Channel", 0);

                var baseUrl    = GetBaseUrl();
                var loginPath  = (_configuration["ApiServices:LoginPath"] ?? "/api/v1/Auth/Login").TrimStart('/');
                var client     = _httpClientFactory.CreateClient();
                var resp       = await client.PostAsJsonAsync($"{baseUrl}/{loginPath}",
                    new { userName, password, deviceToken, channel });

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[UnitApiService] System User login HTTP {S}.", resp.StatusCode);
                    return null;
                }

                var raw = await resp.Content.ReadAsStringAsync();
                using var loginDoc = JsonDocument.Parse(raw);
                var root = loginDoc.RootElement;

                // AuthController ile aynı: isSucceeded false ise token yok
                if (root.TryGetProperty("isSucceeded", out var okEl) &&
                    okEl.ValueKind == JsonValueKind.False)
                {
                    _logger.LogWarning("[UnitApiService] System User login isSucceeded=false. Body: {Body}",
                        raw.Length > 400 ? raw[..400] + "…" : raw);
                    return null;
                }

                var token = ApiServicesJson.TryExtractAccessToken(root);

                if (!string.IsNullOrEmpty(token))
                {
                    _cache.Set(SERVICE_TOKEN_KEY, token, TimeSpan.FromHours(23));
                    _logger.LogInformation("[UnitApiService] System User token alındı (23 saat cache). Kullanıcı: {U}", userName);
                }
                else
                {
                    _logger.LogWarning("[UnitApiService] System User login başarılı ama token ayrıştırılamadı. Body: {Body}", raw[..Math.Min(raw.Length, 200)]);
                }
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UnitApiService] System User token alınamadı.");
                return null;
            }
        }

        // ─── YARDIMCILAR ──────────────────────────────────────────────────────────

        private string GetBaseUrl()
            => (_configuration["ApiServices:BaseUrl"]
                ?? _configuration["PermissionService:BaseUrl"]
                ?? "https://apiservices.selcuk.edu.tr").TrimEnd('/');

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

        /// <summary>
        /// UnitList yanıtında <c>items</c> anahtarı dışında gömülü dizileri de tarar (ApiServices JSON çeşitleri).
        /// </summary>
        private List<UnitDto>? ParseUnitListPayload(string raw)
        {
            var fromKeys = TryDeserializeItems<UnitDto>(raw, "items");
            if (fromKeys != null && fromKeys.Count > 0)
                return fromKeys;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                foreach (var arr in FindJsonArrays(doc.RootElement))
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<UnitDto>>(arr.GetRawText(), JsonOpts);
                        if (list != null && list.Count > 0)
                            return list;
                    }
                    catch { /* bir sonraki dizi */ }
                }
            }
            catch { /* boş */ }

            return null;
        }

        private static IEnumerable<JsonElement> FindJsonArrays(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Array:
                    yield return el;
                    yield break;
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    foreach (var inner in FindJsonArrays(p.Value))
                        yield return inner;
                    break;
            }
        }
    }
}
