namespace AnketOtomasyonu.Authorization.Models
{
    /// <summary>
    /// <c>POST .../Permission/HasPermission</c> gövdesindeki <c>operation</c> alanı (Swagger örneği: 0).
    /// </summary>
    public enum Operations
    {
        /// <summary>En az bir <c>codes</c> öğesi (VEYA) — genelde <c>0</c>.</summary>
        Or = 0,
        /// <summary>Tüm <c>codes</c> öğeleri (VE) — genelde <c>1</c>.</summary>
        And = 1
    }
}