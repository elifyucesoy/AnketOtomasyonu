using AnketOtomasyonu.Authorization.Models;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http.Json;
using System.Text.Json;

namespace AnketOtomasyonu.Authorization
{
    /// <summary>
    /// Kullanıcı tipi: 0 = Personel, 1 = Öğrenci
    /// </summary>
    public enum UserType
    {
        Employee = 0,
        Student = 1,
    }

    // ─── INTERFACE ──────────────────────────────────────────────────────────────

    public interface IAuthServiceHandler
    {
        /// <summary>Token'ın geçerli olup olmadığını uzak serviste kontrol eder.</summary>
        Task<bool> ValidateAuthServiceAsync(string accessToken);

        /// <summary>Kullanıcının belirli bir grup içinde belirtilen izinlere sahip olup olmadığını kontrol eder.</summary>
        Task<bool> ValidatePermissionServiceAsync(string accessToken, string groupCode, List<string> permissionCodes, Operations? operation = Operations.Or);

        /// <summary>Login yapmış kullanıcının detaylı profil bilgilerini alır.</summary>
        Task<CurrentUser?> GetCurrentUser();
    }

    // ─── IMPLEMENTATION ──────────────────────────────────────────────────────────

    public class AuthServiceHandler : IAuthServiceHandler
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuthServiceHandler> _logger;
        private readonly string _permissionServiceUrl;

        public AuthServiceHandler(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthServiceHandler> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _permissionServiceUrl = configuration["PermissionService:BaseUrl"]
                ?? throw new InvalidOperationException("PermissionService:BaseUrl yapılandırılmamış");
        }

        /// <summary>
        /// İstek üzerindeki token'ı şu öncelik sırasıyla bulur:
        ///   1. Authorization header
        ///   2. AccessToken cookie
        ///   3. Claim olarak saklanan token (login sırasında eklendi)
        /// </summary>
        private string? GetTokenFromRequest()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return null;

            // 1. Authorization header
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader))
                return authHeader;

            // 2. Cookie
            var cookieToken = context.Request.Cookies["AccessToken"];
            if (!string.IsNullOrEmpty(cookieToken))
                return $"Bearer {cookieToken}";

            // 3. Claim (login sırasında ClaimsIdentity'e eklenen token)
            var tokenClaim = context.User?.FindFirst("AccessToken")?.Value;
            if (!string.IsNullOrEmpty(tokenClaim))
                return $"Bearer {tokenClaim}";

            return null;
        }

        /// <inheritdoc/>
        public async Task<bool> ValidateAuthServiceAsync(string accessToken)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_permissionServiceUrl}/api/v1/AuthCheck/IsAuthenticate");
                request.Headers.Add("Authorization", accessToken);

                var response = await httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token doğrulama hatası");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> ValidatePermissionServiceAsync(
            string accessToken, string groupCode, List<string> permissionCodes,
            Operations? operation = Operations.Or)
        {
            try
            {
                if (permissionCodes == null || permissionCodes.Count == 0)
                {
                    _logger.LogWarning("İzin kodları boş — false döndürülüyor");
                    return false;
                }

                var httpClient = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{_permissionServiceUrl}/api/v1/Permission/HasPermission");
                request.Headers.Add("Authorization", accessToken);
                request.Content = JsonContent.Create(new HasPermissionRequest
                {
                    GroupCode = groupCode,
                    Codes = permissionCodes,
                    Operation = operation
                });

                var response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("İzin kontrolü başarısız. HTTP:{S} GrupKodu:{G}",
                        response.StatusCode, groupCode);
                    return false;
                }

                var raw = await response.Content.ReadAsStringAsync();
                return PermissionApiResponseParser.TryParseBool(raw, out var ok) && ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GrupKodu:{G} için izin doğrulama hatası", groupCode);
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<CurrentUser?> GetCurrentUser()
        {
            try
            {
                var token = GetTokenFromRequest();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("GetCurrentUser: Token bulunamadı");
                    return null;
                }

                var httpClient = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_permissionServiceUrl}/api/v1/Auth/GetProfile");
                request.Headers.Add("Authorization", token);

                var response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetProfile başarısız. HTTP:{S}", response.StatusCode);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<CurrentUser>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetCurrentUser hatası");
                return null;
            }
        }
    }

    // ─── AUTHORIZATION REQUIREMENT ───────────────────────────────────────────────

    /// <summary>
    /// Policy tabanlı yetkilendirme için gereksinim modeli.
    /// Örnek: [Authorize(Policy = "ANKET_API_ADMIN")]
    /// </summary>
    public class AuthServiceRequirement : IAuthorizationRequirement
    {
        public string GroupCode { get; }
        public List<string> PermissionCode { get; }
        public Operations? Operation { get; }

        public AuthServiceRequirement(
            string groupCode,
            List<string> permissionCode,
            Operations? operation = Operations.Or)
        {
            GroupCode = groupCode;
            PermissionCode = permissionCode;
            Operation = operation;
        }
    }

    // ─── AUTHORIZATION HANDLER ───────────────────────────────────────────────────

    /// <summary>
    /// Controller'larda [Authorize(Policy = "PolicyName")] kullanıldığında devreye girer.
    /// Token alır → IsAuthenticate kontrolü → HasPermission kontrolü.
    /// </summary>
    public class AuthServicePermissionHandler : AuthorizationHandler<AuthServiceRequirement>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuthServicePermissionHandler> _logger;
        private readonly string _permissionServiceUrl;

        public AuthServicePermissionHandler(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthServicePermissionHandler> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _permissionServiceUrl = configuration["PermissionService:BaseUrl"]
                ?? throw new InvalidOperationException("PermissionService:BaseUrl yapılandırılmamış");
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            AuthServiceRequirement requirement)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) { context.Fail(); return; }

            // ── KISAYOL: Cookie claim'leri zaten login'de doğrulanmıştır.
            //    Requirement'taki kodlardan herhangi biri claim'de varsa uzak servise gitme.
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                bool claimSatisfied = requirement.Operation == Operations.And
                    ? requirement.PermissionCode.All(code => context.User.HasClaim(AnketPermissions.ClaimType, code))
                    : requirement.PermissionCode.Any(code => context.User.HasClaim(AnketPermissions.ClaimType, code));

                // AdminController policy (ANKET_API_ADMIN): OR [Admin, SuperAdmin]. Eski oturumlarda yalnızca
                // ClaimTypes.Role olabilir; izin claim'i eksikse yine de yönetim paneline izin ver.
                if (!claimSatisfied
                    && requirement.Operation == Operations.Or
                    && requirement.PermissionCode.Contains(AnketPermissions.Admin)
                    && requirement.PermissionCode.Contains(AnketPermissions.SuperAdmin))
                {
                    if (context.User.IsInRole("SuperAdmin") || context.User.IsInRole("Admin"))
                        claimSatisfied = true;
                }

                if (claimSatisfied)
                {
                    context.Succeed(requirement);
                    return;
                }
            }

            // ── Token al (header > cookie > claim) ─────────────────────────────
            string? accessToken = httpContext.Request.Headers["Authorization"].FirstOrDefault();

            if (string.IsNullOrEmpty(accessToken))
            {
                var cookieToken = httpContext.Request.Cookies["AccessToken"];
                if (!string.IsNullOrEmpty(cookieToken))
                    accessToken = $"Bearer {cookieToken}";
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                var tokenClaim = context.User?.FindFirst("AccessToken")?.Value;
                if (!string.IsNullOrEmpty(tokenClaim))
                    accessToken = $"Bearer {tokenClaim}";
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("İstekte erişim token'ı bulunamadı");
                context.Fail();
                return;
            }

            try
            {
                // ── ADIM 1: IsAuthenticate ──────────────────────────────────────
                var authClient = _httpClientFactory.CreateClient();
                var authReq = new HttpRequestMessage(HttpMethod.Get,
                    $"{_permissionServiceUrl}/api/v1/AuthCheck/IsAuthenticate");
                authReq.Headers.Add("Authorization", accessToken);

                var authResp = await authClient.SendAsync(authReq);
                if (!authResp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Uzak servis doğrulaması başarısız");
                    context.Fail();
                    return;
                }

                // ── ADIM 2: HasPermission ───────────────────────────────────────
                var permClient = _httpClientFactory.CreateClient();
                var permReq = new HttpRequestMessage(HttpMethod.Post,
                    $"{_permissionServiceUrl}/api/v1/Permission/HasPermission");
                permReq.Headers.Add("Authorization", accessToken);
                permReq.Content = JsonContent.Create(new
                {
                    GroupCode = requirement.GroupCode,
                    Codes = requirement.PermissionCode,
                    Operation = (int?)requirement.Operation
                });

                var permResp = await permClient.SendAsync(permReq);
                if (!permResp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("İzin yok. GrupKodu:{G} Kodlar:{C}",
                        requirement.GroupCode, string.Join(", ", requirement.PermissionCode));
                    context.Fail();
                    return;
                }

                var rawBody = await permResp.Content.ReadAsStringAsync();
                if (!PermissionApiResponseParser.TryParseBool(rawBody, out var hasPermission))
                {
                    _logger.LogWarning("HasPermission yanıtı okunamadı. GrupKodu:{G}", requirement.GroupCode);
                    context.Fail();
                    return;
                }

                if (hasPermission)
                    context.Succeed(requirement);
                else
                {
                    _logger.LogWarning("Kullanıcı izne sahip değil. GrupKodu:{G} İşlem:{O}",
                        requirement.GroupCode, requirement.Operation);
                    context.Fail();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GrupKodu:{G} için uzak servis ile izin doğrulama hatası",
                    requirement.GroupCode);
                context.Fail();
            }
        }
    }
}
