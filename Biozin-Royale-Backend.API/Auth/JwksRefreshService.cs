namespace Biozin_Royale_Backend.API.Auth;

/// Refresca las claves JWKS de Supabase de forma async cada 50 minutos,
/// manteniendo el caché caliente para que GetSigningKeys() nunca bloquee un thread.
public class JwksRefreshService : BackgroundService
{
    private readonly SupabaseJwksProvider _provider;
    private readonly ILogger<JwksRefreshService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(50);

    public JwksRefreshService(SupabaseJwksProvider provider, ILogger<JwksRefreshService> logger)
    {
        _provider = provider;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Carga inicial al arrancar el servidor: así el caché está listo
        // antes de que llegue el primer request autenticado.
        await TryRefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            await TryRefreshAsync(stoppingToken);
        }
    }

    private async Task TryRefreshAsync(CancellationToken ct)
    {
        try
        {
            await _provider.RefreshAsync(ct);
            _logger.LogDebug("Claves JWKS de Supabase actualizadas correctamente.");
        }
        catch (OperationCanceledException) { /* app cerrando */ }
        catch (Exception ex)
        {
            // Si falla el refresh se usa el caché anterior — la auth sigue funcionando.
            _logger.LogWarning(ex, "No se pudieron refrescar las claves JWKS. Se usará el caché anterior.");
        }
    }
}
