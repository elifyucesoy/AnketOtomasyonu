using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

namespace AnketOtomasyonu.Authorization
{
    public interface IAuthServiceHandler
    {
        Task<bool> ValidateAuthServiceAsync(string accessToken);
        Task<bool> ValidatePermissionServiceAsync(string accessToken, string groupCode, List<string> permissionCodes, Operations? operation = Operations.Or);
        Task<CurrentUser?> GetCurrentUser();
    }
    public class AuthServiceHandler : IAuthServiceHandler
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _accessor;
        private readonly ILogger<AuthServiceHandler> _logger;
        private readonly string _permissionServiceUrl;

        public AuthServiceHandler(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AuthServiceHandler> logger,
            IHttpContextAccessor accessor
        )
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _accessor = accessor;
            _logger = logger;
            _permissionServiceUrl = _configuration["PermissionService:BaseUrl"]
                ?? throw new InvalidOperationException("PermissionService:BaseUrl is not configured");
        }
        public async Task<CurrentUser?> GetCurrentUser()
        {
            // YENİ
            var token = _accessor.HttpContext?.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                token = _accessor.HttpContext?.Session.GetString("AccessToken");
            }
            try
            {
                var httpClient = _httpClientFactory.CreateClient();

                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_permissionServiceUrl}/api/v1/Auth/GetProfile");
                request.Headers.Add("Authorization", $"{token}");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var userResponse = JsonSerializer.Deserialize<CurrentUser>(content,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                        if (userResponse != null)
                        {
                            return userResponse;
                        }
                    }

                }
                return null;

            }
            catch (Exception x)
            {
                _logger.LogWarning("GetCurrentUser failed with message: {Message}", x.Message);
                return null;
            }
        }
        public async Task<bool> ValidateAuthServiceAsync(string accessToken)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();

                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_permissionServiceUrl}/api/v1/AuthCheck/IsAuthenticate");
                request.Headers.Add("Authorization", $"{accessToken}");

                var response = await httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;

            }
            catch (Exception ex)
            {
                return false;
            }
        }
        public async Task<bool> ValidatePermissionServiceAsync(string accessToken, string groupCode, List<string> permissionCodes, Operations? operation = Operations.Or)
        {
            try
            {
                if (!permissionCodes.Any())
                {
                    _logger.LogWarning("Permission codes list is null or empty");
                    return false;
                }

                var httpClient = _httpClientFactory.CreateClient();

                var requestBody = new HasPermissionRequest
                {
                    GroupCode = groupCode,
                    Codes = permissionCodes,
                    Operation = operation
                };

                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{_permissionServiceUrl}/api/v1/Permission/HasPermission");
                request.Headers.Add("Authorization", $"{accessToken}");
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<bool>(content,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    return result;
                }
                else
                {
                    _logger.LogWarning("Permission check failed with status code: {StatusCode} for groupCode: {GroupCode}",
                        response.StatusCode, groupCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating permissions with remote service for groupCode: {GroupCode}",
                    groupCode);
                return false;
            }
        }
    }
    public class PermissionResponse
    {
        public string Code { get; set; }
        public bool HasPermission { get; set; }
    }

    public class HasPermissionRequest
    {
        public string GroupCode { get; set; }
        public List<string> Codes { get; set; } = new List<string>();
        public Operations? Operation { get; set; } = Operations.Or;
    }
    public class CurrentUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Surname { get; set; } = "";
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public UserType UserTypeId { get; set; } = 0;
        public long TcIdentityNo { get; set; } = 0;
        public string Token { get; set; } = "";
        public DateTime TokenCreateDate { get; set; } = DateTime.Now;
        public DateTime TokenExpireDate { get; set; } = DateTime.Now;
        public Locale Locale { get; set; } = new Locale();
        public ICollection<int>? UnitIds { get; set; }
        public string CorporateRegistrationNo { get; set; } = "";
    }
    public class AuthServiceRequirement : IAuthorizationRequirement
    {
        public string GroupCode { get; }
        public List<string> PermissionCode { get; }
        public Operations? Operation { get; }

        public AuthServiceRequirement(string groupCode, List<string> permissionCode, Operations? operation = Operations.Or)
        {
            GroupCode = groupCode;
            PermissionCode = permissionCode;
            Operation = operation;
        }
    }
    public enum Operations
    {
        And = 0,
        Or = 1
    }
    public enum UserType
    {
        Employee = 0,
        Student = 1,
        InstituteStudent = 2,
        GuestEmployee = 3,
        SystemUser = 4,
        SystemAdmin = 5,
    }
    public class AuthServicePermissionHandler : AuthorizationHandler<AuthServiceRequirement>
    {
        private readonly IAuthServiceHandler _authHandler;
        private readonly ILogger<AuthServicePermissionHandler> _logger;

        public AuthServicePermissionHandler(
            IAuthServiceHandler authHandler,
            ILogger<AuthServicePermissionHandler> logger)
        {
            _authHandler = authHandler;
            _logger = logger;
        }

        protected async override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            AuthServiceRequirement requirement)
        {
            var httpContext = context.Resource as HttpContext;
            if (httpContext == null)
            {
                _logger.LogWarning("HttpContext is null in RemotePermissionHandler");
                return;
            }

            var accessToken = httpContext.Request.Headers["Authorization"]
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                accessToken = httpContext.Session.GetString("AccessToken");
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("No access token found in request");
                return;
            }

            var hasPermission = await _authHandler.ValidatePermissionServiceAsync(
                accessToken,
                requirement.GroupCode,
                requirement.PermissionCode,
                requirement.Operation);
            if (hasPermission)
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning(
                    "User does not have permission. GroupCode: {GroupCode}, PermissionCodes: {PermissionCodes}, Operation: {Operation}",
                    requirement.GroupCode,
                    string.Join(", ", requirement.PermissionCode),
                    requirement.Operation);
            }
        }
    }
    public class Locale
    {
        public int? Id { get; set; } = 1;
        public string? LanguageCode { get; set; } = "tr";
        public string? Culture { get; set; } = "tr-TR";
    }
}