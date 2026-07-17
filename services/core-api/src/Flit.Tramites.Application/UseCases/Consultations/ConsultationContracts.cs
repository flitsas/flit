namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Resultado normalizado de una consulta a un proveedor externo (Verifik RUNT, etc.).
/// El contrato (literales de Overall/Status) es estable y consumido por el frontend
/// (PreflightOverall / PreflightCheckStatus). NO usar enums para preservar la
/// serializacion JSON exacta.
/// </summary>
public sealed record ConsultationResult(
    string Provider,
    string Overall,
    IReadOnlyList<ConsultationCheck> Checks,
    IReadOnlyList<HydratedField> HydratedFields);

/// <summary>
/// Un check individual de la consulta. Status ∈ {"ok","warn","fail","unknown","error"}.
/// <c>"error"</c> = el proveedor no respondió correctamente (no-200, timeout, red) y NO se
/// pudo verificar la información: es un BLOQUEO DURO (no subsanable con "aceptar riesgo"),
/// distinto de <c>"unknown"</c> (dato ausente pero no crítico, no bloquea).
///
/// <para><see cref="Details"/> es el detalle line-by-line del hallazgo (hoy: los comparendos de un
/// check de multas). Opcional y aditivo: los checks que no son de comparendos lo dejan en <c>null</c>.
/// Fluye tal cual al snapshot del preflight y al frontend para pintar la lista bajo la advertencia.</para>
/// </summary>
public sealed record ConsultationCheck(
    string Key,
    string Label,
    string Status,
    string Source,
    string? Message,
    IReadOnlyList<FineDetail>? Details = null);

/// <summary>
/// Detalle de UN comparendo/multa pendiente, para listarlo bajo la advertencia de multas del
/// preflight. Todos los campos son opcionales: cada proveedor llena lo que su respuesta expone
/// (la fuente interna solo trae número/valor/estado; el SIMIT de Verifik agrega fecha, organismo e
/// infracción). NUNCA incluye datos del infractor (PII / Habeas Data): solo información del comparendo.
/// </summary>
public sealed record FineDetail(
    string? Numero,
    string? Fecha,
    decimal? Valor,
    string? Organismo,
    string? Estado,
    string? Infraccion);

/// <summary>
/// Campo del trámite que la consulta puede hidratar (escribir) en la instancia.
/// </summary>
public sealed record HydratedField(
    string FieldKey,
    string? ValueText,
    string? ValueJson);

/// <summary>
/// Contexto que el provider recibe para resolver la consulta. FieldValues mapea
/// field_key → value_text actual de la instancia.
/// </summary>
public sealed record ConsultationContext(
    Guid InstanceId,
    Guid TenantId,
    string TemplateCode,
    IReadOnlyDictionary<string, string?> FieldValues);
