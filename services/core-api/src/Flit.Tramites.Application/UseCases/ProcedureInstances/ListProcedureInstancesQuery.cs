using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;

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
    string Estado,               // TramiteEstado (N 03): "borrador" | "preparado" | "entregado" | ...
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
    bool CanSubmit = false,                   // gates de radicación satisfechos (mismo cómputo que el wizard)
    bool Prioritario = false,                 // HU #10536 — marcado prioritario (ordenamiento con primacía)
                                              // HU #11020 — la parte SALIENTE del traspaso, para identificar el
                                              // trámite en el dashboard sin abrirlo. null en matrícula inicial.
    string? VendedorNombre = null,
    string? VendedorDocumento = null,
    bool SubsanacionActiva = false,           // flag de edición sobre rechazado
    int SubsanacionCount = 0,                 // veces que se activó la subsanación
    string? UltimoRechazoMotivo = null,      // reason (texto libre) del último rechazo OT
                                              // HU #11056 — columnas de seguimiento del listado. Todo se DERIVA del grafo que ya
                                              // carga ListWithSummaryGraphAsync; lo único que cuesta una consulta extra es el
                                              // nombre del gestor (resuelto en lote, nunca por fila).
    DateTimeOffset? UpdatedAt = null,         // última modificación; null si nunca se modificó tras crearse
    string? GestorNombre = null,              // persona que radica (created_by_user_id → DisplayName)
    string Fuente = TramiteFuente.Dashboard,  // dashboard | integracion | migrado (ver TramiteFuente)
                                              // Estado de firma de la compraventa POR PARTE. null en matrícula inicial (no hay
                                              // compraventa) y, en el vendedor, cuando la modalidad no lo contempla.
    string? FirmaVendedorEstado = null,
    string? FirmaCompradorEstado = null,
                                              // Expediente consolidado del wizard (adjunto tipo 'consolidado') ya generado. El
                                              // id viaja para que la fila lo previsualice sin consultar los adjuntos (HU #11055).
    Guid? ConsolidadoAttachmentId = null);

/// <summary>
/// Lista las instancias de un tenant (más recientes primero, cap del repo) y las mapea a
/// <see cref="InstanceSummaryDto"/> para la tabla de operación.
/// </summary>
public sealed class ListProcedureInstancesHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Tope de filas devueltas (la tabla muestra un resumen, no es un export paginado).</summary>
    public const int MaxItems = 200;

    private const string BuyerActorType = "comprador";
    private const string SellerActorType = "vendedor";

    /// <summary>
    /// Lista las instancias visibles para el caller. <paramref name="tenantId"/> <c>null</c> = TODAS
    /// las compañías (#1, el superadmin ve todo); un usuario de compañía siempre llega con su
    /// <paramref name="tenantId"/>, resuelto del JWT por el middleware.
    /// <para>
    /// HU #11056 — el nombre de compañía ya no depende de si el caller es superadmin: la columna
    /// "Gestor" lo necesita en los dos listados. El aislamiento por compañía lo sigue garantizando
    /// <paramref name="tenantId"/>, que es lo único que llega al <c>WHERE</c>.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<InstanceSummaryDto>> HandleAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        var instances = await repo.ListWithSummaryGraphAsync(tenantId, MaxItems, ct);

        // HU #11056 — la razón social se resuelve SIEMPRE, no solo para el superadmin: la columna
        // "Gestor" es «empresa – persona» también para un usuario de compañía. Para un solo tenant la
        // consulta trae una fila (los ids van distintos), así que el coste es el mismo de antes.
        IReadOnlyDictionary<Guid, string> nombres =
            await repo.GetTenantNamesAsync(instances.Select(i => i.TenantId).ToList(), ct) ?? EmptyNames;

        // Nombre de la persona que radica, en lote por los created_by_user_id del listado (sin N+1).
        IReadOnlyDictionary<Guid, string> gestores =
            await repo.GetUserDisplayNamesAsync(instances.Select(i => i.CreatedByUserId).ToList(), ct)
            ?? EmptyNames;

        // Identidad PER-PERSONA (documento) para los chips/progreso: se referencia la identidad vigente de
        // la persona en N trámites sin clonar (HU #10350). Una sola consulta para TODOS los tenants del
        // listado (WHERE tenant_id IN …) → sin N+1.
        var now = DateTimeOffset.UtcNow;
        IReadOnlySet<string> identidadKeys = await repo.ListVigenteApprovedIdentityKeysAsync(
            instances.Select(i => i.TenantId).Distinct().ToList(), now, ct) ?? new HashSet<string>();

        return instances
            .Select(e => ToSummary(
                e,
                IdentityApprovalResolver.ApprovedPartiesFromKeys(e, identidadKeys, now),
                nombres.GetValueOrDefault(e.TenantId),
                gestores.GetValueOrDefault(e.CreatedByUserId)))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<Guid, string> EmptyNames = new Dictionary<Guid, string>();

    internal static InstanceSummaryDto ToSummary(
        ProcedureInstance e,
        IReadOnlySet<string> identidadAprobadaPartes,
        string? companiaNombre = null,
        string? gestorNombre = null)
    {
        var fv = e.FieldValues.ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);
        var buyer = e.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, BuyerActorType, StringComparison.OrdinalIgnoreCase));
        // HU #11020 — el vendedor solo existe en traspaso; en matrícula queda null y la columna sale vacía.
        var seller = e.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, SellerActorType, StringComparison.OrdinalIgnoreCase));

        var modalidad = TramiteModalidadEntradaCodes.FromCode(e.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;
        var modalidadCode = TramiteModalidadEntradaCodes.ToCode(modalidad);

        // Estado server-driven del wizard: misma fuente de verdad que el frontend (canSubmit) y de
        // la que se deriva el progreso (PasoActual/TotalPasos). Se computa una sola vez.
        var state = GetWizardStateHandler.ComputeState(e, identidadAprobadaPartes);
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
            DeriveIdentityStatus(e, modalidad, identidadAprobadaPartes),
            DeriveSignaturePending(e, modalidad),
            state.CanSubmit,
            e.Prioritario,
            string.IsNullOrWhiteSpace(seller?.FullName) ? null : seller.FullName,
            string.IsNullOrWhiteSpace(seller?.DocumentNumber) ? null : seller.DocumentNumber,
            e.SubsanacionActiva,
            e.SubsanacionCount,
            DeriveUltimoRechazoMotivo(e),
            e.UpdatedAt,
            string.IsNullOrWhiteSpace(gestorNombre) ? null : gestorNombre.Trim(),
            TramiteFuente.Desde(e.Origin, e.IsMigrated),
            DeriveFirmaParte(e, modalidad, SellerActorType),
            DeriveFirmaParte(e, modalidad, BuyerActorType),
            DeriveConsolidadoAttachmentId(e));
    }

    /// <summary>
    /// HU #11056 — estado de la firma de la compraventa de UNA parte, para las dos columnas "Firmado"
    /// del listado. <c>null</c> en matrícula inicial: no hay compraventa que firmar, así que la columna
    /// se presenta como no aplicable en vez de como pendiente (que sería una alarma falsa).
    /// Sin fila de firma ⇒ <see cref="FirmaParteEstados.NoSolicitada"/>; con fila, su estado tal cual.
    /// Si hubiera más de una fila para la parte se toma la más reciente (la vigente).
    /// </summary>
    private static string? DeriveFirmaParte(
        ProcedureInstance e, TramiteModalidadEntrada modalidad, string parte)
    {
        if (modalidad != TramiteModalidadEntrada.Traspaso)
            return null;

        var firma = e.Signatures
            .Where(s => string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.SolicitadoAt)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefault();

        return firma?.Estado ?? FirmaParteEstados.NoSolicitada;
    }

    /// <summary>
    /// HU #11055 — adjunto del expediente consolidado del wizard (tipo <c>consolidado</c>) ya generado,
    /// o <c>null</c> si no existe. El id viaja en el resumen para que la fila lo previsualice con el
    /// endpoint de URL prefirmada sin consultar los adjuntos del trámite (una petición por fila).
    /// <para>
    /// Es el consolidado del GESTOR, no el <c>consolidado_maestro</c> del organismo de tránsito: son
    /// dos documentos distintos y el listado de operación muestra el primero.
    /// </para>
    /// </summary>
    private static Guid? DeriveConsolidadoAttachmentId(ProcedureInstance e) =>
        e.Attachments
            .Where(a => string.Equals(a.Tipo, ConsolidadoAttachmentTipo, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault()?.Id;

    private const string ConsolidadoAttachmentTipo = "consolidado";

    /// <summary>
    /// Motivo (texto libre) de la última transición REAL a <c>rechazado</c> (p. ej. entregado→rechazado
    /// del OT/Quipux). Se excluyen entradas auditoría <c>rechazado→rechazado</c> (activar subsanación),
    /// para no mostrar textos del operador como "Subsanación iniciada…".
    /// </summary>
    private static string? DeriveUltimoRechazoMotivo(ProcedureInstance e)
    {
        var latest = e.StatusHistory
            .Where(h => string.Equals(h.ToStatus, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(h.FromStatus, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(h.Reason))
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .FirstOrDefault();
        return latest?.Reason?.Trim();
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
    private static string? DeriveIdentityStatus(
        ProcedureInstance e, TramiteModalidadEntrada modalidad, IReadOnlySet<string> identidadAprobadaPartes)
    {
        var partes = modalidad == TramiteModalidadEntrada.Traspaso ? PartesTraspaso : PartesMatricula;

        // Aprobado PER-PERSONA: TODAS las partes requeridas tienen identidad vigente aprobada (referenciada,
        // aunque el trámite no tenga fila propia). Se evalúa ANTES de mirar filas locales, porque un trámite
        // que referencia una identidad de otro trámite no tiene validaciones propias.
        if (partes.All(identidadAprobadaPartes.Contains))
            return BiometricEstados.Aprobado;

        // Estados NO terminales/aprobados sí dependen de las validaciones PROPIAS del trámite (una captura en
        // curso o rechazada vive en su instancia): en_proceso / rechazado / sin iniciar.
        var esMatricula = modalidad != TramiteModalidadEntrada.Traspaso;
        bool Relevant(ProcedureInstanceBiometricValidation v) =>
            partes.Any(p => string.Equals(v.PartyRole, p, StringComparison.OrdinalIgnoreCase))
            || (esMatricula && v.PartyRole is null);

        var relevant = e.BiometricValidations.Where(Relevant).ToList();
        if (relevant.Count == 0)
            return null;

        if (relevant.Any(v => v.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso))
            return BiometricEstados.EnProceso;

        if (relevant.Any(v => v.Status is BiometricEstados.Rechazado or BiometricEstados.Expirado))
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

        if (!string.Equals(status, TramiteEstado.Borrador, StringComparison.OrdinalIgnoreCase))
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
