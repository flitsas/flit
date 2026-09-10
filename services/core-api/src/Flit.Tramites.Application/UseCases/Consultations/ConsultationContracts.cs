using Flit.Tramites.Domain.Certifications;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Resultado normalizado de una consulta a un proveedor externo (Verifik RUNT, etc.).
/// El contrato (literales de Overall/Status) es estable y consumido por el frontend
/// (PreflightOverall / PreflightCheckStatus). NO usar enums para preservar la
/// serializacion JSON exacta.
/// </summary>
/// <remarks>
/// HU #10878 (ADR-0030): <see cref="FromCache"/>/<see cref="QueriedAt"/> son ADITIVOS (propiedades
/// posicionales opcionales, con default) — no rompen ninguna construcción existente de 4 argumentos
/// posicionales. <see cref="FromCache"/> = true cuando el resultado vino de
/// <c>tramites.external_query_cache</c> sin llamar al proveedor externo (AC1).
///
/// <para>HU #11303 (Feature #11301, ADR-0041): <see cref="Certifications"/> y
/// <see cref="RawPayload"/> siguen el mismo patrón aditivo. Son el canal por el que el mapper entrega
/// lo que certificó en <b>vocabulario canónico</b>, sin que ningún consumidor tenga que volver a
/// interpretar el texto libre de <see cref="HydratedFields"/>.</para>
///
/// <para>La costura es esta y no otra por dos razones. Un normalizador central sobre
/// <see cref="HydratedFields"/> sería un segundo mapeo sobre el primero y perdería lo que el mapper ya
/// sabe. Y fusionar entre proveedores en el resolutor de cadena exigiría llamar a más de uno, y
/// <b>cada llamada se cobra</b>: la fusión que hace falta es a lo largo del tiempo (consulta → OCR →
/// corrección → reconsulta), y esa vive en el almacén. Un proveedor que no lo implemente devuelve
/// <c>null</c> y degrada al camino actual.</para>
/// </remarks>
public sealed record ConsultationResult(
    string Provider,
    string Overall,
    IReadOnlyList<ConsultationCheck> Checks,
    IReadOnlyList<HydratedField> HydratedFields,
    bool FromCache = false,
    DateTimeOffset? QueriedAt = null,
    CertificationBundle? Certifications = null,
    RawProviderPayload? RawPayload = null);

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
    IReadOnlyList<FineDetail>? Details = null,
    IReadOnlyList<CheckDato>? Datos = null);

/// <summary>
/// Un dato del proveedor que respalda el resultado del check: la etiqueta y su valor, por separado.
///
/// <para><b>Por qué no es una cadena.</b> Los checks en OK mandaban <c>Message = null</c> y la tarjeta
/// del panel quedaba con la pastilla verde y el cuerpo vacío. El primer arreglo metió el respaldo en
/// el propio mensaje —«Vigente hasta … · Póliza … · Aseguradora …»— y eso se leía mal: tres campos
/// encadenados con puntos medios que el salto de línea partía por la mitad, y una etiqueta menos en
/// el último, como si la aseguradora fuera parte del número de póliza.</para>
///
/// <para>El mapeador ya tiene las partes separadas y las estaba aplastando para que el panel volviera
/// a separarlas: mandarlas así evita ese contrato implícito por separador y deja que la pantalla
/// decida cómo presentarlas.</para>
///
/// <para>Para las afirmaciones que no son un par etiqueta/valor —«Sin gravámenes ni prendas
/// registradas»— se sigue usando <c>Message</c>: no hay campo que etiquetar.</para>
/// </summary>
public sealed record CheckDato(string Etiqueta, string Valor);

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
