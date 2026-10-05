namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// HU #13296 (Feature #13282, Épica #13202) — lectura de SOLO LECTURA, cross-tenant, de las validaciones de
/// identidad manuales para el Super Admin. Puerto aparte de <see cref="IProcedureInstanceRepository"/>: es la única
/// lectura que cruza TODAS las compañías a propósito, y mantenerla en su propio sitio deja un único lugar que
/// revisar si alguna vez se sospecha una fuga. Quien la llama es responsable de haber verificado que el caller es
/// Super Admin (el repositorio no conoce al usuario).
/// </summary>
public interface IManualIdentityReviewReadRepository
{
    /// <summary>
    /// Página de validaciones con proveedor <c>manual</c> de todas las compañías, ya filtradas y ordenadas
    /// (pendientes de revisión primero, las más antiguas arriba).
    /// </summary>
    Task<(IReadOnlyList<ManualIdentityReviewRow> Items, int Total)> ListAsync(
        ManualIdentityReviewFilter filter,
        int skip,
        int take,
        CancellationToken ct = default);
}

/// <summary>Filtros ya saneados del listado manual (todos opcionales).</summary>
public sealed record ManualIdentityReviewFilter(string? Status, string? Origin, string? Text);

/// <summary>Fila cruda del listado manual; el origen ya viene calculado.</summary>
public sealed record ManualIdentityReviewRow(
    Guid Id,
    string FullName,
    string DocumentNumber,
    string TenantName,
    string Origin,
    string Status,
    DateTimeOffset? ActivatedAt);

/// <summary>
/// Orígenes de una validación manual (contrato Épica #13202 §3). Hoy el modelo solo distingue
/// <see cref="Tramite"/> (hay <c>procedure_instance_id</c>), <see cref="Mandatario"/> (party_role = mandatario con
/// ficha) y <see cref="Prevalidacion"/> (standalone ligada a persona). <see cref="RepresentanteLegal"/> está en el
/// contrato pero ninguna fila lo identifica todavía: no se infiere.
/// </summary>
public static class ManualIdentityReviewOrigins
{
    public const string Tramite = "tramite";
    public const string Prevalidacion = "prevalidacion";
    public const string Mandatario = "mandatario";
    public const string RepresentanteLegal = "representante_legal";

    public static readonly IReadOnlyList<string> Todos = [Tramite, Prevalidacion, Mandatario, RepresentanteLegal];
}
