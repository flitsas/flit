using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;

/// <summary>Lo que se le pide a una fuente: el canal y la clave exacta (NIT con o sin DV, otro documento o placa).</summary>
public sealed record ClaveConsulta(CanalIngesta Canal, TipoDocumento? TipoDocumento, string Clave);

/// <summary>Un comparendo tal como lo trae una fuente, ya normalizado al modelo interno (§8).</summary>
public sealed record ItemFuente
{
    public required string Numero { get; init; }
    public string? Placa { get; init; }
    public TipoDocumento? TipoDocumentoInfractor { get; init; }
    public string? NumeroDocumentoInfractor { get; init; }
    public string? NombreInfractor { get; init; }
    public string? CodigoInfraccion { get; init; }
    public string? DescripcionInfraccion { get; init; }
    public string? DireccionInfraccion { get; init; }
    public DateOnly? FechaImposicion { get; init; }
    public TimeOnly? HoraImposicion { get; init; }
    public DateOnly? FechaNotificacion { get; init; }
    public bool? EsFotomulta { get; init; }
    public string? Secretaria { get; init; }
    public string? TipoVehiculo { get; init; }
    public string? ServicioVehiculo { get; init; }
    public decimal? ValorCapital { get; init; }
    public decimal? ValorAdicional { get; init; }
    public decimal? ValorTotal { get; init; }
    public string? EstadoFuente { get; init; }
    public string? FotoEvidenciaUrl { get; init; }

    /// <summary>Campos con valor, para fusionar repetidos (gana el más completo, S-50).</summary>
    public int CamposConValor => new object?[]
    {
        Placa, TipoDocumentoInfractor, NumeroDocumentoInfractor, NombreInfractor, CodigoInfraccion, DescripcionInfraccion,
        DireccionInfraccion, FechaImposicion, HoraImposicion, FechaNotificacion, EsFotomulta, Secretaria, TipoVehiculo,
        ServicioVehiculo, ValorCapital, ValorAdicional, ValorTotal, EstadoFuente, FotoEvidenciaUrl,
    }.Count(v => v is not null);
}

/// <summary>
/// Resultado de una consulta (o de un municipio, en la fuente municipal). Clasificación §6.3 [Excel C3]: solo una
/// respuesta 200/201 bien formada con lista vacía es <see cref="ResultadoConsulta.ExitosaVacia"/>; todo lo demás que
/// no trae datos es <see cref="ResultadoConsulta.Error"/> y no toca nada de esa clave.
/// </summary>
public sealed record ResultadoParcial(
    string? MunicipioCodigo,
    ResultadoConsulta Resultado,
    IReadOnlyList<ItemFuente> Items,
    TipoErrorFuente? TipoError = null,
    string? MensajeError = null);

public sealed record RespuestaFuente(
    IReadOnlyList<ResultadoParcial> Parciales,
    int? HttpStatus,
    int DuracionMs,
    string? CuerpoCrudo,
    string? ContentType = "application/json")
{
    public static RespuestaFuente Error(TipoErrorFuente tipo, string mensaje, int? http = null, int duracionMs = 0, string? cuerpo = null) =>
        new([new ResultadoParcial(null, ResultadoConsulta.Error, [], tipo, mensaje)], http, duracionMs, cuerpo);

    public static RespuestaFuente Exitosa(IReadOnlyList<ItemFuente> items, int http = 200, int duracionMs = 0, string? cuerpo = null) =>
        new([new ResultadoParcial(null, items.Count == 0 ? ResultadoConsulta.ExitosaVacia : ResultadoConsulta.ExitosaConDatos, items)], http, duracionMs, cuerpo);
}

/// <summary>
/// Una fuente de comparendos (SmartCloud, municipal). Las reales viven tras la plataforma (Excel S4 propuesta) y se
/// conectan al final con su información (WSDL, credenciales, esquema); mientras tanto el modo simulado sirve en DEV.
/// </summary>
public interface IFuenteComparendos
{
    bool Atiende(CanalIngesta canal);

    Task<RespuestaFuente> ConsultarAsync(ClaveConsulta clave, CancellationToken ct);
}
