using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>Cuerpo de <c>POST /liquidaciones/individual</c> (esquema <c>SolicitudIndividual</c>): solo la placa (D-ENTRADA).</summary>
public sealed record SolicitudIndividualEntrada(string? Placa);

/// <summary>Esquema <c>Referencia</c>: id y nombre (vacío si la plataforma no lo da).</summary>
public sealed record ReferenciaDto(Guid Id, string Nombre);

/// <summary>Esquema <c>Contadores</c> de la cabecera.</summary>
public sealed record ContadoresDto(
    int Exitosas, int ConDeuda, int PazYSalvo, int Error, int DatosIncompletos, int DepartamentoNoHabilitado, int Incierto, int SinResultado, int Pendientes);

/// <summary>
/// Esquema <c>ResumenLiquidacion</c> (fila del grid). <see cref="Placa"/>, <see cref="ItemId"/> y <see cref="EstadoItem"/> solo en la
/// individual (delta 4 de la 1.1.0); null en la masiva. Los enums van con su código (<c>INDIVIDUAL</c>, <c>EN_PROCESO</c>).
/// </summary>
public record ResumenLiquidacionDto(
    Guid Id, string Tipo, string Estado, int TotalItems, ContadoresDto Contadores, DateTimeOffset CreadaEn, ReferenciaDto CreadaPor,
    ReferenciaDto Empresa, string? NombreArchivo, string? Placa, Guid? ItemId, string? EstadoItem);

/// <summary>Copia de configuración de la cabecera (<c>configCopia</c>; en la base <c>maxRegistros</c>, en el contrato <c>maxRegistrosLote</c>).</summary>
public sealed record ConfigCopiaDto(int MaxRegistrosLote, IReadOnlyList<string>? DepartamentosHabilitados, int Concurrencia);

/// <summary>Esquema <c>Liquidacion</c>: el resumen más fechas de terminación y cierre y la copia de configuración.</summary>
public record LiquidacionDto(
    Guid Id, string Tipo, string Estado, int TotalItems, ContadoresDto Contadores, DateTimeOffset CreadaEn, ReferenciaDto CreadaPor,
    ReferenciaDto Empresa, string? NombreArchivo, string? Placa, Guid? ItemId, string? EstadoItem,
    DateTimeOffset? TerminadaEn, ReferenciaDto? CerradaPor, DateTimeOffset? CerradaEn, ConfigCopiaDto ConfigCopia)
    : ResumenLiquidacionDto(Id, Tipo, Estado, TotalItems, Contadores, CreadaEn, CreadaPor, Empresa, NombreArchivo, Placa, ItemId, EstadoItem);

/// <summary>Esquema <c>LiquidacionConItems</c>: respuesta 201 de la individual con <c>items[0]</c>.</summary>
public sealed record LiquidacionConItemsDto(
    Guid Id, string Tipo, string Estado, int TotalItems, ContadoresDto Contadores, DateTimeOffset CreadaEn, ReferenciaDto CreadaPor,
    ReferenciaDto Empresa, string? NombreArchivo, string? Placa, Guid? ItemId, string? EstadoItem,
    DateTimeOffset? TerminadaEn, ReferenciaDto? CerradaPor, DateTimeOffset? CerradaEn, ConfigCopiaDto ConfigCopia, IReadOnlyList<ItemDto> Items)
    : LiquidacionDto(Id, Tipo, Estado, TotalItems, Contadores, CreadaEn, CreadaPor, Empresa, NombreArchivo, Placa, ItemId, EstadoItem,
        TerminadaEn, CerradaPor, CerradaEn, ConfigCopia);

/// <summary>Esquema <c>Departamento</c> del ítem.</summary>
public sealed record DepartamentoItemDto(string Codigo, string Nombre);

/// <summary>Esquema <c>Propietario</c>: completo, sin enmascarar (DA-7=b); nunca dirección, correo ni teléfono.</summary>
public sealed record PropietarioDto(string? Nombre, string? DocumentoTipo, string? DocumentoNumero);

/// <summary>Esquema <c>Item</c> (placa del lote). <see cref="TotalAPagar"/> null = «Sin dato», nunca 0.</summary>
public sealed record ItemDto(
    Guid Id, Guid LiquidacionId, int? Fila, string Placa, string Estado, string? ErrorCodigo, string? ErrorClase, string? ErrorMensaje,
    string? CampoFaltante, DepartamentoItemDto? Departamento, PropietarioDto? Propietario, decimal? TotalAPagar, string? ResultadoStatus,
    bool Parcial, bool PosibleDuplicado, bool ResultadoTardio, Guid? RelanzadoDesde);

/// <summary>Página del contrato: <c>{ items, total, page, pageSize }</c>.</summary>
public sealed record PaginaDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>
/// Errores de regla de las liquidaciones con el literal exacto de <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0): el front
/// muestra el <c>error</c> tal cual.
/// </summary>
internal static class ErroresLiquidaciones
{
    public const string PlacaInvalida = "PLACA_INVALIDA";
    public const string AprovisionamientoNoListo = "APROVISIONAMIENTO_NO_LISTO";
    public const string ProductoSuspendido = "PRODUCTO_SUSPENDIDO";
    public const string LiquidacionNoEncontrada = "LIQUIDACION_NO_ENCONTRADA";
    public const string FiltroInvalido = RangoDias.CodigoFiltroInvalido;
    public const string RangoInvalido = RangoDias.CodigoRangoInvalido;
    public const string PaginacionInvalida = "PAGINACION_INVALIDA";

    public const string LiteralPlacaInvalida = "Escribe una placa válida: de 4 a 10 letras o números.";
    public const string LiteralNoListo = "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.";
    public const string LiteralSuspendido = "Impuestos está suspendido para la compañía: solo lectura.";
    public const string LiteralNoEncontrada = "La liquidación no existe.";
    public const string LiteralFiltroInvalido = "Un filtro no es válido.";
    public const string LiteralRangoInvalido = "El rango de fechas no es válido.";
    public const string LiteralPaginacionInvalida = "La paginación no es válida.";

    public static ReglaException Placa() => new(PlacaInvalida, LiteralPlacaInvalida) { Campo = "placa" };

    public static ReglaException NoListo() => ReglaException.Conflicto(AprovisionamientoNoListo, LiteralNoListo);

    public static ReglaException Suspendido() => ReglaException.Conflicto(ProductoSuspendido, LiteralSuspendido);

    public static ReglaException NoEncontrada() => new(LiquidacionNoEncontrada, LiteralNoEncontrada, StatusCodes.Status404NotFound);

    public static ReglaException Filtro(string campo) => new(FiltroInvalido, LiteralFiltroInvalido, StatusCodes.Status400BadRequest) { Campo = campo };

    public static ReglaException Paginacion(string campo) => new(PaginacionInvalida, LiteralPaginacionInvalida, StatusCodes.Status400BadRequest) { Campo = campo };

    /// <summary>
    /// El error de <see cref="RangoDias.Leer"/> con el literal del contrato para su código (el de <c>RangoDias</c> explica el motivo
    /// técnico; el contrato manda un texto único por código).
    /// </summary>
    public static ReglaException ConLiteral(ReglaException rango)
    {
        ArgumentNullException.ThrowIfNull(rango);
        var literal = rango.Codigo == RangoInvalido ? LiteralRangoInvalido : LiteralFiltroInvalido;
        return new ReglaException(rango.Codigo, literal, StatusCodes.Status400BadRequest) { Campo = rango.Campo };
    }
}
