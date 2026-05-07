using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>Anket kartlarında gösterilecek hedef birim adları (SurveyBirim → TargetFaculties yedeği).</summary>
    public static class SurveyTargetUnitsHelper
    {
        public static List<string> Resolve(SurveySummaryDto s, IReadOnlyDictionary<int, List<string>> fromSurveyBirimTable)
        {
            if (fromSurveyBirimTable.TryGetValue(s.Id, out var dbList) && dbList.Count > 0)
                return dbList;

            if (!string.IsNullOrWhiteSpace(s.TargetFaculties))
            {
                return s.TargetFaculties
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new List<string>();
        }
    }
}
