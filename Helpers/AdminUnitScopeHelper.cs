using System.Globalization;
using System.Security.Claims;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// Birim Admin: varsayılan birim (PersonelBirim) + SuperAdmin tarafından atanmış AuthorizedUnits claim’leri.
    /// </summary>
    public static class AdminUnitScopeHelper
    {
        public static List<string> GetAuthorizedUnitNames(ClaimsPrincipal user)
        {
            var tr = new CultureInfo("tr-TR");
            var list = user.FindAll("AuthorizedUnits")
                .Select(c => c.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x.ToUpper(tr))
                .Select(g => g.First().Trim())
                .ToList();

            var birim = user.FindFirstValue("PersonelBirim");
            if (!string.IsNullOrWhiteSpace(birim) &&
                !list.Any(x => string.Equals(x, birim, StringComparison.OrdinalIgnoreCase)))
                list.Add(birim.Trim());

            return list;
        }

        public static HashSet<string> GetAuthorizedUnitNameSet(ClaimsPrincipal user) =>
            new(GetAuthorizedUnitNames(user), StringComparer.OrdinalIgnoreCase);

        /// <summary>Oluşturma / hedef birim alanı izin verilen kümede mi (sunucu tarafı zorunlu doğrulama).</summary>
        public static bool IsBirimAllowed(string? birim, HashSet<string> authorized, bool allowMerkezForMultiScope)
        {
            if (string.IsNullOrWhiteSpace(birim)) return false;
            var b = birim.Trim();
            if (authorized.Contains(b)) return true;
            if (allowMerkezForMultiScope &&
                string.Equals(b, "MERKEZ", StringComparison.OrdinalIgnoreCase) &&
                authorized.Count > 1)
                return true;
            return false;
        }
    }
}
