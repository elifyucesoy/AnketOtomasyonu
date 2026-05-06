using System.Collections.Concurrent;
using AnketOtomasyonu.Models.ApiServices;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnketOtomasyonu.Services.Implementations
{
    /// <summary>
    /// UnitList + UnitTypeList önbelleği. Varsayılan TTL 30 gün.
    /// </summary>
    public sealed class UnitCatalogService : IUnitCatalogService
    {
        public const string CacheRegionUnits = "apiservices_units_blob_v1";
        public const string CacheRegionTypes = "apiservices_unittypes_blob_v1";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly IApiServicesAuthService _authService;
        private readonly ILogger<UnitCatalogService> _logger;

        private readonly ConcurrentDictionary<int, ApiUnitItem> _units = new();
        private readonly object _typesGate = new();
        private List<ApiUnitTypeItem> _unitTypes = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        public UnitCatalogService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IMemoryCache memoryCache,
            IApiServicesAuthService authService,
            ILogger<UnitCatalogService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _authService = authService;
            _logger = logger;
        }

        private TimeSpan CatalogTtl =>
            TimeSpan.FromDays(_configuration.GetValue("ApiServices:CatalogCacheDays", 30));

        public async Task EnsureCatalogAsync(string bearerToken, CancellationToken cancellationToken = default)
        {
            if (_units.Count > 0 && GetUnitTypes().Count > 0)
                return;

            if (_memoryCache.TryGetValue(CacheRegionUnits, out byte[]? uBlob) &&
                _memoryCache.TryGetValue(CacheRegionTypes, out byte[]? tBlob) &&
                uBlob != null && tBlob != null)
            {
                HydrateFromBlobs(uBlob, tBlob);
                if (_units.Count > 0)
                    return;
            }

            await FetchAndStoreAsync(bearerToken, cancellationToken);
        }

        public async Task RefreshCatalogAsync(CancellationToken cancellationToken = default)
        {
            var user = _configuration["ApiServices:SystemUsername"];
            var pass = _configuration["ApiServices:SystemPassword"];
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                _logger.LogWarning("[ApiServices] Catalog yenileme için ApiServices:SystemUsername / SystemPassword tanımlı değil.");
                return;
            }

            var token = await _authService.LoginAsync(user, pass, cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("[ApiServices] Sistem kullanıcısı ile giriş başarısız; katalog yenilenemedi.");
                return;
            }

            await FetchAndStoreAsync(token, cancellationToken);
        }

        private async Task FetchAndStoreAsync(string bearerToken, CancellationToken cancellationToken)
        {
            var baseUrl = _configuration["ApiServices:BaseUrl"]?.TrimEnd('/');
            var unitPath = _configuration["ApiServices:UnitListPath"] ?? "/api/v1/Unit/UnitList";
            var typePath = _configuration["ApiServices:UnitTypeListPath"] ?? "/api/v1/Unit/UnitTypeList";
            if (string.IsNullOrEmpty(baseUrl)) return;

            var client = _httpClientFactory.CreateClient("ApiServices");
            using var uReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{unitPath.TrimStart('/')}");
            using var tReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{typePath.TrimStart('/')}");
            uReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            tReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            var uTask = client.SendAsync(uReq, cancellationToken);
            var tTask = client.SendAsync(tReq, cancellationToken);
            await Task.WhenAll(uTask, tTask);

            using var uResp = await uTask;
            using var tResp = await tTask;

            if (!uResp.IsSuccessStatusCode || !tResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[ApiServices] Unit list HTTP {U} / UnitType HTTP {T}",
                    uResp.StatusCode, tResp.StatusCode);
                return;
            }

            var uJson = await uResp.Content.ReadAsStringAsync(cancellationToken);
            var tJson = await tResp.Content.ReadAsStringAsync(cancellationToken);

            using var uDoc = JsonDocument.Parse(uJson);
            using var tDoc = JsonDocument.Parse(tJson);

            var units = ParseItems<ApiUnitItem>(uDoc);
            var types = ParseItems<ApiUnitTypeItem>(tDoc);

            ApplyInMemory(units, types);

            var ttl = CatalogTtl;
            _memoryCache.Set(CacheRegionUnits, System.Text.Encoding.UTF8.GetBytes(uJson), ttl);
            _memoryCache.Set(CacheRegionTypes, System.Text.Encoding.UTF8.GetBytes(tJson), ttl);

            _logger.LogInformation("[ApiServices] Katalog yüklendi: {Nu} birim, {Nt} alt kayıt.", units.Count, types.Count);
        }

        private void HydrateFromBlobs(byte[] uBlob, byte[] tBlob)
        {
            var uJson = System.Text.Encoding.UTF8.GetString(uBlob);
            var tJson = System.Text.Encoding.UTF8.GetString(tBlob);
            using var uDoc = JsonDocument.Parse(uJson);
            using var tDoc = JsonDocument.Parse(tJson);
            ApplyInMemory(ParseItems<ApiUnitItem>(uDoc), ParseItems<ApiUnitTypeItem>(tDoc));
        }

        private void ApplyInMemory(List<ApiUnitItem> units, List<ApiUnitTypeItem> types)
        {
            _units.Clear();
            lock (_typesGate)
            {
                _unitTypes = types;
            }

            foreach (var u in units.Where(x => x.Id != 0))
                _units[u.Id] = u;

        }

        private List<T> ParseItems<T>(JsonDocument doc)
        {
            var root = doc.RootElement;
            if (TryDeserializeArray<T>(root, out var list) && list.Count > 0)
                return list;

            foreach (var arr in FindArrays(root))
            {
                if (TryDeserializeArray<T>(arr, out list) && list.Count > 0)
                    return list;
            }

            return new List<T>();
        }

        private IEnumerable<JsonElement> FindArrays(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Array:
                    yield return el;
                    yield break;
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    {
                        foreach (var inner in FindArrays(p.Value))
                            yield return inner;
                    }
                    break;
            }
        }

        private static bool TryDeserializeArray<T>(JsonElement el, out List<T> result)
        {
            result = new List<T>();
            if (el.ValueKind != JsonValueKind.Array) return false;
            try
            {
                result = JsonSerializer.Deserialize<List<T>>(el.GetRawText(), JsonOpts) ?? new List<T>();
                return result.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public IReadOnlyDictionary<int, ApiUnitItem> GetUnitsById() => _units;

        public IReadOnlyList<ApiUnitTypeItem> GetUnitTypes()
        {
            lock (_typesGate)
                return _unitTypes.ToList();
        }

        public async Task<IReadOnlyList<string>> GetAllUnitNamesAsync(CancellationToken cancellationToken = default)
        {
            if (_units.Count == 0)
                await TryWarmFromSystemAsync(cancellationToken);

            return _units.Values
                .Select(u => u.DisplayName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), false))
                .ToList();
        }

        private async Task TryWarmFromSystemAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshCatalogAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[ApiServices] Katalog ilk yükleme atlandı.");
            }
        }

        public async Task<IReadOnlyList<string>> GetBolumNamesForUnitNameAsync(string unitName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(unitName)) return Array.Empty<string>();

            if (_units.Count == 0)
                await TryWarmFromSystemAsync(cancellationToken);

            var unit = _units.Values.FirstOrDefault(u =>
                string.Equals(u.DisplayName, unitName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (unit == null) return Array.Empty<string>();

            return GetUnitTypes()
                .Where(t => t.UnitId == unit.Id && IsDepartmentLike(t.TypeDiscriminator))
                .Select(t => t.DisplayName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), false))
                .ToList();
        }

        private static bool IsDepartmentLike(string typeDiscriminator)
        {
            if (string.IsNullOrEmpty(typeDiscriminator)) return true;
            return typeDiscriminator.Contains("bölüm", StringComparison.OrdinalIgnoreCase)
                || typeDiscriminator.Contains("program", StringComparison.OrdinalIgnoreCase)
                || typeDiscriminator.Contains("anabilim", StringComparison.OrdinalIgnoreCase);
        }

        public ResolvedLoginProfile ResolveLoginProfile(IReadOnlyList<int> unitIds, bool isStudent)
        {
            var units = GetUnitsById();
            var types = GetUnitTypes();

            var orderedNames = new List<string>();
            foreach (var id in unitIds)
            {
                if (units.TryGetValue(id, out var u))
                {
                    var n = u.DisplayName;
                    if (!string.IsNullOrWhiteSpace(n))
                        orderedNames.Add(n.Trim());
                }
            }

            var primary = orderedNames.FirstOrDefault() ?? "";

            if (!isStudent)
            {
                return new ResolvedLoginProfile
                {
                    UnitIds = unitIds,
                    PrimaryUnitName = primary,
                    DepartmentName = null,
                    UnitNames = orderedNames
                };
            }

            var anchorUnitId = unitIds.FirstOrDefault(id => units.ContainsKey(id));
            string? dept = null;
            if (anchorUnitId != 0)
            {
                var candidates = types.Where(t => t.UnitId == anchorUnitId).ToList();
                dept = candidates
                    .Where(t => IsDepartmentLike(t.TypeDiscriminator))
                    .Select(t => t.DisplayName)
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

                if (string.IsNullOrEmpty(dept))
                    dept = candidates.Select(t => t.DisplayName).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            }

            return new ResolvedLoginProfile
            {
                UnitIds = unitIds,
                PrimaryUnitName = primary,
                DepartmentName = dept,
                UnitNames = orderedNames
            };
        }
    }
}
