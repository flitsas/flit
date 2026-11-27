using System.Security.Cryptography;
using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14711 — entrega a <c>svc-consultas</c> el secreto del webhook del proveedor de impuesto de la empresa
/// (<c>diagnostico/impuesto_webhook_secret</c>) y, mientras siga vigente, el anterior (<c>impuesto_webhook_secret_anterior</c>):
/// vale <see cref="TenantProductCredentialCodes.VentanaSecretoAnterior"/> desde su <c>updated_at</c>. Con eso core-consultas
/// verifica la firma de los avisos sin perder los que salieron firmados con el secreto viejo durante la rotación. Fail-closed como
/// <see cref="GetTenantProductCredentialHandler"/>: con Diagnóstico apagado en la empresa o en un ancestro no descifra nada. La
/// versión es el <c>row_version</c> del secreto actual (la caché del consumidor se invalida cuando cambia). Nunca registra valores.
/// L3: si el secreto ANTERIOR no se descifra (<see cref="CryptographicException"/>, p. ej. llaves de Data Protection rotadas), se
/// ignora y se entrega solo el actual con un Warning 7424 sin valores; el actual sigue fallando hacia arriba (sin él no hay entrega).
/// </summary>
/// <example>
/// <code>
/// var entrega = await handler.HandleAsync(tenantId, ct);
/// if (entrega.Configurado) verificar(entrega.Actual!, entrega.Anterior);
/// </code>
/// </example>
public sealed partial class GetTenantProductWebhookSecretHandler
{
    private readonly IProductAccessStore _access;
    private readonly ITenantProductCredentialRepository _credentials;
    private readonly ITenantProductSecretProtector _protector;
    private readonly TimeProvider _clock;
    private readonly ILogger<GetTenantProductWebhookSecretHandler> _logger;

    public GetTenantProductWebhookSecretHandler(
        IProductAccessStore access,
        ITenantProductCredentialRepository credentials,
        ITenantProductSecretProtector protector,
        TimeProvider? clock = null,
        ILogger<GetTenantProductWebhookSecretHandler>? logger = null)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<GetTenantProductWebhookSecretHandler>.Instance;
    }

    public async Task<TenantProductWebhookSecretDelivery> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var chain = await _access.GetTenantChainAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (chain.Count == 0)
            return TenantProductWebhookSecretDelivery.Unavailable(MotivoCredencialImpuesto.ProductoApagado);

        var enabled = await _access.GetTenantsWithProductEnabledAsync(chain, TenantProductCredentialCodes.Diagnostico, cancellationToken)
            .ConfigureAwait(false);
        if (!chain.All(enabled.Contains))
            return TenantProductWebhookSecretDelivery.Unavailable(MotivoCredencialImpuesto.ProductoApagado);

        var actual = await _credentials.GetAsync(
                tenantId, TenantProductCredentialCodes.Diagnostico, TenantProductCredentialCodes.ImpuestoWebhookSecret, cancellationToken)
            .ConfigureAwait(false);
        if (actual is null)
            return TenantProductWebhookSecretDelivery.Unavailable(MotivoCredencialImpuesto.SinConfigurar);

        var anterior = await _credentials.GetAsync(
                tenantId, TenantProductCredentialCodes.Diagnostico, TenantProductCredentialCodes.ImpuestoWebhookSecretAnterior, cancellationToken)
            .ConfigureAwait(false);
        string? anteriorPlano = null;
        DateTimeOffset? venceEn = null;
        if (anterior is not null)
        {
            var vence = anterior.UpdatedAt + TenantProductCredentialCodes.VentanaSecretoAnterior;
            if (vence > _clock.GetUtcNow())
            {
                try
                {
                    anteriorPlano = _protector.Unprotect(tenantId, TenantProductCredentialCodes.ImpuestoWebhookSecretAnterior, anterior.SecretProtected);
                    venceEn = vence;
                }
                catch (CryptographicException)
                {
                    // L3: un anterior ilegible no debe tumbar la entrega del actual; solo se pierde la ventana de rotación.
                    LogAnteriorIlegible(_logger, tenantId);
                }
            }
        }

        var actualPlano = _protector.Unprotect(tenantId, TenantProductCredentialCodes.ImpuestoWebhookSecret, actual.SecretProtected);
        return TenantProductWebhookSecretDelivery.Available(actualPlano, anteriorPlano, venceEn, actual.RowVersion);
    }

    [LoggerMessage(EventId = 7424, EventName = "impuesto.webhook.secreto.anterior_ilegible", Level = LogLevel.Warning,
        Message = "impuesto.webhook.secreto.anterior_ilegible tenant={Tenant}: el secreto anterior no se pudo descifrar; se entrega solo el actual.")]
    private static partial void LogAnteriorIlegible(ILogger logger, Guid tenant);
}

/// <summary>Secreto del webhook para verificar avisos. <see cref="ToString"/> nunca incluye los valores.</summary>
public sealed class TenantProductWebhookSecretDelivery
{
    private TenantProductWebhookSecretDelivery(
        bool configurado, MotivoCredencialImpuesto motivo, string? actual, string? anterior, DateTimeOffset? anteriorVenceEn, long version)
    {
        Configurado = configurado;
        Motivo = motivo;
        Actual = actual;
        Anterior = anterior;
        AnteriorVenceEn = anteriorVenceEn;
        Version = version;
    }

    public bool Configurado { get; }

    public MotivoCredencialImpuesto Motivo { get; }

    public string? Actual { get; }

    /// <summary>Solo mientras siga vigente (<see cref="AnteriorVenceEn"/> en el futuro); null si no hay o ya venció.</summary>
    public string? Anterior { get; }

    public DateTimeOffset? AnteriorVenceEn { get; }

    /// <summary><c>row_version</c> del secreto actual; 0 sin secreto.</summary>
    public long Version { get; }

    public static TenantProductWebhookSecretDelivery Available(string actual, string? anterior, DateTimeOffset? anteriorVenceEn, long version) =>
        new(true, MotivoCredencialImpuesto.Unspecified, actual, anterior, anterior is null ? null : anteriorVenceEn, version);

    public static TenantProductWebhookSecretDelivery Unavailable(MotivoCredencialImpuesto motivo) =>
        new(false, motivo, null, null, null, 0);

    public override string ToString() =>
        $"TenantProductWebhookSecretDelivery {{ Configurado = {Configurado}, Motivo = {Motivo}, ConAnterior = {Anterior is not null}, Version = {Version} }}";
}
