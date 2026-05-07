namespace AnketOtomasyonu.Models.Entities
{
    /// <summary>
    /// apiservices.selcuk.edu.tr UnitList endpointinden haftalık senkronizasyonla
    /// çekilen ve local veritabanında saklanan birim kaydı.
    /// </summary>
    public class CachedUnit
    {
        /// <summary>UnitList API'den gelen birim Id'si (PK — identity değil, API Id'si).</summary>
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public int? UnitTypeId { get; set; }

        public string? UnitTypeName { get; set; }

        public bool IsActive { get; set; }

        /// <summary>Son başarılı senkronizasyon zamanı (UTC).</summary>
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
    }
}
