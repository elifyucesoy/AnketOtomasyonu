namespace AnketOtomasyonu.Models
{
    /// <summary>
    /// appsettings.json'daki Bolumler listesindeki her bir bölüm kaydı.
    /// BirimId → hangi fakülte/MYO'ya bağlı olduğunu gösterir.
    /// </summary>
    public class BolumItem
    {
        public int Id { get; set; }

        /// <summary>
        /// Bağlı olduğu birimin Id'si (appsettings Birimler[].Id ile eşleşir).
        /// </summary>
        public int BirimId { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
