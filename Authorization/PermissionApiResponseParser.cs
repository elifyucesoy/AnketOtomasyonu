using System.Text.Json;

namespace AnketOtomasyonu.Authorization
{
    public static class PermissionApiResponseParser
    {
        public static bool TryParseBool(string? raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var trimmed = raw.Trim();
            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(trimmed,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (doc.ValueKind == JsonValueKind.True) { value = true; return true; }
                if (doc.ValueKind == JsonValueKind.False) { value = false; return true; }
                if (doc.TryGetProperty("value", out var val))
                {
                    if (val.ValueKind == JsonValueKind.True) { value = true; return true; }
                    if (val.ValueKind == JsonValueKind.False) return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }
    }
}
