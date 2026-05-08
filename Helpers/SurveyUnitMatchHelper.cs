using System.Security.Claims;
using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// Birim eşleşmesi: cookie’deki birim anahtarları ile anket hedef birimleri.
    /// Anket <b>doldurma</b> ve katılımcı listelerinde <c>includeAuthorizedUnits: false</c> kullanın —
    /// yönetici olarak yetkili olunan başka birimler anketi açmasın.
    /// Sonuç ekranında varsayılan <c>true</c> (yetkili birimlerde sonuç görebilme).
    /// </summary>
    public static class SurveyUnitMatchHelper
    {
        public static string NormalizeBirim(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR"));
        }

        /// <summary>
        /// <paramref name="includeAuthorizedUnits"/>: false = yalnız gerçek profil/katalog birimi (anket doldurma).
        /// </summary>
        public static HashSet<string> GetNormalizedBirimKeys(ClaimsPrincipal user, bool includeAuthorizedUnits = true)
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
            Add(user.FindFirstValue("BolumAdi"));

            if (includeAuthorizedUnits)
            {
                foreach (var c in user.FindAll("AuthorizedUnits"))
                    Add(c.Value);
            }

            return keys;
        }

        public static bool MatchesSurveyBirimStrings(ClaimsPrincipal user, string? createdByBirim, string? targetFaculties) =>
            MatchesSurveyBirimStrings(user, createdByBirim, targetFaculties, null, true);

        public static bool MatchesSurveyBirimStrings(
            ClaimsPrincipal user,
            string? createdByBirim,
            string? targetFaculties,
            HashSet<string>? additionalNormalizedKeys,
            bool includeAuthorizedUnits = true)
        {
            // MERKEZ oluşturucu: herkese serbest geçiş verilmez; hedef birim listesi veya UnitId zinciri eşleşmelidir.

            var userKeys = GetNormalizedBirimKeys(user, includeAuthorizedUnits);
            if (additionalNormalizedKeys != null)
            {
                foreach (var k in additionalNormalizedKeys)
                {
                    if (!string.IsNullOrEmpty(k))
                        userKeys.Add(k);
                }
            }

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

        public static string? MergeSummaryTargetCsv(
            SurveySummaryDto s,
            IReadOnlyDictionary<int, List<string>>? surveyBirimBySurveyId = null)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddCsv(string? csv)
            {
                if (string.IsNullOrWhiteSpace(csv)) return;
                foreach (var p in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var t = p.Trim();
                    if (t.Length > 0) set.Add(t);
                }
            }

            AddCsv(s.TargetFaculties);
            if (!string.IsNullOrWhiteSpace(s.UnitName))
                set.Add(s.UnitName.Trim());

            if (surveyBirimBySurveyId != null &&
                surveyBirimBySurveyId.TryGetValue(s.Id, out var rows))
            {
                foreach (var b in rows)
                {
                    if (!string.IsNullOrWhiteSpace(b))
                        set.Add(b.Trim());
                }
            }

            return set.Count > 0
                ? string.Join(",", set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                : null;
        }

        public static bool MatchesSurveySummary(
            ClaimsPrincipal user,
            SurveySummaryDto s,
            IReadOnlyDictionary<int, List<string>>? surveyBirimBySurveyId = null,
            bool includeAuthorizedUnits = true) =>
            MatchesSurveyBirimStrings(
                user,
                s.CreatedByBirim,
                MergeSummaryTargetCsv(s, surveyBirimBySurveyId),
                null,
                includeAuthorizedUnits);
    }
}
