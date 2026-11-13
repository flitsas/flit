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
    [property: JsonPropertyName("respuesta_proveedor")] JsonElement? RespuestaProveedor = null,
    /// <summary>
    /// Solo impuesto en ERROR cuando la fuente marcó <c>dato_parcial</c>. El detalle lo rotula; el monto no se proyecta.
    /// Ausente (false) en el resto.
    /// </summary>
    [property: JsonPropertyName("dato_parcial")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool DatoParcial = false,
    /// <summary>
    /// HU #14563 B5: auditoría saneada de la consulta (<see cref="AuditoriaConsultaCliente"/>), sin intentos manuales ni datos
    /// internos. Null si la celda no tiene intentos no manuales.
    /// </summary>
    [property: JsonPropertyName("auditoria")] AuditoriaConsulta? Auditoria = null);

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
    IReadOnlyList<AcuerdoSimitDetalle>? Acuerdos = null,
    /// <summary>HU #14563 B4: resoluciones del SIMIT. Null si la fuente no las informó (nunca una lista vacía inferida).</summary>
    [property: JsonPropertyName("resoluciones")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ResolucionSimitDetalle>? Resoluciones = null,
    /// <summary>HU #14563 B4: cuántas resoluciones informó la fuente. Null (no 0) si no las informó.</summary>
    [property: JsonPropertyName("cantidad_resoluciones")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? CantidadResoluciones = null) : ResultadoDetalleDto;

/// <summary>Una multa del SIMIT. Sin el infractor: el nombre y el documento no salen.</summary>
public sealed record MultaSimitDetalle(
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("fecha")] string? Fecha,
    [property: JsonPropertyName("valor")] string? Valor,
    [property: JsonPropertyName("valor_pagar")] string? ValorPagar,
    [property: JsonPropertyName("estado_cartera")] string? EstadoCartera,
    [property: JsonPropertyName("organismo")] string? Organismo,
    [property: JsonPropertyName("codigos")] string? Codigos,
    /// <summary>HU #14563 B4: las infracciones de la multa con código y descripción (null si la fuente no trae).</summary>
    [property: JsonPropertyName("infracciones")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InfraccionSimitDetalle>? Infracciones = null);

/// <summary>Una infracción de una multa del SIMIT (HU #14563 B4): código y descripción; la descripción es null si la fuente no la trae.</summary>
public sealed record InfraccionSimitDetalle(
    [property: JsonPropertyName("codigo")] string? Codigo,
    [property: JsonPropertyName("descripcion")] string? Descripcion);

/// <summary>Una resolución del SIMIT (HU #14563 B4), sin el infractor.</summary>
public sealed record ResolucionSimitDetalle(
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("fecha")] string? Fecha,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("valor")] string? Valor,
    [property: JsonPropertyName("organismo")] string? Organismo);

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
/// Impuesto vehicular departamental. <c>tiene_deuda</c> confirma la deuda; <c>monto_total</c> es null cuando el servicio no
/// informó valor (también con deuda: no se escribe 0). Los opcionales solo viajan si vinieron. Nunca el proveedor, la
/// transacción, el origen ni el departamento consultado (R-20).
/// </summary>
public sealed record ResultadoImpuestoDetalle(
    [property: JsonPropertyName("monto_total")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? MontoTotal,
    [property: JsonPropertyName("vigencias")] IReadOnlyList<VigenciaImpuestoDetalle>? Vigencias,
    [property: JsonPropertyName("tiene_deuda")] bool TieneDeuda = false,
    [property: JsonPropertyName("intereses_mora")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? InteresesMora = null,
    [property: JsonPropertyName("sanciones")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? Sanciones = null,
    [property: JsonPropertyName("avaluo_comercial")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? AvaluoComercial = null,
    [property: JsonPropertyName("ultimo_pago")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    UltimoPagoImpuestoDetalle? UltimoPago = null,
    [property: JsonPropertyName("liquidacion_consolidada_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LiquidacionConsolidadaId = null,
    [property: JsonPropertyName("periodo_certificacion")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PeriodoCertificacion = null,
    [property: JsonPropertyName("fecha_consulta")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FechaConsulta = null) : ResultadoDetalleDto
{
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>
    /// Proyección cerrada del payload. Si falta <c>tiene_deuda</c> y el monto es positivo, se toma como deuda (celdas anteriores
    /// a la bandera). Un monto 0 con deuda confirmada queda null: no es un valor informado.
    /// </summary>
    public static ResultadoImpuestoDetalle? Desde(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            var leido = e.Deserialize<ImpuestoLeido>(Json);
            if (leido is null)
                return null;
            bool? explicita = e.TryGetProperty("tiene_deuda", out var bandera) && bandera.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? bandera.GetBoolean()
                : null;
            long? monto = e.TryGetProperty("monto_total", out var montoJson) && montoJson.TryGetInt64(out var pesos) ? pesos : null;
            var deuda = explicita ?? monto is > 0;
            if (deuda && monto is 0)
                monto = null;
            return new ResultadoImpuestoDetalle(
                monto,
                [.. (leido.Vigencias ?? []).OrderBy(v => v.Anio)],
                deuda,
                NoNegativo(leido.InteresesMora),
                NoNegativo(leido.Sanciones),
                NoNegativo(leido.AvaluoComercial),
                Pago(leido.UltimoPago),
                Texto(leido.LiquidacionConsolidadaId),
                Texto(leido.PeriodoCertificacion),
                Texto(Fecha(e)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>La respuesta cifrada del impuesto trajo <c>dato_parcial</c>. No arma un monto.</summary>
    public static bool EsDatoParcial(JsonElement? respuesta) =>
        respuesta is { ValueKind: JsonValueKind.Object } e
        && e.TryGetProperty("dato_parcial", out var valor)
        && valor.ValueKind == JsonValueKind.True;

    private static long? NoNegativo(long? valor) => valor is < 0 ? null : valor;

    private static string? Texto(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : limpio;
    }

    private static UltimoPagoImpuestoDetalle? Pago(UltimoPagoImpuestoDetalle? pago)
    {
        if (pago is null)
            return null;
        var anio = pago.Anio is > 0 ? pago.Anio : null;
        var valor = pago.Valor is > 0 ? pago.Valor : null;
        var fecha = Texto(pago.Fecha);
        return anio is null && valor is null && fecha is null ? null : new UltimoPagoImpuestoDetalle(anio, valor, fecha);
    }

    private static string? Fecha(JsonElement e) =>
        Texto(LeerTexto(e, "fecha_consulta_proveedor") ?? LeerTexto(e, "fecha_consulta"));

    private static string? LeerTexto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private sealed record ImpuestoLeido(
        [property: JsonPropertyName("vigencias")] List<VigenciaImpuestoDetalle>? Vigencias,
        [property: JsonPropertyName("intereses_mora")] long? InteresesMora,
        [property: JsonPropertyName("sanciones")] long? Sanciones,
        [property: JsonPropertyName("avaluo_comercial")] long? AvaluoComercial,
        [property: JsonPropertyName("ultimo_pago")] UltimoPagoImpuestoDetalle? UltimoPago,
        [property: JsonPropertyName("liquidacion_consolidada_id")] string? LiquidacionConsolidadaId,
        [property: JsonPropertyName("periodo_certificacion")] string? PeriodoCertificacion);
}

/// <summary>Último pago informado. Un campo vacío no se serializa.</summary>
public sealed record UltimoPagoImpuestoDetalle(
    [property: JsonPropertyName("anio")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Anio,
    [property: JsonPropertyName("valor")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? Valor,
    [property: JsonPropertyName("fecha")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Fecha);

/// <summary>Una vigencia del impuesto: año, valor en pesos enteros y si está pagada.</summary>
public sealed record VigenciaImpuestoDetalle(
    [property: JsonPropertyName("anio")] int Anio,
    [property: JsonPropertyName("valor")] long Valor,
    [property: JsonPropertyName("pagada")] bool Pagada);
