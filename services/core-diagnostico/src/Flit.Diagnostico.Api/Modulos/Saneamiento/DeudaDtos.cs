using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Panel de deuda de UN vehículo terminado (plan §6.5, HU #14576 AC36/AC37): <c>GET /diagnosticos/{id}/vehiculos/{vid}/deuda</c>.
/// Lo arma <see cref="ConsultaDeuda"/> desde la proyección cerrada de los reportes (<c>LectorContenidoReporte</c>), nunca desde el
/// crudo de la fuente. Lo mismo alimenta el PDF (<c>PdfDeuda</c>) y el Excel (<c>ExcelDeuda</c>).
/// <list type="bullet">
/// <item><see cref="Modo"/>: <c>SIMPLE</c> o <c>CON_ANALISIS</c>, la serialización de <c>diagnostico.v1.yaml</c> (C2).</item>
/// <item><see cref="Dictamen"/>: null en modo Simple o sin dictamen vigente.</item>
/// <item><see cref="Fuentes"/>: una entrada por fuente de deuda del alcance; en las fuentes de persona, una por persona.</item>
/// <item><see cref="Saneamiento"/>: el estado del saneamiento vigente del vehículo, o null si no hay.</item>
/// </list>
/// Sin nombre de persona en ninguna profundidad (SP-11) y sin total del SIMIT ni total general (S17).
/// </summary>
/// <example>
/// <code>
/// var panel = await consulta.ObtenerAsync(diagnosticoId, vehiculoId, ct);
/// var simit = panel.Fuentes.Where(f => f.Fuente == FuentesDeuda.SimitPropietario);
/// </code>
/// </example>
internal sealed record PanelDeudaDto(
    [property: JsonPropertyName("vehiculo")] VehiculoDeudaDto Vehiculo,
    [property: JsonPropertyName("modo")] string Modo,
    [property: JsonPropertyName("dictamen")] DictamenDeudaDto? Dictamen,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<FuenteDeudaDto> Fuentes,
    [property: JsonPropertyName("saneamiento")] SaneamientoDeudaDto? Saneamiento)
{
    /// <summary>Fecha de creación del lote (encabezado del PDF y del Excel); no sale en el JSON.</summary>
    [JsonIgnore]
    public DateTimeOffset? DiagnosticoCreadoEn { get; init; }

    /// <summary>Nombre del lote (encabezado del PDF y del Excel); no sale en el JSON.</summary>
    [JsonIgnore]
    public string? DiagnosticoNombre { get; init; }
}

/// <summary>Identificación del vehículo: id y placa (o VIN, o el identificador normalizado si no hay placa).</summary>
internal sealed record VehiculoDeudaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("placa")] string Placa);

/// <summary>Dictamen vigente: veredicto, versión del catálogo y sus motivos con efecto.</summary>
internal sealed record DictamenDeudaDto(
    [property: JsonPropertyName("veredicto")] string Veredicto,
    [property: JsonPropertyName("catalogo_version")] int CatalogoVersion,
    [property: JsonPropertyName("motivos")] IReadOnlyList<MotivoDeudaDto> Motivos);

/// <summary>Un motivo del dictamen: código de la regla, efecto (<c>BLOQUEA</c>/<c>ADVIERTE</c>) y texto.</summary>
internal sealed record MotivoDeudaDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("efecto")] string Efecto,
    [property: JsonPropertyName("texto")] string Texto);

/// <summary>
/// Una fuente del panel con su estado (<see cref="EstadosDeuda"/>). Los campos que no aplican a la fuente no se escriben:
/// SIMIT trae persona (en las de persona), paz y salvo, comparendos (valor como texto, tope 100) y acuerdos de pago; el impuesto
/// trae <c>monto_total</c> numérico y sus vigencias; SOAT y RTM traen <c>vence</c>. N/A trae <c>motivo_na</c>.
/// </summary>
internal sealed record FuenteDeudaDto(
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("consultado_en"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ConsultadoEn = null,
    [property: JsonPropertyName("motivo_na"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MotivoNa = null,
    [property: JsonPropertyName("persona"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PersonaDeudaDto? Persona = null,
    [property: JsonPropertyName("paz_y_salvo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PazYSalvo = null,
    [property: JsonPropertyName("comparendos"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ComparendoDeudaDto>? Comparendos = null,
    [property: JsonPropertyName("comparendos_truncados"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ComparendosTruncados = null,
    [property: JsonPropertyName("acuerdos_pago"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AcuerdoDeudaDto>? AcuerdosPago = null,
    [property: JsonPropertyName("monto_total"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? MontoTotal = null,
    [property: JsonPropertyName("vigencias"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<VigenciaDeudaDto>? Vigencias = null,
    [property: JsonPropertyName("vence"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Vence = null);

/// <summary>
/// La persona de una fuente SIMIT de persona: tipo y número de documento completo, como el detalle de la celda (SP-11). Nunca
/// el nombre. <see cref="DocumentoEnmascarado"/> es el del Excel (<c>****</c> + 4) y no sale en el JSON.
/// </summary>
internal sealed record PersonaDeudaDto(
    [property: JsonPropertyName("tipo_documento")] string TipoDocumento,
    [property: JsonPropertyName("documento")] string Documento,
    [property: JsonIgnore] string DocumentoEnmascarado);

/// <summary>Un comparendo: el valor va como texto, tal como llega (S17).</summary>
internal sealed record ComparendoDeudaDto(
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("fecha")] string? Fecha,
    [property: JsonPropertyName("valor")] string? Valor,
    [property: JsonPropertyName("estado")] string? Estado);

/// <summary>Un acuerdo de pago del SIMIT: estado y saldo pendiente como texto, tal como llega (S17).</summary>
internal sealed record AcuerdoDeudaDto(
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("saldo_pendiente")] string? SaldoPendiente);

/// <summary>Una vigencia del impuesto: año, valor en pesos y si figura pagada.</summary>
internal sealed record VigenciaDeudaDto(
    [property: JsonPropertyName("anio")] int Anio,
    [property: JsonPropertyName("valor")] long Valor,
    [property: JsonPropertyName("pagada")] bool Pagada);

/// <summary>
/// El saneamiento vigente del vehículo en el panel: solo su estado en el JSON; los renglones (sin el contenido de las evidencias)
/// van al PDF y al Excel.
/// </summary>
internal sealed record SaneamientoDeudaDto(
    [property: JsonPropertyName("estado")] string Estado)
{
    [JsonIgnore]
    public IReadOnlyList<RenglonDeudaDto> Renglones { get; init; } = [];
}

/// <summary>Un renglón del saneamiento para el PDF y el Excel: fuente, hallazgo, resultado, descripción, nota y si tiene evidencia.</summary>
internal sealed record RenglonDeudaDto(string Fuente, string Hallazgo, string Resultado, string? Descripcion, string? Nota, bool ConEvidencia);

/// <summary>Códigos de fuente del panel (los mismos de los renglones del saneamiento, plan §3.3).</summary>
internal static class FuentesDeuda
{
    public const string SimitVehiculo = "SIMIT_VEHICULO";
    public const string SimitPropietario = "SIMIT_PROPIETARIO";
    public const string SimitLocatario = "SIMIT_LOCATARIO";
    public const string Impuesto = "IMPUESTO";
    public const string Soat = "SOAT";
    public const string Rtm = "RTM";
}

/// <summary>Estado de una fuente del panel (plan §6.5).</summary>
internal static class EstadosDeuda
{
    public const string ConDeuda = "CON_DEUDA";
    public const string NoVigente = "NO_VIGENTE";
    public const string SinVerificar = "SIN_VERIFICAR";
    public const string SinHallazgo = "SIN_HALLAZGO";
    public const string NoAplica = "NO_APLICA";
}
