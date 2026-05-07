using System.Text.Json.Serialization;

namespace AnketOtomasyonu.Authorization.Models
{
    /// <summary>Selçuk Test API GetProfile yanıtı</summary>
    public class CurrentUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int UserTypeId { get; set; }
        public long TcIdentityNo { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime TokenCreateDate { get; set; }
        public DateTime TokenExpireDate { get; set; }
        public Locale Locale { get; set; } = new();
        public ICollection<int>? UnitIds { get; set; }
        public string CorporateRegistrationNo { get; set; } = string.Empty;

        /// <summary>Bazı profillerde doğrudan birim adı (idari personel vb.).</summary>
        public string? BirimAdi { get; set; }

        public string? UnitName { get; set; }

        public string? PrimaryUnitName { get; set; }

        /// <summary>Öğrenci / akademik senaryolarında fakülte metni.</summary>
        public string? FakulteAdi { get; set; }

        public string? BolumAdi { get; set; }

        [JsonIgnore]
        public string? ResolvedBirimOrUnit =>
            FirstNonEmpty(BirimAdi, UnitName, PrimaryUnitName);

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return null;
        }
    }

    public class Locale
    {
        public int? Id { get; set; } = 1;
        public string? LanguageCode { get; set; } = "tr";
        public string? Culture { get; set; } = "tr-TR";
    }
}