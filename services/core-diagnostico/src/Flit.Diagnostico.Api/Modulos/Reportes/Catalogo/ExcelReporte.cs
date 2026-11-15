using System.Globalization;
using Flit.Diagnostico.Api.Infraestructura.Excel;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Cell = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;
using Column = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Column;
using Sheet = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Sheet;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Lo que un archivo de reporte necesita, ya registrado y descifrado (HU #14579, BE-3): las columnas y filas de la lista blanca del
/// ámbito, el filtro, el id de <c>accesos_reporte</c> y el instante de generación.
/// </summary>
internal sealed record ArchivoReporte(
    FiltroReporte Filtro,
    AlcanceReporte Alcance,
    IReadOnlyList<ColumnaReporte> Columnas,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Filas,
    Guid AccesoId,
    DateTimeOffset GeneradoEn,
    int Empresas = 1)
{
    public string NombreTipo => CatalogoReportes.De(Filtro.Tipo).Nombre;
}

/// <summary>
/// Excel del reporte (HU #14579, BE-3; SP-13, SP-15; contrato-api §5) con <see cref="TabularWorkbookWriter"/>: hoja «Aviso» (la primera)
/// con el texto Ley 1581 y el registro, «Reporte» con el aviso en la fila 1 y los encabezados en la 2, y «Filtros» con tipo, ámbito,
/// rango y enums (la placa solo como «filtro aplicado», sin su valor). Todo texto entra con <c>Cell.Of(string)</c> (texto en línea;
/// lo que parece fórmula, con quotePrefix) y el documento siempre como texto. Nombres de hoja constantes y sin vínculos.
/// </summary>
internal static class ExcelReporte
{
    public const string ContentType = TabularWorkbookWriter.ContentType;

    public static byte[] Generar(ArchivoReporte archivo)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        var registro = AvisoDatosPersonales.Registro(archivo.GeneradoEn, archivo.AccesoId);
        var aviso = new Sheet("Aviso", [new Column("Aviso de datos personales", 120)],
            [[Cell.Of(AvisoDatosPersonales.Texto)], [Cell.Of(registro)], [Cell.Of($"Reporte: {archivo.NombreTipo}")]]);
        var reporte = new Sheet(
            "Reporte",
            [.. archivo.Columnas.Select(c => new Column(c.Titulo, Ancho(c)))],
            [.. archivo.Filas.Select(f => (IReadOnlyList<Cell>)[.. archivo.Columnas.Select(c => Celda(f.GetValueOrDefault(c.Clave)))])],
            Previas: [[Cell.Of($"{AvisoDatosPersonales.Texto} {registro}")]]);
        var filtros = new Sheet("Filtros", [new Column("Filtro", 24), new Column("Valor", 60)], [.. Filtros(archivo).Select(p => (IReadOnlyList<Cell>)[Cell.Of(p.Clave), Cell.Of(p.Valor)])]);
        return TabularWorkbookWriter.Write([aviso, reporte, filtros]);
    }

    /// <summary>Pares filtro → valor del archivo (Excel y PDF). La placa nunca con su valor.</summary>
    internal static IReadOnlyList<(string Clave, string Valor)> Filtros(ArchivoReporte archivo)
    {
        var f = archivo.Filtro;
        var pares = new List<(string, string)>
        {
            ("Tipo de reporte", TiposDeReporte.Codigo(f.Tipo)),
            ("Ámbito", CodigosDeEnum.Codigo(archivo.Alcance)),
            ("Desde", f.Desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Hasta", f.Hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        };
        pares.AddRange(f.Enums.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => (e.Key, string.Join(", ", e.Value))));
        if (f.Placa is not null)
            pares.Add((CatalogoReportes.FiltroPlaca, "Filtro aplicado (el valor no se registra)"));
        pares.Add(("Filas", archivo.Filas.Count.ToString(CultureInfo.InvariantCulture)));
        return pares;
    }

    private static int Ancho(ColumnaReporte columna) => columna.Clave switch
    {
        "propietarios_documentos" or "diagnostico_nombre" or "organismo" or DefinicionReporte.EmpresaNombre => 32,
        _ => columna.Tipo == DefinicionReporte.FechaHora ? 18 : 16,
    };

    private static Cell Celda(object? valor) => valor switch
    {
        null => Cell.Empty,
        string s => Cell.Of(s),
        int n => Cell.Of(n),
        long n => Cell.Of((double)n),
        double d => Cell.Of(d),
        DateOnly d => Cell.Of(d),
        DateTimeOffset i => Instante(i),
        _ => Cell.Of(Convert.ToString(valor, CultureInfo.InvariantCulture)),
    };

    private static Cell Instante(DateTimeOffset instante)
    {
        var local = instante.ToOffset(VigenciaDelDia.OffsetColombia);
        return Cell.OfDateTime(DateOnly.FromDateTime(local.DateTime), local.Hour, local.Minute);
    }
}
