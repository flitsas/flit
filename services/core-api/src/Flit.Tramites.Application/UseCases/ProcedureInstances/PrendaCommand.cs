using System.Text.Json;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Contrato congelado de una decisión de prenda (compartido con el front).</summary>
public sealed record PrendaDto(
    Guid Id,
    string Decision,
    string Estado,
    string? AcreedorNombre,
    string? AcreedorDocumento,
    /// <summary>Entidad ante la que se levantó el gravamen; solo la usa el trámite de levantamiento.</summary>
    string? LevantamientoEntidad,
    DateTimeOffset CreatedAt);

/// <summary>Datos de una decisión de prenda a registrar.</summary>
public sealed record RegistrarPrendaInput(
    string Decision,
    string? AcreedorNombre = null,
    string? AcreedorDocumento = null,
    string? LevantamientoEntidad = null,
    string? MetadataJson = null);

/// <summary>
/// Registra una decisión de prenda VIGENTE para un trámite — comando base del cimiento IT-3 (HU-F2-01) que
/// también sirve la modificación post-registro versionada (R17, HU-F2-06). Versionado intrínseco: si ya existe
/// una fila vigente, se marca <c>reemplazada</c> ANTES de insertar la nueva (dos <c>SaveChanges</c>: garantiza
/// que el índice único parcial "una vigente por instancia" nunca vea dos filas vigentes a la vez) y se registra
/// un evento de auditoría <c>prenda_modificada</c> (old/new). Único gate de estado: el trámite en estado FINAL
/// (aprobado/anulado) es inmutable. La prenda vive en su propia tabla, así que modificar fuera de borrador NO
/// viola la inmutabilidad de <c>field_values</c>: por eso la modificación post-registro es posible y auditada.
/// </summary>
public sealed class RegistrarPrendaHandler(
    IProcedureInstanceRepository instances,
    IProcedureInstancePrendaRepository prendas,
    IPrendaDocumentRequirementPolicy? prendaDocumentRequirementPolicy = null,
    IAttachmentStorage? storage = null,
    IVehicleSignatureImprintRepository? imprintAudit = null,
    IMaestroRadicadoLookup? maestroRadicado = null)
{
    /// <summary>Error: el OT exige el certificado de prenda, así que "omitir" no es elegible (salvo la familia Matrículas, Feature #13110).</summary>
    public const string OmitirNoAdmitidoError = "prenda_omitir_no_admitido";

    /// <summary>Error: este tipo no tiene dimensión de gravamen (familia OTROS, tipo no prendario).</summary>
    public const string PrendaNoAdmitidaError = "prenda_no_admitida_en_tipo";

    /// <summary>
    /// ADR-0055 (HU #12129, AC3) — Matrícula/Traspaso (y cualquier tipo que no admite la acción
    /// complementaria de prenda) siguen siendo de UNA sola decisión vigente por instancia
    /// (ADR-0050): regla de negocio EXPLÍCITA, no solo el índice único parcial de BD (que desde
    /// HU #12128 sí permite hasta dos vigentes por instancia). Se devuelve cuando la instancia ya
    /// tiene más de una fila vigente y el tipo no admite la complementaria — estado que este mismo
    /// handler nunca produce por su cuenta, pero que se rechaza en vez de adivinar cuál reemplazar.
    /// </summary>
    public const string SegundaDecisionVigenteNoAdmitidaError = "prenda_segunda_decision_no_admitida";

    private readonly IPrendaDocumentRequirementPolicy _documentPolicy =
        prendaDocumentRequirementPolicy ?? NullPrendaDocumentRequirementPolicy.Instance;

    private readonly IAttachmentStorage? _storage = storage;
    private readonly IVehicleSignatureImprintRepository? _imprintAudit = imprintAudit;
    private readonly IMaestroRadicadoLookup _maestroRadicado = maestroRadicado ?? NullMaestroRadicadoLookup.Instance;

    public async Task<(PrendaDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        RegistrarPrendaInput input,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        if (input is null || !PrendaDecision.IsValid(input.Decision))
            return (null, "prenda_decision_invalida");

        var instance = await instances.GetByIdAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // R17 (HU #10599) — un trámite en estado final (aprobado/anulado) es inmutable: no admite
        // modificar la elección de prenda.
        if (TramiteEstado.EsFinal(instance.Status))
            return (null, TramiteEstadoErrores.EstadoFinal);

        // ADR-0050 — en la familia OTROS la prenda no es una capa que se añada: o el tipo ES el
        // trámite de gravamen (inscribir, levantar, cambiar de acreedor) o el expediente no tiene
        // dimensión de prenda en absoluto. Un duplicado de tarjeta con un gravamen encima son dos
        // trámites, y el organismo devuelve el FUR que los mezcla. Matrícula y traspaso conservan la
        // prenda complementaria del art. 5.1.8 y no pasan por aquí.
        var perfil = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        if (!perfil.AdmiteDimensionDePrenda(instance.ProcedureType?.Family, instance.ProcedureType?.Code))
        {
            return (null, PrendaNoAdmitidaError);
        }

        var decision = input.Decision.Trim().ToLowerInvariant();

        // CF-06 (HU #10881) — con un OT que exige el certificado de prenda, "omitir" no es elegible en
        // Traspaso ni en el resto de familias: la regla es del organismo. Se rechaza AL ELEGIR, que es
        // donde el gate de radicación decía que había que decidirlo (ver PrendaGate.EvaluateOtOverride):
        // bloquear después dejaría guardada una decisión que ningún adjunto puede satisfacer. Las
        // decisiones ya guardadas no se revisan: la regla mira la elección nueva.
        //
        // Feature #13110 — excepción de la familia Matrículas: ahí "Omitir prenda" siempre se admite (la
        // prenda es un trámite propio, art. 5.3.13.1). La regla vive en PrendaDecision.OmitirAdmitido,
        // la misma que publica el estado del wizard (PrendaOmitAllowed). La familia se lee null-safe
        // (instance.Family lanza sin la navegación cargada); sin tipo cargado se trata como "no es
        // matrícula" y se conserva el rechazo (postura conservadora).
        if (string.Equals(decision, PrendaDecision.Omitir, StringComparison.OrdinalIgnoreCase))
        {
            var family = ProcedureFamilyCodes.FromCodeOrOtros(instance.ProcedureType?.Family);
            var otExigeDocumento = await _documentPolicy
                .IsRequiredAsync(tenantId, instance.TransitOfficeId, instance.CreatedAt, ct)
                .ConfigureAwait(false);
            if (!PrendaDecision.OmitirAdmitido(family, otExigeDocumento))
                return (null, OmitirNoAdmitidoError);
        }

        var now = DateTimeOffset.UtcNow;

        // ADR-0055 (HU #12129) — familia de la decisión entrante y si el TIPO admite que coexista con
        // una vigente de la familia contraria (PRENDA_INSCRIPCION/LEVANTAMIENTO_PRENDA únicamente).
        var accionFamilia = PrendaDecision.AccionFamiliaFor(decision);
        var permiteComplementaria = ProcedureTypeLayers.PermiteAccionComplementaria(instance.ProcedureType?.Code);

        // Defensivo: dobles de prueba (Substitute/Fake) sin configurar este método no deben tumbar el
        // handler con NRE — se tratan como "sin vigentes", igual que devolvía GetVigenteAsync = null.
        var vigentes = await prendas.GetVigentesAsync(instanceId, tenantId, ct) ?? [];

        IReadOnlyList<ProcedureInstancePrenda> aReemplazar;
        if (permiteComplementaria)
        {
            // Versionado RE-SCOPEADO POR FAMILIA (AC2): solo se reemplaza la vigente de la MISMA
            // familia que la decisión entrante. La vigente de la familia contraria, si existe, no se
            // toca (AC1) — así conviven constitución y levantamiento, cada una con su propio
            // acreedor/documento.
            aReemplazar = vigentes.Where(v => v.AccionFamilia == accionFamilia).ToList();
        }
        else
        {
            // Matrícula/Traspaso (AC3): decisión mutuamente excluyente, a lo sumo UNA vigente por
            // instancia SIN IMPORTAR la familia (comportamiento histórico, R4/R10/R17 intacto) — por
            // eso se reemplaza la vigente aunque cambie de familia (p. ej. "solicitar" → "levantar").
            // Regla de negocio explícita, no solo el índice de BD: si ya hubiera más de una vigente
            // para un tipo que no admite la complementaria, se rechaza en vez de reemplazar a ciegas.
            if (vigentes.Count > 1)
                return (null, SegundaDecisionVigenteNoAdmitidaError);

            aReemplazar = vigentes;
        }

        // Se persiste cada reemplazo PRIMERO (libera el índice único parcial) y luego se inserta la
        // nueva vigente. Cada reemplazo audita su propio evento old/new.
        foreach (var vigente in aReemplazar)
        {
            var anterior = vigente.Decision;
            vigente.Estado = PrendaEstado.Reemplazada;
            vigente.UpdatedAt = now;
            vigente.UpdatedBy = userId;
            await prendas.SaveChangesAsync(ct);

            await instances.AddEventAsync(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = instanceId,
                Tipo = "prenda_modificada",
                Payload = JsonSerializer.Serialize(new { anterior, nueva = decision }),
                CreatedAt = now,
                CreatedBy = userId,
            }, ct);
        }

        // Feature #13110 (CF-6) — con "omitir" no se solicita trámite de prenda: el acreedor y la entidad
        // de levantamiento no alimentan ningún documento y no se guardan aunque el cliente los envíe.
        var conservaAcreedor = PrendaDecision.ConservaDatosDeAcreedor(decision);
        var nueva = new ProcedureInstancePrenda
        {
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Decision = decision,
            Estado = PrendaEstado.Vigente,
            AcreedorNombre = conservaAcreedor ? Trimmed(input.AcreedorNombre) : null,
            AcreedorDocumento = conservaAcreedor ? Trimmed(input.AcreedorDocumento) : null,
            LevantamientoEntidad = conservaAcreedor ? Trimmed(input.LevantamientoEntidad) : null,
            AccionFamilia = accionFamilia,
            Metadata = string.IsNullOrWhiteSpace(input.MetadataJson) ? "{}" : input.MetadataJson,
            CreatedAt = now,
            CreatedBy = userId,
        };
        await prendas.AddAsync(nueva, ct);
        await prendas.SaveChangesAsync(ct);

        // Bug #13240 — el soporte de la decisión anterior no sobrevive al cambio de decisión.
        var vigentesResultantes = vigentes.Except(aReemplazar).Append(nueva).ToList();
        await RetirarSoportesHuerfanosAsync(instanceId, tenantId, vigentesResultantes, ct).ConfigureAwait(false);

        return (ToDto(nueva), null);
    }

    /// <summary>
    /// Bug #13240 — retira los adjuntos de prenda (<see cref="PrendaDocTipos.All"/>) que ya no exige
    /// NINGUNA decisión vigente: el PDF de «registrar» tras cambiar a <c>omitir</c>/<c>sin_prenda</c> o a
    /// <c>levantar</c> seguía en los documentos del trámite y dentro del consolidado, mientras el FUR —que
    /// lee las vigentes— ya no lo declaraba. Con la acción complementaria (dos vigentes, una por familia)
    /// se conserva el soporte de la otra familia porque sigue exigido.
    ///
    /// <para><b>Mismas reglas que el borrado del gestor</b> (<see cref="DeleteAttachmentHandler"/>, vía
    /// <see cref="AttachmentRetiro"/>): solo en estado editable
    /// (<see cref="TramiteEstado.PermiteEdicionDatos"/>), nunca tipos del sistema ni adjuntos
    /// referenciados por una radicación, auditoría de impronta en soft-delete y checklist des-marcado.
    /// Fuera de estado editable la decisión se guarda (R17, modificación post-registro) pero los
    /// documentos no se tocan: el expediente ya salió con ellos y el gestor tampoco podría borrarlos a
    /// mano. El consolidado se invalida solo (<c>ConsolidadoVigenciaTracker</c> ve el adjunto borrado).</para>
    ///
    /// <para><c>inscripcion_prenda</c> NO entra: es requisito del catálogo del TIPO
    /// (LEVANTAR_INSCRIBIR_PRENDA, CAMBIO_ACREEDOR) y el documento de la política del OT, no el soporte
    /// de una decisión del agregado; ninguna decisión lo exige ni lo deja de exigir.</para>
    ///
    /// <para>Sin almacenamiento cableado (dobles de prueba / composiciones antiguas) no se hace nada:
    /// retirar la fila sin borrar el blob dejaría huérfano el archivo.</para>
    /// </summary>
    private async Task RetirarSoportesHuerfanosAsync(
        Guid instanceId,
        Guid tenantId,
        IReadOnlyList<ProcedureInstancePrenda> vigentes,
        CancellationToken ct)
    {
        if (_storage is null)
            return;

        var instance = await instances.GetByIdWithAttachmentsAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null || !TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return;

        var exigidos = vigentes
            .Select(v => PrendaDecision.DocTipoFor(v.Decision))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var huerfanos = instance.Attachments
            .Where(a => PrendaDocTipos.All.Contains(a.Tipo) && !exigidos.Contains(a.Tipo))
            .ToList();
        if (huerfanos.Count == 0)
            return;

        var retirables = await AttachmentRetiro
            .FiltrarRetirablesAsync(huerfanos, _maestroRadicado, tenantId, instanceId, ct)
            .ConfigureAwait(false);
        if (retirables.Count == 0)
            return;

        AttachmentRetiro.Retirar(instance, retirables, instances, _storage, _imprintAudit);
        await instances.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Contrato de salida del PUT y del GET de prenda. Feature #13110 (Habeas Data, Ley 1581) — con
    /// <c>omitir</c> el acreedor y la entidad de levantamiento se devuelven <c>null</c> aunque la fila
    /// los tenga: cubre las filas <c>omitir</c> guardadas antes de la limpieza en escritura.
    /// </summary>
    internal static PrendaDto ToDto(ProcedureInstancePrenda p)
    {
        var conserva = PrendaDecision.ConservaDatosDeAcreedor(p.Decision);
        return new(
            p.Id,
            p.Decision,
            p.Estado,
            conserva ? p.AcreedorNombre : null,
            conserva ? p.AcreedorDocumento : null,
            conserva ? p.LevantamientoEntidad : null,
            p.CreatedAt);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Lee las decisiones de prenda VIGENTES de un trámite.
///
/// <para><b>Cambio de contrato de lectura (ADR-0055, HU #12129, AC4).</b> Antes de esta HU devolvía
/// <c>PrendaDto?</c> (a lo sumo una decisión), porque el modelo solo admitía una fila vigente por
/// instancia. Desde HU #12128, PRENDA_INSCRIPCION/LEVANTAMIENTO_PRENDA pueden tener constitución y
/// levantamiento vigentes a la vez, así que el resultado pasa a ser una colección de 0 a 2
/// <see cref="PrendaDto"/> (Matrícula/Traspaso siguen devolviendo, en la práctica, 0 o 1 elemento).
/// Es un cambio BINARIO del shape de <c>GET /instances/{id}/prenda</c> (objeto nullable → array),
/// documentado en <c>contracts/openapi/core-api.v1.yaml</c>; el consumidor actual del frontend
/// (<c>tramites-client.ts</c>) debe adaptarse en HU-FE-1 (#12130).</para>
/// </summary>
public sealed class GetPrendaVigenteHandler(IProcedureInstancePrendaRepository prendas)
{
    public async Task<IReadOnlyList<PrendaDto>> HandleAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default)
    {
        var vigentes = await prendas.GetVigentesAsync(instanceId, tenantId, ct) ?? [];
        return vigentes.Select(RegistrarPrendaHandler.ToDto).ToList();
    }
}
