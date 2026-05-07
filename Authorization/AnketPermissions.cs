using System;
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
        /// <summary>Akademik kadro (anket hedefi “Akademik” ile eşlenir).</summary>
        public const string Akademik = "ANKET_API_AKADEMIK";
        /// <summary>İdari personel; anket hedeflerinde “Personel” (Employee) ve “İdari” bu izinle uyumludur.</summary>
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

        /// <summary>Beş izin kodu. Girişte verilen her biri ayrı claim olur; çoklu yetki = bu yetkilerin kümülatif (birleşik) kullanımı.</summary>
        public static readonly string[] AllCodes =
        {
            SuperAdmin, Admin, Akademik, Idari, Student
        };
    }

    public static class AnketPermissionClaims
    {
        public static bool HasAnketPermission(this ClaimsPrincipal? user, string permissionCode) =>
            user?.HasClaim(AnketPermissions.ClaimType, permissionCode) == true;

        /// <summary>
        /// Çoklu izin: ana rol tek seçilse bile cookie'de <see cref="AnketPermissions.SuperAdmin"/> varsa
        /// SuperAdmin UI / yayın / pasif anket davranışı uygulanır.
        /// </summary>
        public static bool HasSuperAdminAccess(this ClaimsPrincipal? user) =>
            user != null &&
            (user.HasAnketPermission(AnketPermissions.SuperAdmin) || user.IsInRole("SuperAdmin"));

        /// <summary>
        /// Çoklu izin: <see cref="AnketPermissions.Admin"/> / <see cref="AnketPermissions.Akademik"/> /
        /// <see cref="AnketPermissions.Idari"/> kodlarından <b>herhangi biri</b> cookie’de varsa (birim listesi / sonuç için).
        /// </summary>
        public static bool HasAnyStaffSurveyPermission(this ClaimsPrincipal? user) =>
            user != null &&
            (user.HasAnketPermission(AnketPermissions.Admin) ||
             user.HasAnketPermission(AnketPermissions.Akademik) ||
             user.HasAnketPermission(AnketPermissions.Idari));

        /// <summary>
        /// Login’de tek ana rol atanır; burada personel tipi roller (öğrenci hariç) — çoklu izinle birlikte kullanılır.
        /// </summary>
        public static bool IsStaffSurveyRole(string? roleClaim) =>
            roleClaim != null &&
            (roleClaim.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
             roleClaim.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
             roleClaim.Equals("Akademik", StringComparison.OrdinalIgnoreCase) ||
             roleClaim.Equals("Employee", StringComparison.OrdinalIgnoreCase) ||
             roleClaim.Equals("Idari", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Ana sayfa: taslak/pasif dahil geniş anket kataloğu — süper admin veya personel izinlerinden en az biri / ilgili rol.
        /// </summary>
        public static bool HasStaffSurveyExtendedCatalogAccess(this ClaimsPrincipal? user, string? roleClaim) =>
            user != null &&
            (user.HasSuperAdminAccess() ||
             user.HasAnyStaffSurveyPermission() ||
             IsStaffSurveyRole(roleClaim));

        /// <summary>
        /// Sonuç görüntüleme: birim eşleştiğinde Admin / Akademik / İdari yetkilerinden <b>biri yeterli</b> (çoklu izin birleşimi).
        /// </summary>
        public static bool HasAnySurveyResultsStaffCapability(this ClaimsPrincipal? user, string? roleClaim)
        {
            if (user == null) return false;
            if (user.HasAnketPermission(AnketPermissions.Admin)) return true;
            if (user.HasAnketPermission(AnketPermissions.Akademik)) return true;
            if (user.HasAnketPermission(AnketPermissions.Idari)) return true;
            if (roleClaim == null) return false;
            if (roleClaim.Equals("Admin", StringComparison.OrdinalIgnoreCase)) return true;
            if (roleClaim.Equals("Akademik", StringComparison.OrdinalIgnoreCase)) return true;
            if (roleClaim.Equals("Employee", StringComparison.OrdinalIgnoreCase)) return true;
            if (roleClaim.Equals("Idari", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
