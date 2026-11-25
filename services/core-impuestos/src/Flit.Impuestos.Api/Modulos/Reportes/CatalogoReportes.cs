using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Reportes;

/// <summary>Enum <c>TipoReporte</c> de impuestos.v1.yaml.</summary>
public enum TipoReporte
{
    Detalle,
    Lotes,
    Deuda,
    Errores,
}

/// <summary>Esquema <c>ColumnaReporte</c>: clave de la fila y su etiqueta visible.</summary>
public sealed record ColumnaReporteDto(string Clave, string Etiqueta);

/// <summary>Un reporte del catálogo: código del contrato, nombre visible y columnas en orden (sin la de la compañía).</summary>
internal sealed record DefinicionReporte(TipoReporte Tipo, string Codigo, string Nombre, IReadOnlyList<ColumnaReporteDto> Columnas);

/// <summary>
/// Catálogo de los cuatro reportes (plan §5.1, M14; impuestos.v1.yaml 1.1.0). Nombres y etiquetas de columna salen de
/// <c>x-flit-etiquetas</c> de <c>TipoReporte</c> y de cada <c>FilaReporte*</c>, en el orden en que el contrato las lista. La columna
/// <c>empresa</c> («Compañía») va primero y solo en la vista global del Super Admin (delta 11). Las etiquetas de los enums de las
/// filas (para Excel y PDF) también son las del contrato; <see cref="MontoSinDato"/> es <c>x-flit-literales.pantalla.montoSinDato</c>
/// y <see cref="MarcaParcial"/>, <c>marcaParcial</c>. Topes de filas para exportar: Excel 5000 y PDF 1000 (<see cref="TopesExportacion"/>).
/// </summary>
/// <example>
/// <code>
/// var def = CatalogoReportes.De("deuda") ?? throw CatalogoReportes.NoEncontrado();
/// var columnas = CatalogoReportes.Columnas(def, global: true); // [empresa, placa, …]
/// </code>
/// </example>
internal static class CatalogoReportes
{
    public const string CodigoNoEncontrado = "REPORTE_NO_ENCONTRADO";
    public const string LiteralNoEncontrado = "El reporte no existe.";
    public const string MontoSinDato = "Sin dato";
    public const string MarcaParcial = "Parcial";

    public static readonly ColumnaReporteDto ColumnaEmpresa = new("empresa", "Compañía");

    public static IReadOnlyList<DefinicionReporte> Todos { get; } =
    [
        new(TipoReporte.Detalle, "detalle", "Detalle de liquidaciones",
        [
            new("tipo", "Tipo"), new("placa", "Placa"), new("estado", "Estado"), new("departamento", "Departamento"), new("propietarioNombre", "Propietario"),
            new("documentoTipo", "Tipo de documento"), new("documentoNumero", "Número de documento"), new("totalAPagar", "Total a pagar"),
            new("resultadoStatus", "Resultado"), new("parcial", "Parcial"), new("creadaEn", "Fecha"), new("usuario", "Usuario"),
        ]),
        new(TipoReporte.Lotes, "lotes", "Resumen por lote",
        [
            new("tipo", "Tipo"), new("estado", "Estado"), new("nombreArchivo", "Archivo"), new("totalItems", "Placas"), new("exitosas", "Exitosas"),
            new("conError", "Con error"), new("pendientes", "Pendientes"), new("creadaEn", "Creado"), new("terminadaEn", "Terminado"), new("usuario", "Usuario"),
        ]),
        new(TipoReporte.Deuda, "deuda", "Placas con deuda",
        [
            new("placa", "Placa"), new("departamento", "Departamento"), new("propietarioNombre", "Propietario"), new("documentoTipo", "Tipo de documento"),
            new("documentoNumero", "Número de documento"), new("vigencias", "Vigencias adeudadas"), new("totalAPagar", "Total a pagar"), new("parcial", "Parcial"),
            new("consultadaEn", "Fecha de consulta"),
        ]),
        new(TipoReporte.Errores, "errores", "Errores de liquidación",
        [
            new("placa", "Placa"), new("estado", "Estado"), new("errorCodigo", "Código de error"), new("errorClase", "Clase"), new("errorMensaje", "Mensaje del proveedor"),
            new("campoFaltante", "Dato faltante"), new("departamento", "Departamento"), new("creadaEn", "Fecha"), new("usuario", "Usuario"),
        ]),
    ];

    /// <summary>x-flit-etiquetas de <c>TipoLiquidacion</c>.</summary>
    public static IReadOnlyDictionary<string, string> EtiquetasTipo { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["INDIVIDUAL"] = "Individual",
        ["MASIVA"] = "Masiva",
    };

    /// <summary>x-flit-etiquetas de <c>EstadoLiquidacion</c> (estado del reporte de lotes).</summary>
    public static IReadOnlyDictionary<string, string> EtiquetasEstadoLote { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CREADO"] = "Creado",
        ["EN_PROCESO"] = "En proceso",
        ["COMPLETADO"] = "Completado",
        ["COMPLETADO_CON_NOVEDADES"] = "Completado con novedades",
        ["CERRADO_MANUALMENTE"] = "Cerrado manualmente",
    };

    /// <summary>x-flit-etiquetas de <c>EstadoItem</c>.</summary>
    public static IReadOnlyDictionary<string, string> EtiquetasEstadoItem { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PENDIENTE"] = "Pendiente",
        ["CONSULTANDO_RUNT"] = "Consultando RUNT",
        ["REINTENTO_PROGRAMADO"] = "Reintento programado",
        ["ENVIANDO"] = "Enviando",
        ["ENVIADO"] = "Esperando resultado",
        ["INCIERTO"] = "Incierto",
        ["INCIERTO_REQUIERE_DECISION"] = "Incierto — requiere decisión",
        ["DATOS_INCOMPLETOS"] = "Datos incompletos",
        ["DEPARTAMENTO_NO_HABILITADO"] = "Departamento no habilitado",
        ["EXITOSO"] = "Exitosa",
        ["ERROR"] = "Con error",
        ["SIN_RESULTADO_LOTE_CERRADO"] = "Sin resultado (lote cerrado)",
    };

    /// <summary>x-flit-etiquetas de <c>ResultadoStatus</c>.</summary>
    public static IReadOnlyDictionary<string, string> EtiquetasResultado { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["con_deuda"] = "Con deuda",
        ["paz_y_salvo"] = "Paz y salvo",
        ["error"] = "Con error",
    };

    /// <summary>x-flit-etiquetas de <c>ClaseError</c>.</summary>
    public static IReadOnlyDictionary<string, string> EtiquetasClaseError { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CORREGIR_DATOS"] = "Corregir datos",
        ["DEPENDE"] = "Depende del mensaje",
        ["REINTENTABLE"] = "Reintentable como nueva consulta",
    };

    /// <summary>x-flit-etiquetas de <c>CampoFaltante</c>.</summary>
    public static IReadOnlyDictionary<string, string> EtiquetasCampoFaltante { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["documentType"] = "Tipo de documento del propietario",
        ["documentNumber"] = "Número de documento del propietario",
        ["OwnerName"] = "Nombre del propietario",
        ["vehicleModel"] = "Modelo del vehículo",
        ["secretaryDepartmentCode"] = "Secretaría de tránsito sin homologar",
        ["secretaryCity"] = "Municipio de la secretaría de tránsito",
    };

    /// <summary>La definición del código del contrato (<c>detalle</c>, <c>lotes</c>, <c>deuda</c>, <c>errores</c>); null si no existe.</summary>
    public static DefinicionReporte? De(string? codigo) => Todos.FirstOrDefault(d => string.Equals(d.Codigo, codigo, StringComparison.Ordinal));

    /// <summary>Las columnas del reporte; en la vista global, «Compañía» primero (delta 11).</summary>
    public static IReadOnlyList<ColumnaReporteDto> Columnas(DefinicionReporte definicion, bool global)
    {
        ArgumentNullException.ThrowIfNull(definicion);
        return global ? [ColumnaEmpresa, .. definicion.Columnas] : definicion.Columnas;
    }

    /// <summary>La etiqueta del contrato para el valor de una columna de enum; null si la columna no es de enum.</summary>
    public static string? Etiqueta(TipoReporte tipo, string clave, string codigo)
    {
        var tabla = clave switch
        {
            "tipo" => EtiquetasTipo,
            "estado" => tipo == TipoReporte.Lotes ? EtiquetasEstadoLote : EtiquetasEstadoItem,
            "resultadoStatus" => EtiquetasResultado,
            "errorClase" => EtiquetasClaseError,
            "campoFaltante" => EtiquetasCampoFaltante,
            _ => null,
        };
        return tabla is null ? null : tabla.TryGetValue(codigo, out var etiqueta) ? etiqueta : codigo;
    }

    /// <summary>404 <c>REPORTE_NO_ENCONTRADO</c> con su literal.</summary>
    public static ReglaException NoEncontrado() => new(CodigoNoEncontrado, LiteralNoEncontrado, StatusCodes.Status404NotFound);
}
