using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// Doldurma ekranında <see cref="Survey.TargetFaculties"/> ile <see cref="SurveyBirim"/> satırlarını birleştirir;
    /// yalnızca sütundan biri dolu kaldığında öğrenci erişiminin kırılmaması için.
    /// </summary>
    public static class SurveyFillAccessHelper
    {
        /// <summary>Yalnızca hedef liste (anket kartı / rapor).</summary>
        public static string? BuildTargetFacultiesCsv(Survey survey) => BuildAccessCsv(survey);

        /// <summary>
        /// Doldurma yetkisi: hedef birimler + formda seçilen <see cref="Survey.UnitName"/> +
        /// isteğe bağlı UnitList’ten çözülen ek ad (admin anketlerinde hedef çoklusu boş kalsa bile).
        /// </summary>
        public static string? BuildAccessCsv(Survey survey, params string?[] extraCatalogUnitNames)
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

            void AddOne(string? s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    set.Add(s.Trim());
            }

            AddCsv(survey.TargetFaculties);
            if (survey.TargetUnits != null)
            {
                foreach (var row in survey.TargetUnits)
                    AddOne(row.Birim);
            }

            AddOne(survey.UnitName);
            foreach (var e in extraCatalogUnitNames)
                AddOne(e);

            return set.Count > 0
                ? string.Join(",", set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                : null;
        }
    }
}
