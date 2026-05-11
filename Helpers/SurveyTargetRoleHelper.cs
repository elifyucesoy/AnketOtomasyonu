using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using AnketOtomasyonu.Authorization;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// <para>Uzak sistemde <b>beş</b> ANKET izin kodu vardır; kullanıcıya bunların <b>alt kümesi</b> atanır (ör. yalnız 2 veya 3 kod).
    /// Oturumda hangi kodlar claim olarak yazılmışsa, hedef rol seçimi <b>sadece bu kodlara</b> göre değerlendirilir — GetProfile tipi kullanılmaz.</para>
    /// <para><b>Anket hedef listesi</b> (virgülle çoklu seçim): Katılımcı, seçilen etiketlerden <b>herhangi biriyle</b>
    /// kendi izinleri örtüşüyorsa uyar (VEYA). Çoklu izinli kullanıcıda birden fazla etiket geçerli olabilir.</para>
    /// <para><b>Etiket → tek kod</b>: Student→ANKET_API_STUDENT, Akademik→ANKET_API_AKADEMIK,
    /// <b>Personel</b> → <see cref="AnketPermissions.Idari"/> (<c>ANKET_IDARI</c>);
    /// <b>İdari</b> / <b>Employee</b> yalnızca eski anket satırları için tanınır (aynı kod).</para>
    /// <para><b>Süper admin</b>: Katılımda hedef rollerden muaf tutulmaz; ankette seçilen etiketlerden biri, oturumdaki ANKET kodlarıyla uyumlu olmalıdır.</para>
    /// </summary>
    public static class SurveyTargetRoleHelper
    {
        public static bool TargetRolesAllowUser(string? targetRolesCsv, ClaimsPrincipal user)
        {
            if (string.IsNullOrWhiteSpace(targetRolesCsv))
                return true;

            var allowedTypes = targetRolesCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return TargetRoleMatchesUser(allowedTypes, user);
        }

        /// <summary>
        /// Anket satırında seçilen etiketlerden en az biri, kullanıcıda atanmış ANKET kodlarıyla uyuyor mu (VEYA mantığı).
        /// </summary>
        public static bool TargetRoleMatchesUser(string[] allowedTypes, ClaimsPrincipal user)
        {
            if (allowedTypes.Length == 0)
                return true;

            if (user == null)
                return false;

            foreach (var raw in allowedTypes)
            {
                var t = raw.Trim();
                if (string.IsNullOrEmpty(t))
                    continue;

                var canonical = ResolveCanonicalPermissionCode(t);
                if (canonical != null)
                {
                    if (user.HasAnketPermission(canonical))
                        return true;
                    continue;
                }

                if (TargetTagMatchesUserPermissions(user, t))
                    return true;
            }

            return false;
        }

        /// <summary>Oturumda hangi ANKET kodları varsa onlara karşılık gelen hedef etiketleri (özet).</summary>
        public static IReadOnlyList<string> GetParticipantCategories(ClaimsPrincipal user)
        {
            var list = new List<string>();
            if (user.HasAnketPermission(AnketPermissions.Student))
                list.Add("Student");
            if (user.HasAnketPermission(AnketPermissions.Akademik))
                list.Add("Akademik");
            if (user.HasAnketPermission(AnketPermissions.Idari))
                list.Add("Personel");
            if (user.HasAnketPermission(AnketPermissions.Admin))
                list.Add("Admin");
            if (user.HasAnketPermission(AnketPermissions.SuperAdmin))
                list.Add("SuperAdmin");
            return list;
        }

        /// <summary>Oturumda şu an hangi ANKET izin kodları tanımlı (sıfırdan beşe kadar).</summary>
        public static IReadOnlyList<string> GetGrantedAnketPermissionCodes(ClaimsPrincipal user)
        {
            var codes = new List<string>();
            foreach (var code in AnketPermissions.AllCodes)
            {
                if (user.HasAnketPermission(code))
                    codes.Add(code);
            }

            return codes;
        }

        /// <summary>Tek hedef etiketi veya Swagger’daki tam kod, kullanıcının atanmış izinleriyle uyuyor mu.</summary>
        public static bool TargetTagMatchesUserPermissions(ClaimsPrincipal user, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            var k = tag.Trim();

            if (string.Equals(k, "Student", StringComparison.OrdinalIgnoreCase))
                return user.HasAnketPermission(AnketPermissions.Student);
            if (string.Equals(k, "Akademik", StringComparison.OrdinalIgnoreCase))
                return user.HasAnketPermission(AnketPermissions.Akademik);
            if (string.Equals(k, "Personel", StringComparison.OrdinalIgnoreCase))
                return user.HasAnketPermission(AnketPermissions.Idari);
            // Eski hedef metni
            if (string.Equals(k, "Idari", StringComparison.OrdinalIgnoreCase))
                return user.HasAnketPermission(AnketPermissions.Idari);
            if (string.Equals(k, "Admin", StringComparison.OrdinalIgnoreCase))
                return user.HasAnketPermission(AnketPermissions.Admin);
            if (string.Equals(k, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                return user.HasAnketPermission(AnketPermissions.SuperAdmin);
            // Eski anket satırlarında "Employee" hedefi kalmış olabilir
            if (string.Equals(k, "Employee", StringComparison.OrdinalIgnoreCase))
                return user.HasAnketPermission(AnketPermissions.Idari);

            return false;
        }

        private static string? ResolveCanonicalPermissionCode(string token)
        {
            if (!token.StartsWith("ANKET_", StringComparison.OrdinalIgnoreCase))
                return null;

            return AnketPermissions.AllCodes.FirstOrDefault(c =>
                string.Equals(c, token, StringComparison.OrdinalIgnoreCase));
        }
    }
}
