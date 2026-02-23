namespace AnketOtomasyonu.Authorization.Models
{
    /// <summary>LDAP doğrulama sonucu</summary>
    public class LdapAuthResult
    {
        public bool IsSuccess { get; set; }
        public string? TcKimlikNo { get; set; }
        public string? ErrorMessage { get; set; }
    }
}