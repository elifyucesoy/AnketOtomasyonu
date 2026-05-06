using AnketOtomasyonu.Services.Interfaces;

namespace AnketOtomasyonu.Services
{
    /// <summary>Ayda bir UnitList + UnitTypeList önbelleğini sistem hesabıyla yeniler.</summary>
    public sealed class UnitCatalogSyncHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UnitCatalogSyncHostedService> _logger;

        public UnitCatalogSyncHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<UnitCatalogSyncHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var catalog = scope.ServiceProvider.GetRequiredService<IUnitCatalogService>();
                    await catalog.RefreshCatalogAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ApiServices] Zamanlanmış birim senkronu başarısız.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromDays(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
