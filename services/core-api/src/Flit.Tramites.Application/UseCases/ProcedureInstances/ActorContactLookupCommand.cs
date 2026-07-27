using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Datos de CONTACTO conocidos de una persona (ciudad, correo, dirección, teléfono) — NUNCA nombre
/// ni documento (esos vienen SIEMPRE del RUNT, AC2). Los cuatro campos van en <c>null</c> cuando la
/// persona nunca ha sido actor de un trámite del tenant (AC3): la ausencia de antecedentes no es un
/// error, es la respuesta 200 esperada.
/// </summary>
public sealed record ActorContactLookupDto(
    string? Ciudad,
    string? Email,
    string? Direccion,
    string? Telefono);

/// <summary>
/// HU #10955 (AC2/AC3/AC4/AC5) — lookup de datos de contacto ya conocidos de una persona, para
/// precargarlos en el paso de actores del wizard SIN pedirle al operador que los reescriba. Reemplaza
/// (para el contacto) el reúso de la consulta externa de identidad que hacía
/// <see cref="Consultations.ExternalQueryCacheService.TryReusePersonAsync"/>: la identidad se sigue
/// consultando siempre en vivo (RUNT/RUES), pero el contacto SÍ puede precargarse porque es un dato
/// que la propia compañía ya capturó de esa persona (no una consulta a un tercero).
/// </summary>
/// <remarks>
/// Fuente: <c>procedure_instance_actors</c> del tenant (<see cref="IProcedureInstanceRepository.FindLatestActorContactAsync"/>),
/// el actor MÁS RECIENTE por <c>CreatedAt</c>. <c>Email</c>/<c>Phone</c> son columnas; <c>ciudad</c>/
/// <c>dirección</c> viven en <c>actor.metadata</c> (jsonb) — se reutiliza el parser de
/// <see cref="PutActorsHandler.ParseMetadata"/>, no se duplica la deserialización.
///
/// Sin gate de consentimiento (AC4, ADR-0031 actualizado): el contacto no requiere
/// <c>PersonDataConsent</c> — solo la reutilización de CONSULTAS EXTERNAS lo requería, y esa
/// reutilización queda eliminada para identidad de actores (HU #10955, AC1).
/// </remarks>
public sealed class ActorContactLookupHandler(IProcedureInstanceRepository repo)
{
    private static readonly HashSet<string> ValidDocumentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "CC", "CE", "NIT", "PAS", "TI" };

    private static readonly ActorContactLookupDto Empty = new(null, null, null, null);

    public async Task<(ActorContactLookupDto? Result, string? Error)> HandleAsync(
        Guid tenantId,
        string? documentType,
        string? documentNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType) || !ValidDocumentTypes.Contains(documentType.Trim()))
            return (null, "invalid_document_type");
        if (string.IsNullOrWhiteSpace(documentNumber))
            return (null, "missing_document_number");

        var docType = documentType.Trim().ToUpperInvariant();
        var docNumber = documentNumber.Trim();

        // AC5: aislamiento por tenant explícito, resuelto en el repositorio (WHERE tenant_id = ...).
        var actor = await repo.FindLatestActorContactAsync(tenantId, docType, docNumber, ct);
        if (actor is null)
            return (Empty, null); // AC3: sin antecedentes -> 200 con los 4 campos vacíos, nunca 404.

        var (ciudad, direccion, _) = PutActorsHandler.ParseMetadata(actor.Metadata);

        return (new ActorContactLookupDto(ciudad, actor.Email, direccion, actor.Phone), null);
    }
}
