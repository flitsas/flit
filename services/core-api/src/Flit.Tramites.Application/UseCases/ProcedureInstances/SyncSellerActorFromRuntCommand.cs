using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0051 Decisión 5 — sincroniza el actor "vendedor" (propietario) desde el RUNT cuando el tipo de
/// trámite exige parte vendedora pero NO la captura por formulario (<c>gate_profile.requiresSeller
/// &amp;&amp; !sellerCapturedViaForm</c>, hoy solo <c>TRASPASO_UNILATERAL</c>: el locatario formaliza el
/// traspaso a su nombre y el propietario nunca pasa por el wizard).
///
/// <para>Sin esta pieza NADA crea esa fila: <c>FinalizeDraftGate</c> bloquearía el 100% de esos
/// borradores con <c>actores_incompletos</c> y <c>FurCommand.AddParte(..., "vendedor")</c> no
/// encontraría datos que estampar.</para>
///
/// <para><b>Fuente del dato.</b> <c>owner_document_type</c>/<c>owner_document_number</c> ya los tecleó
/// el gestor en el paso 1 (<see cref="CreateProcedureInstanceFromConsultaHandler"/>); esta pieza los
/// reutiliza para disparar el MISMO lookup best-effort que <see cref="RuntPersonLookupHandler"/> ya usa
/// para autopoblar al comprador en matrícula (proveedor <c>kyverum_runt_conductor</c>), pero PERSISTE
/// el resultado como fila de <c>instance.Actors</c> en vez de devolverlo para que un formulario lo
/// confirme — aquí no hay formulario que lo haga por ella.</para>
///
/// <para><b>Best-effort real (degradación, nunca error).</b> Si el lookup falla, no resuelve nombre, o
/// el documento es de una persona JURÍDICA (el RUNT-conductor solo cubre personas naturales:
/// <see cref="RuntPersonLookupHandler"/> devuelve <c>unsupported_document_type</c> para NIT), la fila se
/// crea igual con lo que sí se conoce (documento) y <c>FullName</c> en blanco. El criterio de
/// completitud de una parte SINCRONIZADA sin nombre (¿basta el documento? ¿degrada a "NO REGISTRA" en
/// el FUR?) es Fase 3 (<c>FinalizeDraftProcedureInstanceCommand</c>, fuera de este alcance) — hoy esa
/// combinación queda con el mismo <c>actores_incompletos</c> que ya bloquea cualquier borrador sin ese
/// dato, sin formulario con el que el gestor lo corrija manualmente (riesgo documentado en el ADR,
/// Decisión 5/6: el revelado del formulario oculto para ese caso es Fase 3).</para>
///
/// <para><b>Habeas Data (nota para el Security Agent, sin resolver aquí).</b> Esta pieza persiste datos
/// personales de un tercero (el propietario) a partir de una consulta, sin que esa persona haya
/// diligenciado un formulario en ESTE trámite. El ADR-0051 deja pendiente confirmar si el consentimiento
/// ya obtenido en el contrato de leasing (fuera del sistema) cubre esta persistencia, o si hace falta
/// una nota de origen del dato (p. ej. <c>Source="runt_sync"</c>) para trazabilidad. Por eso este
/// handler NUNCA loguea el nombre, documento ni correo del vendedor — solo el id del trámite.</para>
/// </summary>
public sealed class SyncSellerActorFromRuntHandler(
    IProcedureInstanceRepository repo,
    ICatalogRepository catalogRepo,
    RuntPersonLookupHandler personLookup,
    ILogger<SyncSellerActorFromRuntHandler>? logger = null)
{
    /// <summary><c>actor_type</c> que se persiste (mismo vocabulario que ActorsCommand/FurCommand).</summary>
    private const string VendedorActorType = "vendedor";

    /// <summary>Código de catálogo (procedure_entities) del propietario — mismo mapeo que ActorsCommand.RolToEntityCode.</summary>
    private const string VendedorEntityCode = "OWNER";

    private readonly ILogger _logger = logger ?? NullLogger<SyncSellerActorFromRuntHandler>.Instance;

    /// <summary>
    /// Sincroniza el vendedor si aún no existe. Idempotente y sin efecto si falta el documento, si ya
    /// hay una fila "vendedor", o si la entidad de catálogo no está seedeada — en ningún caso rompe la
    /// creación del trámite (best-effort, mismo criterio que el resto del preflight/consulta del paso 1).
    /// </summary>
    public async Task SyncAsync(
        Guid instanceId,
        Guid tenantId,
        string? ownerDocumentType,
        string? ownerDocumentNumber,
        CancellationToken ct = default)
    {
        var documentType = ownerDocumentType?.Trim();
        var documentNumber = ownerDocumentNumber?.Trim();
        if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(documentNumber))
            return; // Sin documento no hay a quién sincronizar; FinalizeDraftGate lo señalará (Fase 3).

        var instance = await repo.GetByIdWithActorsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return;

        // Idempotente: ya hay vendedor (ejecución previa, migración, o —en un tipo futuro que combine
        // esta pieza con captura por formulario— el gestor ya lo guardó a mano).
        if (instance.Actors.Any(a => string.Equals(a.ActorType, VendedorActorType, StringComparison.OrdinalIgnoreCase)))
            return;

        var entity = await catalogRepo.GetProcedureEntityByCodeAsync(VendedorEntityCode, ct);
        if (entity is null)
        {
            // Nunca bloquea: sin la fila de catálogo, el trámite se sigue creando igual y
            // FinalizeDraftGate reportará actores_incompletos, como si esta pieza no existiera.
            SyncSellerActorFromRuntLog.EntidadCatalogoFaltante(_logger, instanceId);
            return;
        }

        // Best-effort: un lookup fallido (proveedor caído, NIT no soportado por el RUNT-conductor,
        // documento no encontrado) NUNCA rompe la creación del trámite — se persiste igual con lo que
        // sí se conoce (el documento) y el nombre en blanco.
        string? fullName = null;
        try
        {
            var (lookup, _) = await personLookup.HandleAsync(instanceId, tenantId, documentType, documentNumber, ct);
            if (lookup is { Found: true })
                fullName = lookup.FullName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SyncSellerActorFromRuntLog.LookupFallo(_logger, ex, instanceId);
        }

        // El RUNT-conductor (RuntPersonLookupHandler) solo resuelve persona NATURAL; un NIT llega hasta
        // aquí igual (no se descarta antes) para que la fila quede creada con el documento aunque el
        // nombre no resuelva — es exactamente el caso "persona jurídica sin RL utilizable" que
        // WizardStateQuery revelará en Fase 3 (Decisión 6 del ADR).
        var actor = new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = entity.Id,
            ActorType = VendedorActorType,
            DocumentType = documentType.ToUpperInvariant(),
            DocumentNumber = documentNumber,
            FullName = fullName?.Trim() ?? string.Empty,
            PersonType = string.Equals(documentType, "NIT", StringComparison.OrdinalIgnoreCase)
                ? ActorPersonTypes.Juridical
                : ActorPersonTypes.Natural,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(actor);
        // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito, mismo idiom que
        // PutActorsHandler/UpsertSingleField (sin esto EF infiere Modified por la PK no-default).
        repo.Add(actor);
        await repo.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Trazas de la sincronización. NUNCA loguea nombre, documento ni correo del vendedor (Habeas Data,
/// ver remarks de <see cref="SyncSellerActorFromRuntHandler"/>) — solo el id del trámite.
/// </summary>
internal static partial class SyncSellerActorFromRuntLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sincronización del vendedor omitida para el trámite {ProcedureInstanceId}: falta la entidad de catálogo 'OWNER'.")]
    public static partial void EntidadCatalogoFaltante(ILogger logger, Guid procedureInstanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Falló el lookup RUNT del vendedor para el trámite {ProcedureInstanceId}; se sincroniza igual con el documento, sin nombre.")]
    public static partial void LookupFallo(ILogger logger, Exception ex, Guid procedureInstanceId);
}
