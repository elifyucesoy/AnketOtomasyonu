using System.Security.Claims;

namespace AnketOtomasyonu.Authorization
{
    /// <summary>Uzak izin servisi grup ve kod sabitleri; cookie claim tipi.</summary>
    public static class AnketPermissions
    {
        public const string GroupCode = "ANKET_API";

        /// <summary>Cookie’deki izin claim’i: her verilen izin için bir adet.</summary>
        public const string ClaimType = "AnketPermission";

        public const string SuperAdmin = "ANKET_API_SUPER_ADMIN";
        public const string Admin = "ANKET_API_ADMIN";
        public const string Akademik = "ANKET_API_AKADEMIK";
        public const string Idari = "ANKET_IDARI";
        public const string Student = "ANKET_API_STUDENT";

        /// <summary>
        /// Anket sonuç ekranına (ör. <c>Home/Results</c>) giriş: bu izinlerden en az biri gerekir.
        /// Kapsam ayrımı (tüm birimler vs. yalnızca kendi birimi) action içinde yapılır.
        /// </summary>
        public const string PolicySurveyResultsEntry = "ANKET_API_SURVEY_RESULTS_ENTRY";

        /// <summary>
        /// Tam kapsam sonuç erişimi (tüm fakülte/bölümler). Yalnızca <see cref="SuperAdmin"/>.
        /// Süper admin panelindeki sonuç aksiyonları bu policy ile işaretlenebilir.
        /// </summary>
        public const string PolicySurveyResultsFullAccess = "ANKET_API_SURVEY_RESULTS_FULL";

        /// <summary>
        /// Birim kapsamı sonuç erişimi. Yalnızca <see cref="Admin"/>; yalnızca kendi birimindeki anket sonuçları (detay mantığı eklenecek).
        /// </summary>
        public const string PolicySurveyResultsUnitAdmin = "ANKET_API_SURVEY_RESULTS_UNIT_ADMIN";

        public static readonly string[] AllCodes =
        {
            SuperAdmin, Admin, Akademik, Idari, Student
        };
    }

    public static class AnketPermissionClaims
    {
        public static bool HasAnketPermission(this ClaimsPrincipal? user, string permissionCode) =>
            user?.HasClaim(AnketPermissions.ClaimType, permissionCode) == true;
    }
}
