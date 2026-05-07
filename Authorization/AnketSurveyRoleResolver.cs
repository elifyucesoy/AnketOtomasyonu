using System.Security.Claims;

namespace AnketOtomasyonu.Authorization
{
    /// <summary>
    /// GetProfile / UnitIds ile uyumlu: ANKET_API grubundaki beş izin kodunun tamamı için
    /// tek tip öncelik (login JobRecordType ve anket hedef tipi).
    /// </summary>
    public static class AnketSurveyRoleResolver
    {
        /// <summary>
        /// Cookie <c>JobRecordType</c> claim'i — login sırasında <paramref name="granted"/> + ana rol ile atanır.
        /// </summary>
        public static string ResolveJobRecordType(IReadOnlyCollection<string> granted, string primaryRole)
        {
            if (granted.Contains(AnketPermissions.Idari))
                return "Idari";

            if (string.Equals(primaryRole, "Student", StringComparison.OrdinalIgnoreCase))
                return "";

            return "Akademik";
        }

        /// <summary>
        /// Anket hedef rol satırıyla ilk eşleşme (Employee, Student, Akademik, Idari) —
        /// çoklu izin için claim önceliği; kalan eşleşme anket doldurma tarafındaki izin matrisindedir.
        /// </summary>
        public static string ResolveSurveyUserRoleType(ClaimsPrincipal user)
        {
            var role = user.FindFirstValue(ClaimTypes.Role);

            // 1) Öğrenci — UserTypeId veya ANKET_API_STUDENT
            var ut = user.FindFirstValue("UserTypeId");
            if (ut == "1"
                || user.HasClaim(AnketPermissions.ClaimType, AnketPermissions.Student))
                return "Student";

            // 2) Ana rol Akademik
            if (string.Equals(role, "Akademik", StringComparison.OrdinalIgnoreCase))
                return "Akademik";

            // 3) ANKET_IDARI (Admin / SuperAdmin / Akademik ile birlikte olabilir)
            if (user.HasClaim(AnketPermissions.ClaimType, AnketPermissions.Idari)
                || string.Equals(user.FindFirstValue("JobRecordType"), "Idari", StringComparison.OrdinalIgnoreCase))
                return "Idari";

            // 4) ANKET_API_AKADEMIK veya akademik iş kaydı
            if (user.HasClaim(AnketPermissions.ClaimType, AnketPermissions.Akademik)
                || string.Equals(user.FindFirstValue("JobRecordType"), "Akademik", StringComparison.OrdinalIgnoreCase))
                return "Akademik";

            // 5) ANKET_API_ADMIN / ANKET_API_SUPER_ADMIN — çoğu personel hedefi (Employee, Admin, …) ile ilk satır eşlemesi
            if (user.HasClaim(AnketPermissions.ClaimType, AnketPermissions.Admin)
                || user.HasClaim(AnketPermissions.ClaimType, AnketPermissions.SuperAdmin))
                return "Employee";

            // 6) Varsayılan personel / öğrenci ayrımı
            return ut == "0" ? "Employee" : "Student";
        }
    }
}
