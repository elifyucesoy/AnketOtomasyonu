namespace AnketOtomasyonu.Models.Entities
{
    public enum SurveyStatus
    {
        Draft = 0,
        Active = 1,
        Inactive = 2,
        Closed = 3
    }

    public enum SurveyType
    {
        /// <summary>Normal anket — standart akış</summary>
        Normal = 0,
        /// <summary>Ders değerlendirme anketi — OBIS SOAP kimlik doğrulama + ders seçimi akışı</summary>
        CourseEvaluation = 1
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

        /// <summary>
        /// Anket tipi. Normal: standart akış, CourseEvaluation: OBIS kimlik doğrulama + ders seçimi.
        /// </summary>
        public SurveyType SurveyType { get; set; } = SurveyType.Normal;
        public string CreatedByUserId { get; set; } = string.Empty;
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

        // ── BİRİM BİLGİSİ (UnitList'ten) ─────────────────────────────────────
        /// <summary>
        /// SuperAdmin'in anket oluştururken seçtiği birimin API'deki Id'si.
        /// UnitList endpointinden gelen birim Id'si. Raporlama ve filtreleme için.
        /// </summary>
        public int? UnitId { get; set; }

        /// <summary>
        /// SuperAdmin'in anket oluştururken seçtiği birimin adı (metin olarak saklanır).
        /// UnitList endpointinden gelen birim adı. Raporlama kolaylığı için.
        /// </summary>
        public string? UnitName { get; set; }

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
        public ICollection<SurveyBirim> TargetUnits { get; set; } = new List<SurveyBirim>();
    }
}