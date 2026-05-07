namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// CreateSurvey formunda «Tüm birimler» seçildiğinde POST gövdesinde yalnızca bu jeton gider;
    /// <see cref="Services.Implementations.SurveyService"/> sunucuda tam birim listesine çevirir.
    /// </summary>
    public static class SurveyTargetAllUnits
    {
        public const string Token = "__ALL__";

        public static bool ContainsAllToken(IEnumerable<string>? values)
        {
            if (values == null) return false;
            foreach (var v in values)
            {
                if (string.Equals(v?.Trim(), Token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
