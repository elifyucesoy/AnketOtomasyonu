using AnketOtomasyonu.Authorization.Models;
using System.Text.Json;

namespace AnketOtomasyonu.Services.Interfaces
{
    public interface IApiServicesAuthService
    {
        /// <summary>POST /api/v1/Auth/Login — başarılıysa access token.</summary>
        Task<string?> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);

        /// <summary>GET /api/v1/User/GetProfile — ham JSON (şema değişimine dayanıklı).</summary>
        Task<JsonDocument?> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default);

        /// <summary>POST /api/v1/Permission/HasPermission — başarılıysa sonuç; ağ/şema hatasında <c>null</c>.</summary>
        Task<bool?> HasPermissionAsync(string bearerToken, HasPermissionRequest request, CancellationToken cancellationToken = default);
    }
}
