namespace AnketOtomasyonu.Models.Entities
{
    /// <summary>
    /// Admin yetki tablosu.
    /// Akademik personel kullanıcı adı burada varsa, ilgili PersonelBirim için
    /// anket yönetme (Admin) yetkisi alır.
    /// </summary>
    public class AdminPermission
    {
        public int Id { get; set; }

        /// <summary>Personelin kurumsal kullanıcı adı (@ olmadan, örn: "elif_yucesoy")</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Yetki verilen birim adı (örn: "BİLGİSAYAR MÜHENDİSLİĞİ")</summary>
        public string PersonelBirim { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Opsiyonel açıklama (örn: "Bölüm Başkanı")</summary>
        public string? Note { get; set; }
    }
}
