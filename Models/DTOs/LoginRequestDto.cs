using System.ComponentModel.DataAnnotations;

namespace AnketOtomasyonu.Models.DTOs
{
    /// <summary>Selçuk Test API login isteği</summary>
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "Kullanıcı adı zorunludur")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre zorunludur")]
        public string Password { get; set; } = string.Empty;

        public string DeviceToken { get; set; } = "string";
        public int Channel { get; set; } = 0; // 0=Web
    }
}