using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Respuesta de <c>GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}</c> (HU #13798, F06-H1, F06-C1..C4/C12): el
/// resultado normalizado de UNA celda para la compañía. Es un DTO propio y cerrado: no tiene origen, usuario manual,
/// proveedor, transacción, payload ni documentos completos (R-20), así que exponer uno de ellos obliga a cambiar este tipo
/// (lo verifica <c>DetalleCeldaTests</c> por reflexión). Una celda llenada a mano y una consultada por API tienen las mismas
/// claves. Todas las claves van siempre (con <c>null</c> si no aplican): <c>resultado</c> en las fuentes del vehículo
/// (RUNT, SIMIT_VEHICULO, AVALUO, IMPUESTO_DEPARTAMENTAL) con la celda COMPLETADO; <c>personas</c> en las fuentes de persona (RUNT_PROPIETARIO,
/// RUNT_LOCATARIO, SIMIT_PROPIETARIO, SIMIT_LOCATARIO), null en las demás.
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
    [property: JsonPropertyName("personas")] IReadOnlyList<PersonaDetalleDto>? Personas);

/// <summary>
/// Una persona de una celda de persona: su posición y rol, el tipo de documento, el documento enmascarado (<c>****</c> más
/// los cuatro últimos caracteres; nunca completo, <see cref="EnmascaradorDocumento"/>), si lo declaró el cliente, qué se
/// consultó (<c>RUNT_PERSONA</c>, <c>RUES</c> o <c>SIMIT</c>), su estado (<c>OK</c>, <c>ERROR</c> o <c>SIN_RESULTADO</c>
/// si la celda aún no tiene dato de esa persona), el tipo de error, la fecha del dato y su resultado normalizado. Nunca el
/// nombre ni el id interno de la persona.
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
    [property: JsonPropertyName("resultado")] ResultadoDetalleDto? Resultado);

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
    [property: JsonPropertyName("comparendos_truncados")] bool ComparendosTruncados) : ResultadoDetalleDto;

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
    [property: JsonPropertyName("licencias")] IReadOnlyList<LicenciaDetalle>? Licencias) : ResultadoDetalleDto;

/// <summary>Una licencia de conducción: categoría, estado y vigencia.</summary>
public sealed record LicenciaDetalle(
    [property: JsonPropertyName("categoria")] string? Categoria,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("vigente_hasta")] string? VigenteHasta);

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
