namespace AnketOtomasyonu.Models.Entities
{
    public enum SurveyStatus
    {
        Draft = 0,
        Active = 1,
        Inactive = 2,
        Closed = 3
    }

    public enum ApprovalStatus
    {
        /// <summary>SuperAdmin onayı bekleniyor</summary>
        Pending = 0,
        /// <summary>SuperAdmin onayladı, admin yayınlayabilir</summary>
        Approved = 1,
        /// <summary>SuperAdmin reddetti</summary>
        Rejected = 2
    }

    public class Survey
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public SurveyStatus Status { get; set; } = SurveyStatus.Draft;
        public string CreatedByUserId { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string TargetRoles { get; set; } = string.Empty;
        public bool IsAnonymous { get; set; } = false;

        /// <summary>Çoklu Fakülte eşleşmesi (Virgülle ayrılmış)</summary>
        public string? TargetFaculties { get; set; }

        /// <summary>Çoklu Bölüm eşleşmesi (Virgülle ayrılmış)</summary>
        public string? TargetDepartments { get; set; }

        /// <summary>Anketi oluşturan adminin birimi (Normal admin filtresi için)</summary>
        public string? CreatedByBirim { get; set; }

        // ── SUPERADMIN ONAY SİSTEMİ ──────────────────────────────────────────
        /// <summary>
        /// SuperAdmin'in onay durumu.
        /// Pending: Henüz incelenmedi, Approved: Onaylandı (yayınlanabilir), Rejected: Reddedildi
        /// </summary>
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;

        /// <summary>SuperAdmin'in onay/ret notu (isteğe bağlı)</summary>
        public string? ApprovalNote { get; set; }

        /// <summary>Onaylandığı/reddedildiği tarih</summary>
        public DateTime? ApprovedAt { get; set; }

        public ICollection<Question> Questions { get; set; } = new List<Question>();
        public ICollection<SurveyResponse> Responses { get; set; } = new List<SurveyResponse>();
    }
}