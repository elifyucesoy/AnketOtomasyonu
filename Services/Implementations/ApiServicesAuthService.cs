using AnketOtomasyonu.Authorization.Models;
using AnketOtomasyonu.Services.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnketOtomasyonu.Services.Implementations
{
    public sealed class ApiServicesAuthService : IApiServicesAuthService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApiServicesAuthService> _logger;

        public ApiServicesAuthService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<ApiServicesAuthService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string?> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
        {
            var baseUrl = _configuration["ApiServices:BaseUrl"]?.TrimEnd('/');
            var loginPath = _configuration["ApiServices:LoginPath"] ?? "/api/v1/Auth/Login";
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("[ApiServices] ApiServices:BaseUrl tanımlı değil.");
                return null;
            }

            var client = _httpClientFactory.CreateClient("ApiServices");
            var payload = new
            {
                userName,
                password,
                deviceToken = _configuration["ApiServices:DeviceToken"] ?? "AnketOtomasyonu",
                channel = _configuration.GetValue("ApiServices:Channel", 0)
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var uri = $"{baseUrl.TrimEnd('/')}/{loginPath.TrimStart('/')}";
            using var response = await client.PostAsync(uri, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("[ApiServices] Login HTTP {Code}: {Body}", response.StatusCode, err);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var token = ApiServicesJson.TryExtractAccessToken(doc.RootElement);
            if (string.IsNullOrEmpty(token))
                _logger.LogWarning("[ApiServices] Login yanıtında access token bulunamadı.");
            return token;
        }

        public async Task<JsonDocument?> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default)
        {
            var baseUrl = _configuration["ApiServices:BaseUrl"]?.TrimEnd('/');
            var path = _configuration["ApiServices:GetProfilePath"] ?? "/api/v1/User/GetProfile";
            if (string.IsNullOrEmpty(baseUrl)) return null;

            var client = _httpClientFactory.CreateClient("ApiServices");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            using var resp = await client.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("[ApiServices] GetProfile HTTP {Code}: {Body}", resp.StatusCode, err);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(json);
        }

        /// <inheritdoc />
        public async Task<bool?> HasPermissionAsync(
            string bearerToken,
            HasPermissionRequest request,
            CancellationToken cancellationToken = default)
        {
            var baseUrl = _configuration["ApiServices:BaseUrl"]?.TrimEnd('/');
            var path = _configuration["ApiServices:HasPermissionPath"] ?? "/api/v1/Permission/HasPermission";
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("[ApiServices] HasPermission: BaseUrl tanımlı değil.");
                return null;
            }

            var usePascal = _configuration.GetValue("ApiServices:HasPermissionRequestUsePascalCase", false);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = usePascal ? null : JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(request, jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("ApiServices");
            var uri = $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            var locale = _configuration["ApiServices:Locale"];
            if (!string.IsNullOrWhiteSpace(locale))
                httpRequest.Headers.TryAddWithoutValidation("Locale", locale.Trim());

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[ApiServices] HasPermission HTTP {Code}: {Body}", response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var result = ApiServicesJson.TryExtractHasPermissionResult(doc.RootElement);
            
            _logger.LogInformation("[ApiServices] HasPermission Request: {Json} | Response: {Body} -> Extracted: {Result}", json, body, result);
            
            return result;
        }
    }

    internal static class ApiServicesJson
    {
        public static string? TryExtractAccessToken(JsonElement root)
        {
            foreach (var name in new[] { "accessToken", "access_token", "token", "jwt" })
            {
                if (TryGetPropertyIgnoreCase(root, name, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString();
            }

            if (TryGetPropertyIgnoreCase(root, "value", out var value) && value.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "accessToken", "access_token", "token" })
                {
                    if (TryGetPropertyIgnoreCase(value, name, out var p) && p.ValueKind == JsonValueKind.String)
                        return p.GetString();
                }
            }

            if (TryGetPropertyIgnoreCase(root, "data", out var data))
                return TryExtractAccessToken(data);

            return null;
        }

        public static IReadOnlyList<int> TryExtractUnitIds(JsonElement root)
        {
            var list = new List<int>();
            CollectUnitIds(root, list);
            return list.Distinct().ToList();
        }

        /// <summary>GetProfile yanıtındaki tüm <c>ANKET_*</c> izin kodlarını toplar (iç içe JSON).</summary>
        public static HashSet<string> TryExtractAnketPermissionCodes(JsonElement root)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectAnketPermissionCodeStrings(root, set);
            return set;
        }

        private static void CollectAnketPermissionCodeStrings(JsonElement el, HashSet<string> set)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                        CollectAnketPermissionCodeStrings(p.Value, set);
                    break;
                case JsonValueKind.Array:
                    foreach (var x in el.EnumerateArray())
                        CollectAnketPermissionCodeStrings(x, set);
                    break;
                case JsonValueKind.String:
                    {
                        var s = el.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(s) &&
                            s.StartsWith("ANKET_", StringComparison.OrdinalIgnoreCase))
                            set.Add(s);
                        break;
                    }
            }
        }

        private static void CollectUnitIds(JsonElement el, List<int> buffer)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    {
                        if (p.Name.Equals("unitIds", StringComparison.OrdinalIgnoreCase) ||
                            p.Name.Equals("UnitIds", StringComparison.OrdinalIgnoreCase))
                        {
                            AddInts(p.Value, buffer);
                            continue;
                        }
                        CollectUnitIds(p.Value, buffer);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                        CollectUnitIds(item, buffer);
                    break;
            }
        }

        private static void AddInts(JsonElement el, List<int> buffer)
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in el.EnumerateArray())
                {
                    if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n))
                        buffer.Add(n);
                }
            }
        }

        /// <summary>HasPermission yanıtından izin var/yok okur (<c>data</c>, <c>value</c>, <c>true</c>/<c>false</c>, <c>0</c>/<c>1</c>).</summary>
        public static bool? TryExtractHasPermissionResult(JsonElement root)
        {
            return WalkPermissionBool(root);
        }

        private static bool? WalkPermissionBool(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.String:
                    {
                        var s = el.GetString()?.Trim();
                        if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s, "1", StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s, "0", StringComparison.OrdinalIgnoreCase))
                            return false;
                        return null;
                    }
                case JsonValueKind.Number:
                    if (el.TryGetInt32(out var n))
                    {
                        if (n == 1) return true;
                        if (n == 0) return false;
                    }
                    return null;
                case JsonValueKind.Object:
                    // Önce doğrudan izin alanları; "success" genelde HTTP/API başarısıdır — yanlış pozitif olmasın diye en sonda.
                    foreach (var key in new[] { "hasPermission", "data", "value", "result", "permission", "authorized", "isGranted" })
                    {
                        if (TryGetPropertyIgnoreCase(el, key, out var p))
                        {
                            var inner = WalkPermissionBool(p);
                            if (inner.HasValue)
                                return inner.Value;
                        }
                    }
                    foreach (var p in el.EnumerateObject())
                    {
                        if (p.Name.Equals("success", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var inner = WalkPermissionBool(p.Value);
                        if (inner.HasValue)
                            return inner.Value;
                    }
                    if (TryGetPropertyIgnoreCase(el, "success", out var succ))
                    {
                        var innerOk = WalkPermissionBool(succ);
                        if (innerOk.HasValue)
                            return innerOk.Value;
                    }
                    return null;
                default:
                    return null;
            }
        }

        public static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
        {
            value = default;
            if (obj.ValueKind != JsonValueKind.Object) return false;
            foreach (var p in obj.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Profilde sık kullanılan TC / ad alanlarını bulur.</summary>
        public static void TryExtractPersonFields(JsonElement root, out string first, out string last, out string? tc)
        {
            first = "";
            last = "";
            tc = null;
            WalkPersonFields(root, ref first, ref last, ref tc);
        }

        private static void WalkPersonFields(JsonElement el, ref string first, ref string last, ref string? tc)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    {
                        MatchPersonField(p.Name, p.Value, ref first, ref last, ref tc);
                        WalkPersonFields(p.Value, ref first, ref last, ref tc);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var x in el.EnumerateArray())
                        WalkPersonFields(x, ref first, ref last, ref tc);
                    break;
            }
        }

        private static void MatchPersonField(string name, JsonElement val, ref string first, ref string last, ref string? tc)
        {
            if (val.ValueKind != JsonValueKind.String) return;
            var s = val.GetString();
            if (string.IsNullOrEmpty(s)) return;

            if (name.Equals("firstName", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ad", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("givenName", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(first)) first = s;
            }
            else if (name.Equals("lastName", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("surname", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("soyad", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("familyName", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(last)) last = s;
            }
            else if (name.Contains("tc", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("kimlik", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("nationalId", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("identityNumber", StringComparison.OrdinalIgnoreCase))
            {
                if (s.Length >= 10 && s.All(char.IsDigit))
                    tc ??= s;
            }
        }
    }
}
