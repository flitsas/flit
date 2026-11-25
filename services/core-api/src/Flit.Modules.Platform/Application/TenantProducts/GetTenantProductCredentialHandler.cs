using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Entrega la API key del impuesto a <c>svc-consultas</c> (ADR-0092): fail-closed si Diagnóstico está
/// apagado en la empresa o en un ancestro; no descifra si no hay entrega.
/// </summary>
public sealed class GetTenantProductCredentialHandler
{
    private readonly IProductAccessStore _access;
    private readonly ITenantProductCredentialRepository _credentials;
    private readonly ITenantProductSecretProtector _protector;

    public GetTenantProductCredentialHandler(
        IProductAccessStore access,
        ITenantProductCredentialRepository credentials,
        ITenantProductSecretProtector protector)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async Task<TenantProductCredentialDelivery> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var chain = await _access.GetTenantChainAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (chain.Count == 0)
            return TenantProductCredentialDelivery.Unavailable(MotivoCredencialImpuesto.ProductoApagado);

        var enabled = await _access.GetTenantsWithProductEnabledAsync(chain, TenantProductCredentialCodes.Diagnostico, cancellationToken)
            .ConfigureAwait(false);
        if (!chain.All(enabled.Contains))
            return TenantProductCredentialDelivery.Unavailable(MotivoCredencialImpuesto.ProductoApagado);

        var row = await _credentials.GetAsync(
                tenantId,
                TenantProductCredentialCodes.Diagnostico,
                TenantProductCredentialCodes.ImpuestoApiKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return TenantProductCredentialDelivery.Unavailable(MotivoCredencialImpuesto.SinConfigurar);

        var value = _protector.Unprotect(tenantId, TenantProductCredentialCodes.ImpuestoApiKey, row.SecretProtected);
        return TenantProductCredentialDelivery.Available(value, row.RowVersion);
    }
}

/// <summary>Motivo de no entrega (espejo del enum proto, sin dependencia gRPC).</summary>
public enum MotivoCredencialImpuesto
{
    Unspecified = 0,
    SinConfigurar = 1,
    ProductoApagado = 2,
}

/// <summary>Resultado de la entrega. <see cref="ToString"/> nunca incluye el valor.</summary>
public sealed class TenantProductCredentialDelivery
{
    private TenantProductCredentialDelivery(bool disponible, MotivoCredencialImpuesto motivo, string? value, long version)
    {
        Disponible = disponible;
        Motivo = motivo;
        Value = value;
        Version = version;
    }

    public bool Disponible { get; }

    public MotivoCredencialImpuesto Motivo { get; }

    public string? Value { get; }

    public long Version { get; }

    public static TenantProductCredentialDelivery Available(string value, long version) =>
        new(true, MotivoCredencialImpuesto.Unspecified, value, version);

    public static TenantProductCredentialDelivery Unavailable(MotivoCredencialImpuesto motivo) =>
        new(false, motivo, null, 0);

    public override string ToString() =>
        $"TenantProductCredentialDelivery {{ Disponible = {Disponible}, Motivo = {Motivo}, Version = {Version} }}";
}
