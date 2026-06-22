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
    int PasoActual,              // 1..TotalPasos
    int TotalPasos,              // 5 matrícula | 6 traspaso
    DateTimeOffset CreatedAt);

/// <summary>
/// Lista las instancias de un tenant (más recientes primero, cap del repo) y las mapea a
/// <see cref="InstanceSummaryDto"/> para la tabla de operación.
/// </summary>
public sealed class ListProcedureInstancesHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Tope de filas devueltas (la tabla muestra un resumen, no es un export paginado).</summary>
    public const int MaxItems = 200;

    private const string BuyerActorType = "comprador";

    public async Task<IReadOnlyList<InstanceSummaryDto>> HandleAsync(Guid tenantId, CancellationToken ct = default)
    {
        var instances = await repo.ListByTenantWithSummaryGraphAsync(tenantId, MaxItems, ct);
        return instances.Select(ToSummary).ToList();
    }

    internal static InstanceSummaryDto ToSummary(ProcedureInstance e)
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
            pasoActual,
            totalPasos,
            e.CreatedAt);
    }

    /// <summary>
    /// Computa (PasoActual, TotalPasos) reusando el estado server-driven del wizard
    /// (<see cref="GetWizardStateHandler.ComputeState"/>) — misma fuente de verdad que los gates
    /// (VIN consultado, documentos obligatorios, comprador presente, biométrica aprobada, FUR generado).
    /// <para>Heurística: <c>PasoActual</c> = número de pasos en estado <c>complete</c>, acotado a
    /// [1, TotalPasos]. Para instancias ya radicadas (Submitted o posterior) se reporta
    /// <c>PasoActual = TotalPasos</c> (todos los pasos quedaron resueltos al radicar).</para>
    /// </summary>
    private static (int PasoActual, int TotalPasos) ComputeProgress(ProcedureInstance e)
    {
        var state = GetWizardStateHandler.ComputeState(e);
        var total = state.TotalSteps;

        if (!string.Equals(e.Status, ProcedureInstanceStatus.Draft, StringComparison.OrdinalIgnoreCase))
            return (total, total);

        var completos = state.Steps.Count(s => string.Equals(s.Status, "complete", StringComparison.Ordinal));
        var paso = Math.Clamp(completos, 1, total);
        return (paso, total);
    }

    private static string? Field(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}
