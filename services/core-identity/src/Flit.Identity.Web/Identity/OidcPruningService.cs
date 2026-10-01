using OpenIddict.Abstractions;

namespace Flit.Api.Identity;

/// <summary>
/// Limpia <c>identity.oidc_tokens</c> y <c>identity.oidc_authorizations</c> (FLIT Suite A-05, HU #12990): cada código
/// y cada refresh token deja una fila, y sin limpieza la tabla crece sin fin (espiga A-04, pregunta 1). Borra lo
/// vencido o revocado de hace más de un día, cada 6 horas.
/// </summary>
internal sealed partial class OidcPruningService(IServiceProvider services, ILogger<OidcPruningService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var threshold = DateTimeOffset.UtcNow - Retention;
                var tokens = await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>()
                    .PruneAsync(threshold, stoppingToken).ConfigureAwait(false);
                var authorizations = await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>()
                    .PruneAsync(threshold, stoppingToken).ConfigureAwait(false);
                LogPruned(logger, tokens, authorizations);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPruneFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OIDC: limpieza de {Tokens} tokens y {Authorizations} autorizaciones vencidos.")]
    private static partial void LogPruned(ILogger logger, long tokens, long authorizations);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC: falló la limpieza de tokens; se reintenta en el próximo ciclo.")]
    private static partial void LogPruneFailed(ILogger logger, Exception exception);
}
