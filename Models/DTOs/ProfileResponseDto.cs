using AnketOtomasyonu.Authorization;

namespace AnketOtomasyonu.Models.DTOs
{
    /// <summary>
    /// /api/v1/Auth/GetProfile endpoint yanıtını karşılar.
    /// Login endpoint gibi { isSucceeded, error, value } yapısında gelir.
    /// value içinde userTypeId (0=Employee/Personel, 1=Student/Öğrenci) ve hasPermission alanları bulunur.
    /// </summary>
    public class ProfileResponseDto
    {
        public bool IsSucceeded { get; set; }
        public LoginErrorDto? Error { get; set; }
        public CurrentUser? Value { get; set; }
    }
}
