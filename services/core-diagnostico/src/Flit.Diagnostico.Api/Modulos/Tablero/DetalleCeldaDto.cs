using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Respuesta de <c>GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}</c> (HU #13798, F06-H1, F06-C1..C4/C12): el
/// resultado normalizado de UNA celda para la compañía. Es un DTO propio y cerrado: no tiene origen, usuario manual,
/// transacción ni payload en campos propios (R-20), así que exponer uno de ellos obliga a cambiar este tipo (lo verifica
/// <c>DetalleCeldaTests</c> por reflexión). Una celda llenada a mano y una consultada por API tienen las mismas
/// claves. Todas las claves van siempre (con <c>null</c> si no aplican): <c>resultado</c> en las fuentes del vehículo
/// (RUNT, SIMIT_VEHICULO, AVALUO, IMPUESTO_DEPARTAMENTAL) con la celda COMPLETADO; <c>personas</c> en las fuentes de persona (RUNT_PROPIETARIO,
/// RUNT_LOCATARIO, SIMIT_PROPIETARIO, SIMIT_LOCATARIO), null en las demás.
/// <para>
/// HU #14087 AC8 (decisión del usuario 2026-10-27; enmienda de ADR-0075 en Propuesto): <c>respuesta_fuente</c> es la respuesta
/// COMPLETA de la fuente (el <c>*Response</c> del rpc en proto-JSON, nombres del .proto en snake_case y valores por defecto),
/// descifrada, tal cual: con datos personales y lo que la fuente responda. En la celda solo en las fuentes del vehículo; en las de
/// persona va en cada <see cref="PersonaDetalleDto"/>. Null sin respuesta guardada o si no se puede descifrar.
/// </para>
/// <para>
/// HU #14087 (detalle en pestaña nueva): <c>identificador</c> es la placa del vehículo o, sin placa, su VIN (y, sin ninguno de
/// los dos, el identificador normalizado de la carga); <c>nombre_diagnostico</c> es el nombre del lote. Sirven para que la página
/// se titule sola; no son origen, usuario ni transacción (R-20 sigue vigente).
/// </para>
/// <para>
/// HU #14183: <c>respuesta_proveedor</c> a nivel de celda es el JSON de <c>resultado.respuesta_cruda.json</c> parseado como
/// objeto, la misma regla que <see cref="PersonaDetalleDto.RespuestaProveedor"/>. Solo se llena en SIMIT_VEHICULO. En las
/// celdas de persona sigue en <c>personas[].respuesta_proveedor</c>. Null si falta, no es JSON o no es un objeto.
/// </para>
/// </summary>
public sealed record DetalleCeldaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("motivo_na")] string? MotivoNa,
    [property: JsonPropertyName("tipo_error")] string? TipoError,
    [property: JsonPropertyName("dato_obtenido_en")] DateTimeOffset? DatoObtenidoEn,
    [property: JsonPropertyName("reutilizada")] bool Reutilizada,
    [property: JsonPropertyName("resultado")] ResultadoDetalleDto? Resultado,
    [property: JsonPropertyName("personas")] IReadOnlyList<PersonaDetalleDto>? Personas,
    [property: JsonPropertyName("respuesta_fuente")] JsonElement? RespuestaFuente,
    [property: JsonPropertyName("identificador")] string Identificador,
    [property: JsonPropertyName("nombre_diagnostico")] string NombreDiagnostico,
    [property: JsonPropertyName("respuesta_proveedor")] JsonElement? RespuestaProveedor = null);

/// <summary>
/// Una persona de una celda de persona: su posición y rol, el tipo de documento, el documento enmascarado (<c>****</c> más
/// los cuatro últimos caracteres; nunca completo, <see cref="EnmascaradorDocumento"/>), si lo declaró el cliente, qué se
/// consultó (<c>RUNT_PERSONA</c>, <c>RUES</c> o <c>SIMIT</c>), su estado (<c>OK</c>, <c>ERROR</c> o <c>SIN_RESULTADO</c>
/// si la celda aún no tiene dato de esa persona), el tipo de error, la fecha del dato y su resultado normalizado. Nunca el id
/// interno de la persona. <c>respuesta_fuente</c> (HU #14087 AC8): la respuesta completa de la fuente para esa persona, descifrada
/// (con nombre y lo que la fuente responda); null si no hay.
/// <para>HU #14153 (decisión del usuario 2026-10-28, literal): <c>respuesta_proveedor</c> es el JSON que devolvió el proveedor
/// (<c>resultado.respuesta_cruda.json</c> de <c>respuesta_fuente</c>) parseado como objeto, sin quitar ningún dato: RUNT persona
/// (Kyverum/Verifik conductor) y RUES. Null si no hay crudo, si no es JSON válido o si no es un objeto (log sin el contenido).</para>
/// </summary>
public sealed record PersonaDetalleDto(
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("rol")] string Rol,
    [property: JsonPropertyName("tipo_documento")] string TipoDocumento,
    [property: JsonPropertyName("documento_enmascarado")] string DocumentoEnmascarado,
    [property: JsonPropertyName("declarado_por_cliente")] bool DeclaradoPorCliente,
    [property: JsonPropertyName("consulta")] string? Consulta,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("tipo_error")] string? TipoError,
    [property: JsonPropertyName("consultado_en")] DateTimeOffset? ConsultadoEn,
    [property: JsonPropertyName("resultado")] ResultadoDetalleDto? Resultado,
    [property: JsonPropertyName("respuesta_fuente")] JsonElement? RespuestaFuente = null,
    [property: JsonPropertyName("respuesta_proveedor")] JsonElement? RespuestaProveedor = null,
    /// <summary>Número de documento completo para el detalle de la consulta. La grilla sigue con el enmascarado.</summary>
    [property: JsonPropertyName("documento")] string? Documento = null);

/// <summary>
/// Resultado normalizado de una fuente en el detalle. Conjunto cerrado de tipos (polimorfismo de solo escritura, sin
/// discriminador): <see cref="ResultadoRuntDetalle"/>, <see cref="ResultadoSimitDetalle"/>, <see cref="ResultadoAvaluoDetalle"/>,
/// <see cref="ResultadoImpuestoDetalle"/>, <see cref="ResultadoRuntPersonaDetalle"/> y <see cref="ResultadoRuesDetalle"/>. Sumar un
/// tipo exige registrarlo aquí (y en el <c>oneOf</c> de <c>DetalleCelda.resultado</c> del OpenAPI).
/// </summary>
[JsonDerivedType(typeof(ResultadoRuntDetalle))]
[JsonDerivedType(typeof(ResultadoSimitDetalle))]
[JsonDerivedType(typeof(ResultadoAvaluoDetalle))]
[JsonDerivedType(typeof(ResultadoImpuestoDetalle))]
[JsonDerivedType(typeof(ResultadoRuntPersonaDetalle))]
[JsonDerivedType(typeof(ResultadoRuesDetalle))]
public abstract record ResultadoDetalleDto;

/// <summary>RUNT del vehículo: placa, VIN, servicio, clase, organismo, tipo de documento y de propiedad de cada propietario y cuántos locatarios.</summary>
public sealed record ResultadoRuntDetalle(
    [property: JsonPropertyName("placa")] string? Placa,
    [property: JsonPropertyName("vin")] string? Vin,
    [property: JsonPropertyName("servicio")] string? Servicio,
    [property: JsonPropertyName("clase")] string? Clase,
    [property: JsonPropertyName("organismo")] string? Organismo,
    [property: JsonPropertyName("propietarios")] IReadOnlyList<PropietarioRuntDetalle>? Propietarios,
    [property: JsonPropertyName("locatarios")] int Locatarios) : ResultadoDetalleDto;

/// <summary>Un propietario del RUNT del vehículo, sin número ni nombre.</summary>
public sealed record PropietarioRuntDetalle(
    [property: JsonPropertyName("tipo_documento")] string? TipoDocumento,
    [property: JsonPropertyName("tipo_propiedad")] string? TipoPropiedad);

/// <summary>
/// SIMIT (del vehículo o de una persona): semáforo, cantidad total de comparendos y, como mucho,
/// <see cref="DetalleCelda.TopeComparendos"/> comparendos; <c>comparendos_truncados=true</c> si había más.
/// </summary>
public sealed record ResultadoSimitDetalle(
    [property: JsonPropertyName("semaforo")] string? Semaforo,
    [property: JsonPropertyName("cantidad_comparendos")] int CantidadComparendos,
    [property: JsonPropertyName("comparendos")] IReadOnlyList<ComparendoDetalle>? Comparendos,
    [property: JsonPropertyName("comparendos_truncados")] bool ComparendosTruncados,
    [property: JsonPropertyName("total_general")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TotalGeneral = null,
    [property: JsonPropertyName("total_por_pagar")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TotalPorPagar = null,
    [property: JsonPropertyName("paz_y_salvo")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? PazYSalvo = null,
    [property: JsonPropertyName("suspendida")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Suspendida = null,
    [property: JsonPropertyName("cancelada")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Cancelada = null,
    [property: JsonPropertyName("multas")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<MultaSimitDetalle>? Multas = null,
    [property: JsonPropertyName("acuerdos_pago")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AcuerdoSimitDetalle>? Acuerdos = null) : ResultadoDetalleDto;

/// <summary>Una multa del SIMIT. Sin el infractor: el nombre y el documento no salen.</summary>
public sealed record MultaSimitDetalle(
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("fecha")] string? Fecha,
    [property: JsonPropertyName("valor")] string? Valor,
    [property: JsonPropertyName("valor_pagar")] string? ValorPagar,
    [property: JsonPropertyName("estado_cartera")] string? EstadoCartera,
    [property: JsonPropertyName("organismo")] string? Organismo,
    [property: JsonPropertyName("codigos")] string? Codigos);

/// <summary>Un acuerdo de pago del SIMIT, sin el infractor.</summary>
public sealed record AcuerdoSimitDetalle(
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("cuotas_abonadas")] string? CuotasAbonadas,
    [property: JsonPropertyName("cuotas_pendientes")] string? CuotasPendientes,
    [property: JsonPropertyName("pendiente")] string? Pendiente,
    [property: JsonPropertyName("fecha")] string? Fecha,
    [property: JsonPropertyName("organismo")] string? Organismo);

/// <summary>Un comparendo: número, fecha (AAAA-MM-DD), valor (texto decimal en pesos), estado y organismo.</summary>
public sealed record ComparendoDetalle(
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("fecha")] string? Fecha,
    [property: JsonPropertyName("valor")] string? Valor,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("organismo")] string? Organismo);

/// <summary>Avalúo comercial: «sin valor comercial» (Q4 = b) o el valor sugerido con su fuente principal, y el valor de cada fuente.</summary>
public sealed record ResultadoAvaluoDetalle(
    [property: JsonPropertyName("sin_valor_comercial")] bool SinValorComercial,
    [property: JsonPropertyName("valor_sugerido")] long? ValorSugerido,
    [property: JsonPropertyName("fuente_principal")] string? FuentePrincipal,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<FuenteAvaluoDetalle>? Fuentes) : ResultadoDetalleDto;

/// <summary>Valor de una fuente del avalúo (<c>fasecolda</c>, <c>mercado_libre</c>, <c>base_gravable</c>): estado, valor, muestras y fecha.</summary>
public sealed record FuenteAvaluoDetalle(
    [property: JsonPropertyName("fuente")] string? Fuente,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("valor")] long? Valor,
    [property: JsonPropertyName("muestras")] int? Muestras,
    [property: JsonPropertyName("fecha")] string? Fecha);

/// <summary>RUNT de una persona natural: si está inscrita, su estado de conductor y sus licencias.</summary>
public sealed record ResultadoRuntPersonaDetalle(
    [property: JsonPropertyName("inscrito")] bool Inscrito,
    [property: JsonPropertyName("estado_conductor")] string? EstadoConductor,
    [property: JsonPropertyName("licencias")] IReadOnlyList<LicenciaDetalle>? Licencias,
    [property: JsonPropertyName("fecha_inscripcion")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FechaInscripcion = null,
    [property: JsonPropertyName("tiene_multas")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TieneMultas = null,
    [property: JsonPropertyName("paz_y_salvo")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PazYSalvo = null,
    [property: JsonPropertyName("certificados_aptitud")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CertificadoAptitudDetalle>? Certificados = null,
    [property: JsonPropertyName("solicitudes_conductor")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SolicitudConductorDetalle>? Solicitudes = null) : ResultadoDetalleDto;

/// <summary>Certificado de aptitud del conductor. Sin el documento ni el nombre de la persona.</summary>
public sealed record CertificadoAptitudDetalle(
    [property: JsonPropertyName("categoria")] string? Categoria,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("fecha_expedicion")] string? Expedida,
    [property: JsonPropertyName("vigente_hasta")] string? Vence,
    [property: JsonPropertyName("tipo")] string? Tipo,
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("entidad")] string? Entidad);

/// <summary>Trámite del conductor ante un organismo. Sin el identificador, que puede ser el documento.</summary>
public sealed record SolicitudConductorDetalle(
    [property: JsonPropertyName("tramite")] string? Tramite,
    [property: JsonPropertyName("estado_solicitud")] string? EstadoSolicitud,
    [property: JsonPropertyName("estado_tramite")] string? EstadoTramite,
    [property: JsonPropertyName("fecha")] string? Fecha,
    [property: JsonPropertyName("entidad")] string? Entidad,
    [property: JsonPropertyName("registro")] string? Registro);

/// <summary>Una licencia de conducción: categoría, estado, vigencia y número cuando la fuente lo informa.</summary>
public sealed record LicenciaDetalle(
    [property: JsonPropertyName("categoria")] string? Categoria,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("vigente_hasta")] string? VigenteHasta,
    [property: JsonPropertyName("numero")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Numero = null,
    [property: JsonPropertyName("fecha_expedicion")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Expedida = null,
    [property: JsonPropertyName("organismo_expide")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Organismo = null,
    [property: JsonPropertyName("restricciones")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Restricciones = null,
    [property: JsonPropertyName("suspension_desde")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SuspensionDesde = null,
    [property: JsonPropertyName("suspension_hasta")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SuspensionHasta = null);

/// <summary>RUES de una persona jurídica (NIT, decisión Q2): si se encontró, el estado de la matrícula y la última renovación.</summary>
public sealed record ResultadoRuesDetalle(
    [property: JsonPropertyName("encontrada")] bool Encontrada,
    [property: JsonPropertyName("estado_matricula")] string? EstadoMatricula,
    [property: JsonPropertyName("ultima_renovacion")] string? UltimaRenovacion) : ResultadoDetalleDto;

/// <summary>
/// Impuesto vehicular departamental (HU #13864, F09-C9, Q1 = a): el total adeudado (suma de las vigencias no pagadas) y cada
/// vigencia, en pesos enteros. Nunca el proveedor que resolvió el rpc, la transacción, el origen ni el departamento consultado (R-20).
/// </summary>
public sealed record ResultadoImpuestoDetalle(
    [property: JsonPropertyName("monto_total")] long MontoTotal,
    [property: JsonPropertyName("vigencias")] IReadOnlyList<VigenciaImpuestoDetalle>? Vigencias) : ResultadoDetalleDto;

/// <summary>Una vigencia del impuesto: año, valor en pesos enteros y si está pagada.</summary>
public sealed record VigenciaImpuestoDetalle(
    [property: JsonPropertyName("anio")] int Anio,
    [property: JsonPropertyName("valor")] long Valor,
    [property: JsonPropertyName("pagada")] bool Pagada);
