using Biozin_Royale_Backend.Dominio.InterfacesAD;

namespace Biozin_Royale_Backend.API.BackgroundServices;

/// Las mesas de blackjack viven en memoria: si el servidor se reinicia a mitad
/// de ronda, las apuestas ya debitadas quedarían huérfanas (status=pending).
/// Este servicio corre una vez al arrancar y devuelve ese dinero a las wallets.
public class BlackjackRefundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlackjackRefundService> _logger;

    public BlackjackRefundService(IServiceScopeFactory scopeFactory, ILogger<BlackjackRefundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitWork>();

            var pendientes = await unitOfWork.GamesHistory
                .ObtenerEntidadesAsync(b => b.GameType == "blackjack" && b.Status == "pending");

            if (pendientes.Count == 0) return;

            var now = DateTime.UtcNow;
            foreach (var bet in pendientes)
            {
                var wallet = await unitOfWork.Wallets.ObtenerEntidadAsync(w => w.UserId == bet.UserId);
                if (wallet is not null)
                {
                    wallet.Balance = Math.Round(wallet.Balance + bet.Amount, 2);
                    wallet.UpdatedAt = now;
                    unitOfWork.Wallets.Modificar(wallet);
                }

                // push = recuperó su apuesta; así el historial no muestra un estado raro
                bet.Payout = bet.Amount;
                bet.Profit = 0m;
                bet.Result = "push";
                bet.Status = "settled";
                bet.SettledAt = now;
                unitOfWork.GamesHistory.Modificar(bet);
            }

            await unitOfWork.CompletarAsync();
            _logger.LogWarning(
                "Blackjack: {Count} apuestas pendientes reembolsadas tras reinicio del servidor.",
                pendientes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reembolsando apuestas de blackjack pendientes.");
        }
    }
}
