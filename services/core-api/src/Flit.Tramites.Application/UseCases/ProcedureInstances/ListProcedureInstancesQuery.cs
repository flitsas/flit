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
    Guid TenantId,                            // compañía dueña (para el scoping/columna del superadmin, #1)
    string? CompaniaNombre,                   // razón social; solo se resuelve en el listado multi-tenant (superadmin)
    // HU #10350 — desacople de la validación de identidad async. Estos campos alimentan los chips
    // del listado ("Pendiente validación" / "Pendiente firma" / "Radicar") y la acción de la fila sin
    // que el frontend recalcule gates. Opcionales (default) para compat con consumidores existentes.
    DateTimeOffset? DraftFinalizedAt = null,
    string? IdentityValidationStatus = null,  // aprobado | en_proceso | rechazado | null (sin iniciar)
    bool SignaturePending = false,            // traspaso: compraventa de alguna parte sin firmar
    bool CanSubmit = false);                  // gates de radicación satisfechos (mismo cómputo que el wizard)

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

        // Estado server-driven del wizard: misma fuente de verdad que el frontend (canSubmit) y de
        // la que se deriva el progreso (PasoActual/TotalPasos). Se computa una sola vez.
        var state = GetWizardStateHandler.ComputeState(e);
        var (pasoActual, totalPasos) = ComputeProgress(state, e.Status);

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
            string.IsNullOrWhiteSpace(companiaNombre) ? null : companiaNombre,
            e.DraftFinalizedAt,
            DeriveIdentityStatus(e, modalidad),
            DeriveSignaturePending(e, modalidad),
            state.CanSubmit);
    }

    /// <summary>Partes que llevan validación de identidad por modalidad (matrícula = solo comprador).</summary>
    private static readonly string[] PartesTraspaso = ["comprador", "vendedor"];
    private static readonly string[] PartesMatricula = ["comprador"];

    /// <summary>
    /// Estado agregado de la validación de identidad para los chips del listado (HU #10350): <c>aprobado</c>
    /// si TODAS las partes requeridas tienen una validación aprobada; <c>en_proceso</c> si alguna relevante
    /// está enviada/en proceso; <c>rechazado</c> si alguna relevante quedó rechazada/expirada; <c>null</c> si
    /// no hay ninguna validación iniciada. En matrícula la única parte (comprador) puede venir con
    /// <c>Parte</c> null o "comprador".
    /// </summary>
    private static string? DeriveIdentityStatus(ProcedureInstance e, TramiteModalidadEntrada modalidad)
    {
        var partes = modalidad == TramiteModalidadEntrada.Traspaso ? PartesTraspaso : PartesMatricula;
        var esMatricula = modalidad != TramiteModalidadEntrada.Traspaso;

        bool Relevant(ProcedureInstanceBiometricValidation v) =>
            partes.Any(p => string.Equals(v.Parte, p, StringComparison.OrdinalIgnoreCase))
            || (esMatricula && v.Parte is null);

        var relevant = e.BiometricValidations.Where(Relevant).ToList();
        if (relevant.Count == 0)
            return null;

        // Aprobado para el chip = aprobado Y VIGENTE (≤30 días): una aprobación vencida deja de contar.
        var now = DateTimeOffset.UtcNow;
        bool Aprobada(string parte) => e.BiometricValidations.Any(v =>
            (string.Equals(v.Parte, parte, StringComparison.OrdinalIgnoreCase) || (esMatricula && v.Parte is null))
            && BiometricRules.EsAprobadaVigente(v, now));

        if (partes.All(Aprobada))
            return BiometricEstados.Aprobado;

        if (relevant.Any(v => v.Estado is BiometricEstados.Enviado or BiometricEstados.EnProceso))
            return BiometricEstados.EnProceso;

        if (relevant.Any(v => v.Estado is BiometricEstados.Rechazado or BiometricEstados.Expirado))
            return BiometricEstados.Rechazado;

        return null;
    }

    /// <summary>
    /// Firma de la compraventa pendiente (solo traspaso): alguna de las dos partes aún no tiene su firma
    /// <c>firmada</c>. En matrícula no aplica firma de compraventa → siempre false.
    /// </summary>
    private static bool DeriveSignaturePending(ProcedureInstance e, TramiteModalidadEntrada modalidad)
    {
        if (modalidad != TramiteModalidadEntrada.Traspaso)
            return false;

        bool Firmada(string parte) => e.Signatures.Any(s =>
            string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase)
            && s.Estado == SignatureEstados.Firmada);

        return !(Firmada("comprador") && Firmada("vendedor"));
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
    private static (int PasoActual, int TotalPasos) ComputeProgress(WizardStateDto state, string status)
    {
        var total = state.TotalSteps;

        if (!string.Equals(status, ProcedureInstanceStatus.Draft, StringComparison.OrdinalIgnoreCase))
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
