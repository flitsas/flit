using Flit.Infrastructure.Persistence;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Infrastructure.Platform;

/// <summary>
/// HU #14694 (plan §1.1, decisión E2; contracts/asyncapi/impuestos-events.v1.yaml) — trabajos dirigidos al servicio del
/// producto por la outbox del host que atiende el PUT: core-api (productor <c>tramites</c>, <c>FlitDbContext</c>) o
/// core-identity (productor <c>plataforma</c>, <c>IdentityDbContext</c>). Van al exchange <c>flit.&lt;producto&gt;</c>.
/// </summary>
/// <remarks>
/// Fail-closed: si el host no registró la outbox, encolar lanza en vez de perder el trabajo (la transacción del cambio se
/// revierte con él). Los productos sin aprovisionamiento nunca llegan aquí.
/// </remarks>
internal sealed class TenantProductChangeJobsOutbox(IPlatformOutbox? outbox, IIdentityDb db) : ITenantProductChangeJobs
{
    /// <summary>Versión del sobre de los dos trabajos.</summary>
    public const int Version = 1;

    private readonly IPlatformOutbox? _outbox = outbox;
    private readonly IIdentityDb _db = db;

    /// <summary>HU #14711 — Impuestos recopia la llave del impuesto que cambió en Diagnóstico.</summary>
    public const string TipoCredencialDiagnostico = "impuestos.credencial_diagnostico.actualizada";

    /// <summary><c>&lt;producto&gt;.producto.cambiado</c>.</summary>
    public static string TipoCambio(string productCode) => $"{productCode}.producto.cambiado";

    /// <summary><c>&lt;producto&gt;.aprovisionamiento.reintentar</c>.</summary>
    public static string TipoReintento(string productCode) => $"{productCode}.aprovisionamiento.reintentar";

    public void EncolarCambio(TenantProduct actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        Outbox().EnqueueJob(
            TipoCambio(actual.ProductCode),
            Version,
            actual.TenantId,
            new ProductoCambiado(actual.TenantId, actual.ProductCode, actual.Enabled, actual.UpdatedAt));
    }

    /// <summary>
    /// HU #14711 — trabajo <see cref="TipoCredencialDiagnostico"/> v1 al exchange <c>flit.impuestos</c> (productor del host:
    /// <c>plataforma</c> o <c>tramites</c>), sin guardar: viaja en el guardado de las credenciales. Solo ids y la versión.
    /// </summary>
    public void EncolarCredencialDiagnosticoActualizada(Guid tenantId, long version, DateTimeOffset occurredAt) =>
        Outbox().EnqueueJob(
            TipoCredencialDiagnostico,
            Version,
            tenantId,
            new CredencialDiagnosticoActualizada(tenantId, "diagnostico", version, occurredAt));

    public async Task EncolarReintentoAsync(Guid tenantId, string productCode, Guid solicitadoPor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        Outbox().EnqueueJob(TipoReintento(productCode), Version, tenantId, new AprovisionamientoReintentar(tenantId, solicitadoPor));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private IPlatformOutbox Outbox() =>
        _outbox ?? throw new InvalidOperationException(
            "Este host no tiene outbox (Platform:Messaging): no se puede encolar el trabajo de aprovisionamiento del producto.");

    /// <summary><c>data</c> de <c>impuestos.producto.cambiado</c>.</summary>
    internal sealed record ProductoCambiado(Guid TenantId, string ProductCode, bool Enabled, DateTimeOffset ChangedAt);

    /// <summary><c>data</c> de <c>impuestos.credencial_diagnostico.actualizada</c> (HU #14711).</summary>
    internal sealed record CredencialDiagnosticoActualizada(Guid TenantId, string ProductCode, long Version, DateTimeOffset OccurredAt);

    /// <summary><c>data</c> de <c>impuestos.aprovisionamiento.reintentar</c>.</summary>
    internal sealed record AprovisionamientoReintentar(Guid TenantId, Guid SolicitadoPor);
}
