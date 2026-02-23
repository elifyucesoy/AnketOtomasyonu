namespace AnketOtomasyonu.Models.DTOs
{
    public class LoginResponseDto
    {
        public bool IsSucceeded { get; set; }
        public LoginErrorDto? Error { get; set; }
        public LoginValueDto? Value { get; set; }
    }

    public class LoginValueDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public string? RefreshToken { get; set; }
    }

    public class LoginErrorDto
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}