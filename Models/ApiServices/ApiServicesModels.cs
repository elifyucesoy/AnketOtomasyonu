using System.Text.Json.Serialization;

namespace AnketOtomasyonu.Models.ApiServices
{
    /// <summary>API'den gelen birim satırı — property isimleri swagger ile uyumsuzsa JsonSerializer case-insensitive ile yakalanır.</summary>
    public sealed class ApiUnitItem
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Title { get; set; }

        [JsonPropertyName("parentId")]
        public int? ParentId { get; set; }

        [JsonIgnore]
        public string DisplayName => (Name ?? Title ?? "").Trim();
    }

    /// <summary>Birim alt tipi (ör. bölüm); UnitType alanı tip bilgisini taşır.</summary>
    public sealed class ApiUnitTypeItem
    {
        public int Id { get; set; }

        [JsonPropertyName("unitId")]
        public int UnitId { get; set; }
        public string? Name { get; set; }
        public string? Title { get; set; }
        /// <summary>Örn. "Bölüm", "Birim" — filtre için kullanılır.</summary>
        public string? Type { get; set; }
        public string? UnitType { get; set; }

        [JsonIgnore]
        public string DisplayName => (Name ?? Title ?? "").Trim();

        [JsonIgnore]
        public string TypeDiscriminator => (Type ?? UnitType ?? "").Trim();
    }

    public sealed class ResolvedLoginProfile
    {
        public IReadOnlyList<int> UnitIds { get; init; } = Array.Empty<int>();
        public string PrimaryUnitName { get; init; } = "";
        public string? DepartmentName { get; init; }
        /// <summary>Personel için birden fazla birimde yetki id eşlemesi.</summary>
        public IReadOnlyList<string> UnitNames { get; init; } = Array.Empty<string>();
    }
}
