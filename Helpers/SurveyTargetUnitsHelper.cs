using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>Anket kartlarında gösterilecek hedef birim adları (SurveyBirim → TargetFaculties yedeği).</summary>
    public static class SurveyTargetUnitsHelper
    {
        /// <summary>HomeController / SurveyResponseController için.</summary>
        public static List<string> Resolve(SurveySummaryDto s, IReadOnlyDictionary<int, List<string>> fromSurveyBirimTable)
            => ResolveCore(s, fromSurveyBirimTable);

        /// <summary>SuperAdminController için — nullable Dictionary overload.</summary>
        public static List<string> ResolveFromDto(SurveySummaryDto s, IReadOnlyDictionary<int, List<string>>? fromSurveyBirimTable)
            => ResolveCore(s, fromSurveyBirimTable);

        private static List<string> ResolveCore(SurveySummaryDto s, IReadOnlyDictionary<int, List<string>>? map)
        {
            if (map != null && map.TryGetValue(s.Id, out var dbList) && dbList.Count > 0)
                return dbList;

            if (!string.IsNullOrWhiteSpace(s.TargetFaculties))
            {
                var parsed = s.TargetFaculties
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (SurveyTargetAllUnits.ContainsAllToken(parsed))
                    return new List<string> { "Tüm birimler" };

                return parsed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }

            var fallbacks = new List<string>();
            if (!string.IsNullOrWhiteSpace(s.UnitName))
                fallbacks.Add(s.UnitName.Trim());
            var cb = (s.CreatedByBirim ?? "").Trim();
            if (cb.Length > 0 &&
                !string.Equals(cb, "MERKEZ", StringComparison.OrdinalIgnoreCase) &&
                !fallbacks.Contains(cb, StringComparer.OrdinalIgnoreCase))
                fallbacks.Add(cb);

            return fallbacks;
        }
    }
}
