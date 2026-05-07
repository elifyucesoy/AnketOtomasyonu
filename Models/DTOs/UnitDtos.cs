using System.Text.Json.Serialization;

namespace AnketOtomasyonu.Models.DTOs
{
    // ─── UnitList API Response ────────────────────────────────────────────────────

    public class UnitListResponse
    {
        [JsonPropertyName("value")]
        public UnitListValue? Value { get; set; }
    }

    public class UnitListValue
    {
        [JsonPropertyName("items")]
        public List<UnitDto>? Items { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }
    }

    public class UnitDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("parentId")]
        public int? ParentId { get; set; }

        [JsonPropertyName("unitTypeId")]
        public int? UnitTypeId { get; set; }

        [JsonPropertyName("unitTypeName")]
        public string? UnitTypeName { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    // ─── UnitTypeList API Response ────────────────────────────────────────────────

    public class UnitTypeListResponse
    {
        [JsonPropertyName("value")]
        public UnitTypeListValue? Value { get; set; }
    }

    public class UnitTypeListValue
    {
        [JsonPropertyName("items")]
        public List<UnitTypeDto>? Items { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }
    }

    public class UnitTypeDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    // ─── Kullanıcının birim özeti (claims'e yazılır) ──────────────────────────────

    public class UserUnitInfo
    {
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public int? UnitTypeId { get; set; }
        public string UnitTypeName { get; set; } = string.Empty;
    }
}
