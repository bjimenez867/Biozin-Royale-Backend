using Biozin_Royale_Backend.Dominio.InterfacesAD;

namespace Biozin_Royale_Backend.API.BackgroundServices;

/// Desactiva bonos (promotions) cuya fecha de expiración ya pasó.
/// Corre cada 30 min; IsActive queda desincronizado de EndsAt hasta el
/// próximo tick, igual que la ventana de liquidación de BetSettlementService.
public class BonusExpirationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BonusExpirationService> _logger;

    public BonusExpirationService(IServiceScopeFactory scopeFactory, ILogger<BonusExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ExpireAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error desactivando bonos expirados.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private Task ExpireAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitWork>();

        var now = DateTime.UtcNow;
        var expired = unitOfWork.Promotions
            .ObtenerEntidades(p => p.IsActive && p.EndsAt != null && p.EndsAt <= now)
            .ReturnValue!
            .ToList();

        if (expired.Count == 0) return Task.CompletedTask;

        foreach (var promo in expired)
        {
            promo.IsActive = false;
            unitOfWork.Promotions.Modificar(promo);
        }

        unitOfWork.Completar();
        return Task.CompletedTask;
    }
}
