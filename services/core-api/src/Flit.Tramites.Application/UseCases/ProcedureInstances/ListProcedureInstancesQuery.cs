using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Resumen por instancia para la tabla de operación (Slice M6). Los nombres de campo son contrato
/// con el frontend: NO renombrar sin coordinar. Campos derivados de field_values, actores y el grafo
/// del wizard; <c>PasoActual</c>/<c>TotalPasos</c> reusan los gates del wizard server-driven.
/// </summary>
public sealed record InstanceSummaryDto(
    Guid Id,
    string ReferenceNumber,
    string Modalidad,            // "matricula_inicial" | "traspaso"
    string Estado,               // ProcedureInstanceStatus: "draft" | "submitted" | ...
    string? Placa,
    string? Vin,
    string? VehiculoMarca,
    string? VehiculoLinea,
    string? CompradorNombre,
    string? CompradorDocumento,
    string? OrganismoTransito,   // nombre del OT elegido (field_value transit_office_name)
    int PasoActual,              // 1..TotalPasos
    int TotalPasos,              // 5 matrícula | 6 traspaso
    DateTimeOffset CreatedAt,
    Guid TenantId,               // compañía dueña (para el scoping/columna del superadmin, #1)
    string? CompaniaNombre);     // razón social; solo se resuelve en el listado multi-tenant (superadmin)

/// <summary>
/// Lista las instancias de un tenant (más recientes primero, cap del repo) y las mapea a
/// <see cref="InstanceSummaryDto"/> para la tabla de operación.
/// </summary>
public sealed class ListProcedureInstancesHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Tope de filas devueltas (la tabla muestra un resumen, no es un export paginado).</summary>
    public const int MaxItems = 200;

    private const string BuyerActorType = "comprador";

    /// <summary>
    /// Lista las instancias visibles para el caller. <paramref name="tenantId"/> <c>null</c> +
    /// <paramref name="isSuperAdmin"/> = TODAS las compañías (#1, el superadmin ve todo); en ese caso
    /// se resuelve el nombre de compañía por fila. Un usuario de compañía siempre llega con su
    /// <paramref name="tenantId"/> (resuelto del JWT por el middleware) y sin nombre de compañía.
    /// </summary>
    public async Task<IReadOnlyList<InstanceSummaryDto>> HandleAsync(
        Guid? tenantId, bool isSuperAdmin, CancellationToken ct = default)
    {
        var instances = await repo.ListWithSummaryGraphAsync(tenantId, MaxItems, ct);

        // Solo el listado multi-tenant del superadmin necesita el nombre de compañía por fila.
        IReadOnlyDictionary<Guid, string> nombres = isSuperAdmin
            ? await repo.GetTenantNamesAsync(instances.Select(i => i.TenantId).ToList(), ct)
            : EmptyNames;

        return instances
            .Select(e => ToSummary(e, nombres.GetValueOrDefault(e.TenantId)))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<Guid, string> EmptyNames = new Dictionary<Guid, string>();

    internal static InstanceSummaryDto ToSummary(ProcedureInstance e, string? companiaNombre = null)
    {
        var fv = e.FieldValues.ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);
        var buyer = e.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, BuyerActorType, StringComparison.OrdinalIgnoreCase));

        var modalidad = TramiteModalidadEntradaCodes.FromCode(e.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;
        var modalidadCode = TramiteModalidadEntradaCodes.ToCode(modalidad);

        var (pasoActual, totalPasos) = ComputeProgress(e);

        return new InstanceSummaryDto(
            e.Id,
            e.ReferenceNumber,
            modalidadCode,
            e.Status,
            Field(fv, "plate"),
            Field(fv, "vin"),
            Field(fv, "vehicle_brand"),
            Field(fv, "vehicle_line"),
            string.IsNullOrWhiteSpace(buyer?.FullName) ? null : buyer.FullName,
            string.IsNullOrWhiteSpace(buyer?.DocumentNumber) ? null : buyer.DocumentNumber,
            Field(fv, "transit_office_name"),
            pasoActual,
            totalPasos,
            e.CreatedAt,
            e.TenantId,
            string.IsNullOrWhiteSpace(companiaNombre) ? null : companiaNombre);
    }

    /// <summary>
    /// Computa (PasoActual, TotalPasos) reusando el estado server-driven del wizard
    /// (<see cref="GetWizardStateHandler.ComputeState"/>) — misma fuente de verdad que los gates
    /// (VIN consultado, documentos obligatorios, comprador presente, biométrica aprobada, FUR generado).
    /// <para><c>PasoActual</c> = paso ACTIVO (1-based) = la "frontera" del flujo: el primer paso aún
    /// no <c>complete</c>. Es donde el wizard reanuda (Track B), de modo que la columna "Paso" del
    /// listado coincide con el paso en que se abre el trámite. Si todos los pasos están completos, o
    /// la instancia ya está radicada (Submitted o posterior), se reporta <c>PasoActual = TotalPasos</c>.</para>
    /// </summary>
    private static (int PasoActual, int TotalPasos) ComputeProgress(ProcedureInstance e)
    {
        var state = GetWizardStateHandler.ComputeState(e);
        var total = state.TotalSteps;

        if (!string.Equals(e.Status, ProcedureInstanceStatus.Draft, StringComparison.OrdinalIgnoreCase))
            return (total, total);

        // Frontera = primer paso no completo (mismo criterio que frontierIndex del frontend).
        // PasoActual 1-based = frontera + 1; si no hay incompletos, el último paso.
        var frontier = state.Steps
            .ToList()
            .FindIndex(s => !string.Equals(s.Status, "complete", StringComparison.Ordinal));
        var paso = frontier < 0 ? total : Math.Min(frontier + 1, total);
        return (paso, total);
    }

    private static string? Field(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}
