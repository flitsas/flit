using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Escritura ACTIVA y VIGENTE resuelta para un actor del trámite, lista para adjuntar al expediente
/// (HU #10926, ADR-0033). <c>Content</c> son los bytes del PDF de la escritura (admin.company_deeds).
/// <c>Nit</c> es PII (Ley 1581): no loguear. <c>DeedId</c> es la referencia (admin.company_deeds.id) de
/// la escritura que entró al registro; se persiste en el adjunto (<c>source_deed_id</c>) para trazar
/// exactamente cuál se usó (HU #10936).
/// </summary>
public sealed record ResolvedDeedDocument(string Tipo, string Filename, byte[] Content, string Nit, string Rol, Guid DeedId);

/// <summary>
/// Resuelve las escrituras ACTIVAS y VIGENTES de las compañías (NIT) de los actores de un trámite
/// (directorio del tenant, #10899), con sus BYTES, para inyectarlas como adjunto del sistema y que se
/// fusionen en el PDF consolidado. Puerto en Trámites; la implementación (Infrastructure) cruza el
/// directorio de escrituras (Admin) y el almacenamiento de adjuntos SIN acoplar este módulo a Admin.
/// </summary>
public interface IProcedureDeedResolver
{
    /// <summary>
    /// Devuelve, por cada actor persona jurídica (NIT) con escritura vigente en el tenant, su PDF con
    /// tipo por rol ('escritura' para vendedor/propietario, 'escritura_comprador' para comprador — D2),
    /// la MÁS PRÓXIMA A VENCER por compañía (menor VigenciaHasta entre las vigentes, HU #10936). Vacío
    /// si no hay actores NIT o ninguna escritura vigente.
    /// </summary>
    Task<IReadOnlyList<ResolvedDeedDocument>> ResolveForActorsAsync(
        Guid tenantId,
        IEnumerable<ProcedureInstanceActor> actors,
        CancellationToken ct = default);
}

/// <summary>
/// Implementación nula (no resuelve nada). Default seguro para tests/DI que no ejercitan las
/// escrituras, espejo de <c>NullSignatureVaultPolicy</c>.
/// </summary>
public sealed class NullProcedureDeedResolver : IProcedureDeedResolver
{
    public static readonly NullProcedureDeedResolver Instance = new();

    private NullProcedureDeedResolver() { }

    public Task<IReadOnlyList<ResolvedDeedDocument>> ResolveForActorsAsync(
        Guid tenantId,
        IEnumerable<ProcedureInstanceActor> actors,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ResolvedDeedDocument>>([]);
}
