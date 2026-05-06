using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AnketOtomasyonu.Services
{
    /// <summary>
    /// Bitiş tarihi geçen aktif anketleri otomatik olarak Pasif'e alır.
    /// Her 5 dakikada bir çalışır.
    /// </summary>
    public class SurveyExpirationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SurveyExpirationWorker> _logger;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

        public SurveyExpirationWorker(
            IServiceProvider serviceProvider,
            ILogger<SurveyExpirationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger          = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[ExpirationWorker] Başlatıldı. Kontrol aralığı: {I} dk.", CheckInterval.TotalMinutes);

            // Uygulama tamamen başlayana kadar kısa bir süre bekle
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExpireOverdueSurveysAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ExpirationWorker] Kontrol sırasında hata oluştu.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        private async Task ExpireOverdueSurveysAsync()
        {
            using var scope  = _serviceProvider.CreateScope();
            var db           = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now          = DateTime.UtcNow;

            var expired = await db.Surveys
                .Where(s => s.Status == SurveyStatus.Active
                         && s.EndDate != null
                         && s.EndDate < now)
                .ToListAsync();

            if (!expired.Any()) return;

            foreach (var survey in expired)
            {
                survey.Status    = SurveyStatus.Inactive;
                survey.UpdatedAt = now;
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("[ExpirationWorker] {N} anket süresi dolduğu için pasife alındı.", expired.Count);
        }
    }
}
