using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0051 Decisión 5 — sincroniza el actor "vendedor" (propietario) desde las consultas del paso 1
/// cuando el tipo de trámite exige parte vendedora pero NO la captura por formulario
/// (<c>gate_profile.requiresSeller &amp;&amp; !sellerCapturedViaForm</c>, hoy solo
/// <c>TRASPASO_UNILATERAL</c>: el locatario formaliza el traspaso a su nombre y el propietario nunca
/// pasa por el wizard).
///
/// <para>Sin esta pieza NADA crea esa fila: <c>FinalizeDraftGate</c> bloquearía el 100% de esos
/// borradores con <c>actores_incompletos</c> y <c>FurCommand.AddParte(..., "vendedor")</c> no
/// encontraría datos que estampar.</para>
///
/// <para><b>Fuente del dato.</b> <c>owner_document_type</c>/<c>owner_document_number</c> ya los tecleó
/// el gestor en el paso 1 (<see cref="CreateProcedureInstanceFromConsultaHandler"/>). Con ese
/// documento, esta pieza dispara UNA de dos consultas ya existentes, según el tipo de documento — el
/// propietario de un leasing casi siempre es la compañía de leasing (persona jurídica), así que las
/// dos vías son necesarias, no solo la natural:</para>
/// <list type="bullet">
/// <item><description><c>NIT</c> → <see cref="RuesActorJuridicalLookup"/> (RUES, <c>verifik_rues</c>),
/// razón social ⇒ <c>FullName</c>, <c>PersonType = juridical</c>. Núcleo COMPARTIDO con
/// <see cref="RuesPersonLookupHandler"/>/<c>RuesPreviewHandler</c> — no se resuelve el provider por
/// segunda vez.</description></item>
/// <item><description>Cualquier otro documento (CC/CE/PAS/TI) → <see cref="RuntPersonLookupHandler"/>
/// (RUNT-conductor), mismo patrón que autopobla al comprador en matrícula, <c>PersonType =
/// natural</c>.</description></item>
/// </list>
///
/// <para>En ambos casos el resultado se PERSISTE como fila de <c>instance.Actors</c> en vez de
/// devolverse para que un formulario lo confirme — aquí no hay formulario que lo haga por ella. Mismo
/// idioma de persistencia que <c>PutActorsHandler</c> (ActorsCommand.cs): entity id resuelto del
/// catálogo, PK store-generated con <c>repo.Add</c> explícito.</para>
///
/// <para><b>Best-effort real (degradación, nunca error), en las dos vías.</b> Si la consulta que
/// corresponda falla, no resuelve nombre, o el proveedor no está registrado
/// (<c>RuesActorJuridicalLookup</c> devuelve <c>Error = "provider_not_found"</c> sin lanzar;
/// <c>RuntPersonLookupHandler</c> puede no encontrar la persona), la fila se crea igual con lo que sí
/// se conoce (documento) y <c>FullName</c> en blanco. El criterio de completitud de una parte
/// SINCRONIZADA sin nombre (basta el documento, o degrada a "NO REGISTRA" en el FUR) es Fase 3
/// (<c>FinalizeDraftProcedureInstanceCommand</c>, fuera de este alcance) — hoy esa combinación queda
/// con el mismo <c>actores_incompletos</c> que ya bloquea cualquier borrador sin ese dato, sin
/// formulario con el que el gestor lo corrija manualmente (riesgo documentado en el ADR, Decisión 5/6:
/// el revelado del formulario oculto para ese caso es Fase 3).</para>
///
/// <para><b>Habeas Data (nota para el Security Agent, sin resolver aquí).</b> Esta pieza persiste datos
/// personales de un tercero (el propietario) a partir de una consulta, sin que esa persona haya
/// diligenciado un formulario en ESTE trámite. El ADR-0051 deja pendiente confirmar si el consentimiento
/// ya obtenido en el contrato de leasing (fuera del sistema) cubre esta persistencia, o si hace falta
/// una nota de origen del dato (por ejemplo <c>Source="runt_sync"</c>/<c>"rues_sync"</c>) para
/// trazabilidad. Por eso este handler NUNCA loguea el nombre, documento ni correo del vendedor — solo
/// el id del trámite y, cuando aplica, el tipo de documento consultado (NIT vs. natural), que no es
/// dato personal per se.</para>
/// </summary>
public sealed class SyncSellerActorFromConsultationsHandler(
    IProcedureInstanceRepository repo,
    ICatalogRepository catalogRepo,
    IConsultationProviderRegistry registry,
    RuntPersonLookupHandler personLookup,
    ILogger<SyncSellerActorFromConsultationsHandler>? logger = null)
{
    /// <summary><c>actor_type</c> que se persiste (mismo vocabulario que ActorsCommand/FurCommand).</summary>
    private const string VendedorActorType = "vendedor";

    /// <summary>Código de catálogo (procedure_entities) del propietario — mismo mapeo que ActorsCommand.RolToEntityCode.</summary>
    private const string VendedorEntityCode = "OWNER";

    private const string DocumentTypeNit = "NIT";

    private readonly ILogger _logger = logger ?? NullLogger<SyncSellerActorFromConsultationsHandler>.Instance;

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
            return; // Sin documento no hay a quien sincronizar; FinalizeDraftGate lo señalará (Fase 3).

        var instance = await repo.GetByIdWithActorsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return;

        // Idempotente: ya hay vendedor (ejecución previa, migración, o -en un tipo futuro que combine
        // esta pieza con captura por formulario- el gestor ya lo guardó a mano).
        if (instance.Actors.Any(a => string.Equals(a.ActorType, VendedorActorType, StringComparison.OrdinalIgnoreCase)))
            return;

        var entity = await catalogRepo.GetProcedureEntityByCodeAsync(VendedorEntityCode, ct);
        if (entity is null)
        {
            // Nunca bloquea: sin la fila de catálogo, el trámite se sigue creando igual y
            // FinalizeDraftGate reportará actores_incompletos, como si esta pieza no existiera.
            SyncSellerActorFromConsultationsLog.EntidadCatalogoFaltante(_logger, instanceId);
            return;
        }

        var esJuridica = string.Equals(documentType, DocumentTypeNit, StringComparison.OrdinalIgnoreCase);
        var fullName = esJuridica
            ? await ResolveJuridicalNameAsync(instanceId, tenantId, documentNumber, ct)
            : await ResolveNaturalNameAsync(instanceId, tenantId, documentType, documentNumber, ct);

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
            PersonType = esJuridica ? ActorPersonTypes.Juridical : ActorPersonTypes.Natural,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(actor);
        // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito, mismo idiom que
        // PutActorsHandler/UpsertSingleField (sin esto EF infiere Modified por la PK no-default).
        repo.Add(actor);
        await repo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// NIT → RUES, vía el núcleo COMPARTIDO <see cref="RuesActorJuridicalLookup"/> (mismo provider que
    /// usan <c>RuesPersonLookupHandler</c>/<c>RuesPreviewHandler</c>, sin resolverlo por segunda vez).
    /// Best-effort: <c>provider_not_found</c> o cualquier excepción degradan a <c>null</c> (sin nombre),
    /// nunca rompen la sincronización.
    /// </summary>
    private async Task<string?> ResolveJuridicalNameAsync(
        Guid instanceId, Guid tenantId, string nit, CancellationToken ct)
    {
        try
        {
            var (result, error) = await RuesActorJuridicalLookup.ConsultAsync(registry, instanceId, tenantId, nit, ct);
            if (error is not null || result is null)
            {
                SyncSellerActorFromConsultationsLog.ProveedorNoDisponible(_logger, instanceId, DocumentTypeNit);
                return null;
            }

            return RuesActorJuridicalLookup.GetHydrated(result.HydratedFields, "rues_razon_social");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort (ADR-0051 Decisión 5): un lookup fallido NUNCA rompe la creación del trámite.
            SyncSellerActorFromConsultationsLog.LookupFallo(_logger, ex, instanceId);
            return null;
        }
    }

    /// <summary>
    /// Cualquier documento de persona natural (CC/CE/PAS/TI) → RUNT-conductor, vía
    /// <see cref="RuntPersonLookupHandler"/> (best-effort, NO persiste — la persistencia del actor la
    /// hace este handler, no aquel).
    /// </summary>
    private async Task<string?> ResolveNaturalNameAsync(
        Guid instanceId, Guid tenantId, string documentType, string documentNumber, CancellationToken ct)
    {
        try
        {
            var (lookup, _) = await personLookup.HandleAsync(instanceId, tenantId, documentType, documentNumber, ct);
            return lookup is { Found: true } ? lookup.FullName : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort (ADR-0051 Decisión 5): un lookup fallido NUNCA rompe la creación del trámite.
            SyncSellerActorFromConsultationsLog.LookupFallo(_logger, ex, instanceId);
            return null;
        }
    }
}

/// <summary>
/// Trazas de la sincronización. NUNCA loguea nombre, documento ni correo del vendedor (Habeas Data,
/// ver remarks de <see cref="SyncSellerActorFromConsultationsHandler"/>) — solo el id del trámite y,
/// cuando aplica, el tipo de documento consultado.
/// </summary>
internal static partial class SyncSellerActorFromConsultationsLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sincronización del vendedor omitida para el trámite {ProcedureInstanceId}: falta la entidad de catálogo 'OWNER'.")]
    public static partial void EntidadCatalogoFaltante(ILogger logger, Guid procedureInstanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "El proveedor de la consulta ({DocumentType}) no está registrado para el trámite {ProcedureInstanceId}; el vendedor se sincroniza igual con el documento, sin nombre.")]
    public static partial void ProveedorNoDisponible(ILogger logger, Guid procedureInstanceId, string documentType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Falló el lookup del vendedor para el trámite {ProcedureInstanceId}; se sincroniza igual con el documento, sin nombre.")]
    public static partial void LookupFallo(ILogger logger, Exception ex, Guid procedureInstanceId);
}
