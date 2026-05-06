using AnketOtomasyonu.Authorization;

namespace AnketOtomasyonu.Authorization.Models
{
    /// <summary>
    /// <c>POST /api/v1/Permission/HasPermission</c> gövdesi (Swagger).
    /// <see cref="GroupCode"/> = <c>ANKET_API</c>;
    /// <see cref="Codes"/> = alt kod listesi (<c>ADMIN</c>, <c>SUPER_ADMIN</c>, <c>AKADEMIK</c>, <c>IDARI</c>, <c>STUDENT</c>).
    /// Tam kodlar çoğunlukla <c>ANKET_API_</c> + suffix; istisna idari = <c>ANKET_IDARI</c> (bkz. <see cref="AnketPermissions.Combine"/>).
    /// </summary>
    public class HasPermissionRequest
    {
        public string GroupCode { get; set; } = string.Empty;
        public List<string> Codes { get; set; } = new();
        public Operations? Operation { get; set; }

        /// <summary>Grup <c>ANKET_API</c>, istenen alt kodlar ve VEYA/VE mantığı.</summary>
        public static HasPermissionRequest ForAnketApi(Operations operation, IEnumerable<string> codeSuffixes) =>
            new()
            {
                GroupCode = AnketPermissions.GroupCode,
                Codes = codeSuffixes.ToList(),
                Operation = operation
            };

        /// <summary>Tek alt kod için kısayol.</summary>
        public static HasPermissionRequest ForAnketApiSingle(string codeSuffix, Operations operation = Operations.Or) =>
            new()
            {
                GroupCode = AnketPermissions.GroupCode,
                Codes = new List<string> { codeSuffix },
                Operation = operation
            };
    }
}
