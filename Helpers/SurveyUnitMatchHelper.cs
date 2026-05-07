using System.Security.Claims;
using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// GetProfile / çoklu birim: kullanıcıda hangi ANKET_* izinleri olursa olsun, cookie’deki <b>tüm</b> birim
    /// anahtarlarından biri anketi tanımlayan birimle örtüşürse erişim (tek PersonelBirim ile sınırlı değil).
    /// </summary>
    public static class SurveyUnitMatchHelper
    {
        public static string NormalizeBirim(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR"));
        }

        /// <summary>
        /// Tüm <c>UnitName</c> claim’leri, PersonelBirim, FakulteAdi, AuthorizedUnits — normalize edilmiş küme.
        /// </summary>
        public static HashSet<string> GetNormalizedBirimKeys(ClaimsPrincipal user)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string? s)
            {
                var n = NormalizeBirim(s);
                if (!string.IsNullOrEmpty(n))
                    keys.Add(n);
            }

            foreach (var c in user.FindAll("UnitName"))
                Add(c.Value);

            Add(user.FindFirstValue("PersonelBirim"));
            Add(user.FindFirstValue("FakulteAdi"));

            foreach (var c in user.FindAll("AuthorizedUnits"))
                Add(c.Value);

            return keys;
        }

        /// <summary>
        /// Anketin CreatedByBirim / TargetFaculties ile kullanıcının herhangi bir birimi eşleşiyor mu?
        /// </summary>
        public static bool MatchesSurveyBirimStrings(ClaimsPrincipal user, string? createdByBirim, string? targetFaculties)
        {
            if (NormalizeBirim(createdByBirim) == "MERKEZ")
                return true;

            var userKeys = GetNormalizedBirimKeys(user);
            if (userKeys.Count == 0)
                return false;

            var createdN = NormalizeBirim(createdByBirim);
            if (!string.IsNullOrEmpty(createdN) && userKeys.Contains(createdN))
                return true;

            if (string.IsNullOrWhiteSpace(targetFaculties))
                return false;

            foreach (var raw in targetFaculties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var tn = NormalizeBirim(raw);
                if (!string.IsNullOrEmpty(tn) && userKeys.Contains(tn))
                    return true;
            }

            return false;
        }

        public static bool MatchesSurveySummary(ClaimsPrincipal user, SurveySummaryDto s) =>
            MatchesSurveyBirimStrings(user, s.CreatedByBirim, s.TargetFaculties);
    }
}
