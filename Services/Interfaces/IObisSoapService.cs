using AnketOtomasyonu.Models.Obis;

namespace AnketOtomasyonu.Services.Interfaces
{
    /// <summary>
    /// Selçuk Üniversitesi OBIS SOAP web servisi üzerinden öğrenci kimlik doğrulama
    /// ve ders listesi çekme işlemlerini soyutlar.
    /// </summary>
    public interface IObisSoapService
    {
        /// <summary>
        /// OgrenciDersleriniGetir SOAP operasyonunu çağırır.
        /// Başarılı yanıtta <see cref="ObisDersListResult.Success"/> true,
        /// <see cref="ObisDersListResult.Courses"/> dolu olur.
        /// Başarısız veya hatalı yanıtta <see cref="ObisDersListResult.Success"/> false,
        /// <see cref="ObisDersListResult.ErrorMessage"/> doldurulur.
        /// </summary>
        Task<ObisDersListResult> GetOgrenciDersleriAsync(
            string ogrNo,
            string parola,
            CancellationToken cancellationToken = default);
    }
}
