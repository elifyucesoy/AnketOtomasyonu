using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AnketOtomasyonu.Services
{
    /// <summary>
    /// Haftada bir kez System User ile UnitList endpointine istek atar,
    /// tüm birimleri hem MemoryCache'e hem de local CachedUnits tablosuna kaydeder.
    /// İlk başlatmada 5 dakika bekler (uygulama ayağa kalksın), sonra 7 günde bir çalışır.
    /// </summary>
    public class UnitSyncBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UnitSyncBackgroundService> _logger;

        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15); // İlk çalışma hızlı
        private static readonly TimeSpan SyncInterval = TimeSpan.FromDays(7);

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
                using var scope         = _scopeFactory.CreateScope();
                var unitApiService      = scope.ServiceProvider.GetRequiredService<IUnitApiService>();
                var db                  = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // 1. System User ile login ol, UnitList çek, MemoryCache yenile
                var (unitCount, typeCount) = await unitApiService.ForceRefreshAsync(string.Empty);
                _logger.LogInformation("[UnitSync] API'den {U} birim, {T} bölüm çekildi.", unitCount, typeCount);

                if (unitCount == 0)
                {
                    _logger.LogWarning("[UnitSync] Birim listesi boş döndü, DB yazımı atlandı.");
                    return;
                }

                // 2. Güncel birimleri al (cache'den — ForceRefresh zaten doldurdu)
                var units = await unitApiService.GetAllUnitsAsync();

                // 3. DB'ye upsert — mevcut kayıtları güncelle, yenileri ekle
                var now = DateTime.UtcNow;
                var incoming = units.Select(u => new CachedUnit
                {
                    Id           = u.Id,
                    Name         = u.Name,
                    ParentId     = u.ParentId,
                    UnitTypeId   = u.UnitTypeId,
                    UnitTypeName = u.UnitTypeName,
                    IsActive     = u.IsActive,
                    LastSyncedAt = now
                }).ToList();

                // Mevcut ID'leri çek
                var existingIds = (await db.CachedUnits.Select(x => x.Id).ToListAsync(ct)).ToHashSet();

                var toAdd    = incoming.Where(u => !existingIds.Contains(u.Id)).ToList();
                var toUpdate = incoming.Where(u =>  existingIds.Contains(u.Id)).ToList();

                if (toAdd.Count > 0)
                    await db.CachedUnits.AddRangeAsync(toAdd, ct);

                foreach (var upd in toUpdate)
                {
                    db.CachedUnits.Update(upd);
                }

                // Artık olmayan (deaktif) kayıtları sil
                var incomingIds = incoming.Select(u => u.Id).ToHashSet();
                var toDelete = await db.CachedUnits
                    .Where(u => !incomingIds.Contains(u.Id))
                    .ToListAsync(ct);
                if (toDelete.Count > 0)
                    db.CachedUnits.RemoveRange(toDelete);

                await db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[UnitSync] DB güncellendi: +{A} eklendi, ~{U} güncellendi, -{D} silindi. Toplam: {T}",
                    toAdd.Count, toUpdate.Count, toDelete.Count, incoming.Count);
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
