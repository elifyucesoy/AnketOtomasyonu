using AnketOtomasyonu.Services.Interfaces;

namespace AnketOtomasyonu.Services
{
    /// <summary>
    /// Ayda bir kez Unit ve UnitType listelerini API'den çekip cache'i yenileyen background job.
    /// İlk başlatmada 5 dakika bekler (uygulama ayağa kalksın), sonra 30 günde bir çalışır.
    /// </summary>
    public class UnitSyncBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UnitSyncBackgroundService> _logger;

        private static readonly TimeSpan InitialDelay  = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SyncInterval  = TimeSpan.FromDays(30);

        public UnitSyncBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<UnitSyncBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[UnitSync] Background job başlatıldı. İlk senkronizasyon {D} sonra.", InitialDelay);

            // Uygulama başlarken bekle
            await Task.Delay(InitialDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunSyncAsync(stoppingToken);

                _logger.LogInformation("[UnitSync] Bir sonraki senkronizasyon {D} sonra.", SyncInterval);
                await Task.Delay(SyncInterval, stoppingToken);
            }
        }

        private async Task RunSyncAsync(CancellationToken ct)
        {
            try
            {
                using var scope      = _scopeFactory.CreateScope();
                var unitApiService   = scope.ServiceProvider.GetRequiredService<IUnitApiService>();
                var configuration    = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                // Cache zaten doluysa atla (elle tetiklenmediyse)
                if (unitApiService.IsCached())
                {
                    _logger.LogInformation("[UnitSync] Cache dolu, otomatik senkronizasyon atlandı.");
                    return;
                }

                // Servis hesabı token'ı yoksa GetAllUnitsAsync kendi alır
                var (unitCount, typeCount) = await unitApiService.ForceRefreshAsync(string.Empty);

                _logger.LogInformation("[UnitSync] Senkronizasyon tamamlandı: {U} birim, {T} bölüm.", unitCount, typeCount);
            }
            catch (OperationCanceledException)
            {
                // Normal kapatma
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UnitSync] Senkronizasyon başarısız, bir sonraki döngüde tekrar denecek.");
            }
        }
    }
}
