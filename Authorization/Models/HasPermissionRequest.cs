namespace AnketOtomasyonu.Authorization.Models
{
    /// <summary>Selçuk Test API HasPermission isteği</summary>
    public class HasPermissionRequest
    {
        public string GroupCode { get; set; } = string.Empty;
        public List<string> Codes { get; set; } = new();
        public Operations? Operation { get; set; }
    }
}