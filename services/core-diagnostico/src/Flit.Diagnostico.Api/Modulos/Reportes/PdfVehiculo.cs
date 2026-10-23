using System.Globalization;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Reportes.Marca;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Renderizador del PDF corporativo de UN vehículo (HU #13848, F07-H3, F07-C1..C4/C9/C10): membrete FLIT (copia de la marca de
/// core-api, <see cref="FlitLetterhead"/>), encabezado en tabla (ID, lote, empresa, creador, fecha de creación en hora Colombia,
/// tipo de análisis e identificación), una sección por fuente del contenido de F07-H2 en el orden de la grilla y pie con «Generado
/// el …» y «Página X de Y». Pinta SOLO lo que trae <see cref="ContenidoReporteVehiculo"/> (R-20): sin origen, usuario manual,
/// proveedor, transacción ni tipo de error; una fuente en ERROR sale «consulta no disponible» y los documentos de persona ya vienen
/// enmascarados. Función pura de su entrada: F07-H5 (ZIP) la reutiliza con el contenido de <c>LectorContenidoReporte</c>.
/// QuestPDF con licencia Community declarada en el constructor estático (patrón de <c>FlitCoverPageGenerator</c> de core-api).
/// </summary>
/// <example>
/// <code>
/// var bytes = PdfVehiculo.Generar(contenido, reloj.GetUtcNow());
/// var nombre = PdfVehiculo.NombreArchivo(contenido.Encabezado); // diagnostico-0192ab34-TST301.pdf
/// </code>
/// </example>
internal static partial class PdfVehiculo
{
    /// <summary>Versión del diseño: entra en la huella (súbela al cambiar lo que se pinta o cómo).</summary>
    public const int VersionPlantilla = 1;

    public const string ContentType = "application/pdf";

    public const string Titulo = "Reporte de diagnóstico vehicular";

    private const string NoRegistra = "No registra";

    /// <summary>Impuesto sin vigencias registradas (HU #13864).</summary>
    public const string SinVigencias = "No registra vigencias";

    /// <summary>Títulos de sección por código de fuente (los de la grilla).</summary>
    public static readonly IReadOnlyDictionary<string, string> Titulos = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RUNT"] = "RUNT — Datos del vehículo",
        ["RUNT_PROPIETARIO"] = "RUNT — Propietarios",
        ["RUNT_LOCATARIO"] = "RUNT — Locatarios",
        ["SIMIT_VEHICULO"] = "SIMIT — Comparendos del vehículo",
        ["SIMIT_PROPIETARIO"] = "SIMIT — Comparendos de los propietarios",
        ["SIMIT_LOCATARIO"] = "SIMIT — Comparendos de los locatarios",
        ["AVALUO"] = "Avalúo comercial",
        ["IMPUESTO_DEPARTAMENTAL"] = "Impuesto vehicular departamental",
    };

    /// <summary>Nombre de cada fuente del avalúo (también para el Excel del lote, HU #13849).</summary>
    internal static readonly IReadOnlyDictionary<string, string> FuentesAvaluo = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fasecolda"] = "Fasecolda",
        ["mercado_libre"] = "Mercado Libre",
        ["base_gravable"] = "Base gravable",
    };

    private static readonly NumberFormatInfo Pesos = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberGroupSizes = [3] };

    static PdfVehiculo()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary><c>diagnostico-{8 primeros del id}-{placa o VIN}.pdf</c> (solo letras y dígitos en la identificación).</summary>
    public static string NombreArchivo(EncabezadoReporte encabezado)
    {
        ArgumentNullException.ThrowIfNull(encabezado);
        var identificacion = NoAlfanumerico().Replace(encabezado.Placa ?? encabezado.Vin ?? encabezado.Identificador, string.Empty);
        return $"diagnostico-{encabezado.DiagnosticoId.ToString()[..8]}-{(identificacion.Length > 0 ? identificacion : "vehiculo")}.pdf";
    }

    /// <summary>El PDF del contenido, con la fecha de generación <paramref name="generadoEn"/> en el pie (hora Colombia).</summary>
    public static byte[] Generar(ContenidoReporteVehiculo contenido, DateTimeOffset generadoEn)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        FuentesFlit.Registrar();
        var generado = generadoEn.ToOffset(VigenciaDelDia.OffsetColombia);
        return Document.Create(documento => documento.Page(page =>
            {
                FlitLetterhead.ApplyTo(page);
                page.Header().Element(FlitLetterhead.Header);
                page.Footer().Column(pie =>
                {
                    pie.Item().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).Row(fila =>
                    {
                        fila.RelativeItem().Text($"Generado el {Fecha(generado)} (hora Colombia)").FontSize(7).FontColor(FlitDocumentTheme.Gris);
                        fila.RelativeItem().AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(7).FontColor(FlitDocumentTheme.Gris));
                            t.Span("Página ");
                            t.CurrentPageNumber();
                            t.Span(" de ");
                            t.TotalPages();
                        });
                    });
                    pie.Item().Element(FlitLetterhead.Footer);
                });
                page.Content().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).PaddingVertical(0.4f, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(Titulo).FontFamily(FlitDocumentTheme.FontMedium).FontSize(14).FontColor(FlitDocumentTheme.PrimaryBlue);
                    col.Item().Element(c => Encabezado(c, contenido.Encabezado));
                    foreach (var seccion in contenido.Secciones)
                        col.Item().Element(c => Seccion(c, seccion));
                });
            }))
            .WithMetadata(new DocumentMetadata
            {
                Title = Titulo,
                Author = "FLIT",
                Creator = "FLIT Diagnóstico",
                Producer = "FLIT Diagnóstico",
                CreationDate = generado,
                ModifiedDate = generado,
            })
            .GeneratePdf();
    }

    private static void Encabezado(IContainer contenedor, EncabezadoReporte e) => Tabla(contenedor,
    [
        ("ID del diagnóstico", e.DiagnosticoId.ToString()),
        ("Nombre del lote", e.NombreLote),
        ("Empresa", e.Empresa),
        ("Creado por", e.UsuarioCreador),
        ("Fecha y hora de creación", $"{Fecha(e.CreadoEn)} (hora Colombia)"),
        ("Tipo de análisis", e.TipoAnalisis),
        ("Vehículo", e.Identificador),
        ("Placa", e.Placa ?? NoRegistra),
        ("VIN", e.Vin ?? NoRegistra),
    ]);

    private static void Seccion(IContainer contenedor, SeccionReporte seccion) => contenedor.Column(col =>
    {
        col.Spacing(4);
        col.Item().PaddingTop(4).BorderBottom(1).BorderColor(FlitDocumentTheme.PrimaryBlue).PaddingBottom(2)
            .Text(Titulos.GetValueOrDefault(seccion.Fuente, seccion.Fuente)).FontFamily(FlitDocumentTheme.FontMedium).FontSize(11)
            .FontColor(FlitDocumentTheme.PrimaryBlue);
        if (seccion.DatoObtenidoEn is { } dato)
            col.Item().Text($"Consultado el {Fecha(dato)} (hora Colombia)").FontSize(7).FontColor(FlitDocumentTheme.Gris);
        switch (seccion.Estado)
        {
            case EstadoSeccion.Completado when seccion.Resultado is { } resultado:
                col.Item().Element(c => Resultado(c, resultado));
                break;
            case EstadoSeccion.NoAplica:
                col.Item().Text("No aplica. " + (seccion.Leyenda ?? string.Empty));
                break;
            case EstadoSeccion.NoDisponible:
                col.Item().Text(SeccionReporte.ConsultaNoDisponible);
                break;
            case EstadoSeccion.Pendiente:
                col.Item().Text("Pendiente: la consulta aún no termina.");
                break;
        }

        if (seccion.Personas is { Count: > 0 } personas)
        {
            foreach (var persona in personas)
                col.Item().Element(c => Persona(c, persona));
        }
        else if (seccion.Personas is not null && seccion.Estado == EstadoSeccion.Completado)
        {
            col.Item().Text("Sin personas para consultar.");
        }
    });

    private static void Persona(IContainer contenedor, PersonaReporte persona) => contenedor.PaddingLeft(8).Column(col =>
    {
        col.Spacing(3);
        var rol = persona.Rol switch { "PROPIETARIO" => "Propietario", "LOCATARIO" => "Locatario", _ => persona.Rol };
        col.Item().Text($"{rol} {persona.Orden.ToString(CultureInfo.InvariantCulture)}").FontFamily(FlitDocumentTheme.FontMedium).FontSize(10);
        var filas = new List<(string, string)> { ("Documento", $"{persona.TipoDocumento} {persona.DocumentoEnmascarado}") };
        if (persona.RotuloDeclarado is { } rotulo)
            filas.Add(("Declaración", rotulo));
        if (persona.ConsultadoEn is { } consultado)
            filas.Add(("Consultado el", $"{Fecha(consultado)} (hora Colombia)"));
        col.Item().Element(c => Tabla(c, filas));
        switch (persona.Estado)
        {
            case EstadoPersonaReporte.Ok when persona.Resultado is { } resultado:
                col.Item().Element(c => Resultado(c, resultado));
                break;
            case EstadoPersonaReporte.NoDisponible:
                col.Item().Text(SeccionReporte.ConsultaNoDisponible);
                break;
            default:
                col.Item().Text("Sin resultado.");
                break;
        }
    });

    private static void Resultado(IContainer contenedor, ResultadoDetalleDto resultado)
    {
        switch (resultado)
        {
            case ResultadoRuntDetalle runt:
                Tabla(contenedor,
                [
                    ("Placa", runt.Placa ?? NoRegistra),
                    ("VIN", runt.Vin ?? NoRegistra),
                    ("Servicio", runt.Servicio ?? NoRegistra),
                    ("Clase", runt.Clase ?? NoRegistra),
                    ("Organismo de tránsito", runt.Organismo ?? NoRegistra),
                    ("Titulares", runt.Propietarios is { Count: > 0 } titulares
                        ? string.Join(", ", titulares.Select(t => $"{t.TipoDocumento ?? NoRegistra} ({t.TipoPropiedad ?? NoRegistra})"))
                        : NoRegistra),
                    ("Locatarios registrados", runt.Locatarios.ToString(CultureInfo.InvariantCulture)),
                ]);
                break;
            case ResultadoSimitDetalle simit:
                contenedor.Element(c => Simit(c, simit));
                break;
            case ResultadoAvaluoDetalle avaluo:
                contenedor.Element(c => Avaluo(c, avaluo));
                break;
            case ResultadoImpuestoDetalle impuesto:
                contenedor.Element(c => Impuesto(c, impuesto));
                break;
            case ResultadoRuntPersonaDetalle persona:
                contenedor.Column(col =>
                {
                    col.Spacing(3);
                    col.Item().Element(c => Tabla(c,
                    [
                        ("Inscrito en el RUNT", persona.Inscrito ? "Sí" : "No"),
                        ("Estado como conductor", persona.EstadoConductor ?? NoRegistra),
                    ]));
                    if (persona.Licencias is { Count: > 0 } licencias)
                    {
                        col.Item().Element(c => Grilla(c, ["Categoría", "Estado", "Vigente hasta"],
                            [.. licencias.Select(l => new[] { l.Categoria ?? NoRegistra, l.Estado ?? NoRegistra, l.VigenteHasta ?? NoRegistra })]));
                    }
                    else
                    {
                        col.Item().Text("Sin licencias de conducción.");
                    }
                });
                break;
            case ResultadoRuesDetalle rues:
                Tabla(contenedor,
                [
                    ("Encontrada en el RUES", rues.Encontrada ? "Sí" : "No"),
                    ("Estado de la matrícula", rues.EstadoMatricula ?? NoRegistra),
                    ("Última renovación", rues.UltimaRenovacion ?? NoRegistra),
                ]);
                break;
        }
    }

    private static void Simit(IContainer contenedor, ResultadoSimitDetalle simit) => contenedor.Column(col =>
    {
        col.Spacing(3);
        col.Item().Element(c => Tabla(c,
        [
            ("Semáforo", simit.Semaforo ?? NoRegistra),
            ("Comparendos", simit.CantidadComparendos.ToString(CultureInfo.InvariantCulture)),
        ]));
        var comparendos = simit.Comparendos ?? [];
        if (comparendos.Count == 0)
        {
            col.Item().Text("Sin comparendos.");
            return;
        }

        col.Item().Element(c => Grilla(c, ["Número", "Fecha", "Valor", "Estado", "Organismo"],
            [.. comparendos.Select(k => new[]
            {
                k.Numero ?? NoRegistra, k.Fecha ?? NoRegistra, ValorTexto(k.Valor), k.Estado ?? NoRegistra, k.Organismo ?? NoRegistra,
            })]));
        var restantes = simit.CantidadComparendos - comparendos.Count;
        if (simit.ComparendosTruncados || restantes > 0)
            col.Item().Text($"y {Math.Max(restantes, 0).ToString(CultureInfo.InvariantCulture)} más").FontColor(FlitDocumentTheme.Gris);
    });

    private static void Avaluo(IContainer contenedor, ResultadoAvaluoDetalle avaluo) => contenedor.Column(col =>
    {
        col.Spacing(3);
        if (avaluo.SinValorComercial)
        {
            col.Item().Text("Sin valor comercial");
        }
        else
        {
            col.Item().Element(c => Tabla(c,
            [
                ("Valor sugerido", avaluo.ValorSugerido is { } valor ? Moneda(valor) : NoRegistra),
                ("Fuente principal", NombreFuenteAvaluo(avaluo.FuentePrincipal)),
            ]));
        }

        if (avaluo.Fuentes is { Count: > 0 } fuentes)
        {
            col.Item().Element(c => Grilla(c, ["Fuente", "Estado", "Valor", "Muestras", "Fecha"],
                [.. fuentes.Select(f => new[]
                {
                    NombreFuenteAvaluo(f.Fuente), f.Estado ?? NoRegistra, f.Valor is { } v ? Moneda(v) : NoRegistra,
                    f.Muestras?.ToString(CultureInfo.InvariantCulture) ?? NoRegistra, f.Fecha ?? NoRegistra,
                })]));
        }
    });

    /// <summary>
    /// Impuesto departamental (HU #13864): «Total adeudado» con el formato de moneda del avalúo y la grilla Vigencia / Valor / Estado
    /// («Pagada» o «Pendiente») por año; sin vigencias, «No registra vigencias».
    /// </summary>
    private static void Impuesto(IContainer contenedor, ResultadoImpuestoDetalle impuesto) => contenedor.Column(col =>
    {
        col.Spacing(3);
        col.Item().Element(c => Tabla(c, [("Total adeudado", Moneda(impuesto.MontoTotal))]));
        if (impuesto.Vigencias is { Count: > 0 } vigencias)
        {
            col.Item().Element(c => Grilla(c, ["Vigencia", "Valor", "Estado"],
                [.. vigencias.Select(v => new[] { v.Anio.ToString(CultureInfo.InvariantCulture), Moneda(v.Valor), v.Pagada ? "Pagada" : "Pendiente" })]));
        }
        else
        {
            col.Item().Text(SinVigencias);
        }
    });

    /// <summary>Tabla de dos columnas: etiqueta y valor.</summary>
    private static void Tabla(IContainer contenedor, IReadOnlyList<(string Etiqueta, string Valor)> filas) => contenedor.Table(tabla =>
    {
        tabla.ColumnsDefinition(c =>
        {
            c.RelativeColumn(2);
            c.RelativeColumn(5);
        });
        foreach (var (etiqueta, valor) in filas)
        {
            tabla.Cell().Element(Etiqueta).Text(etiqueta).FontFamily(FlitDocumentTheme.FontMedium);
            tabla.Cell().Element(Celda).Text(valor);
        }
    });

    /// <summary>Tabla con encabezado de columnas y una fila por elemento.</summary>
    private static void Grilla(IContainer contenedor, IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas) => contenedor.Table(tabla =>
    {
        tabla.ColumnsDefinition(c =>
        {
            foreach (var _ in columnas)
                c.RelativeColumn();
        });
        tabla.Header(h =>
        {
            foreach (var columna in columnas)
                h.Cell().Element(Etiqueta).Text(columna).FontFamily(FlitDocumentTheme.FontMedium);
        });
        foreach (var fila in filas)
        {
            foreach (var valor in fila)
                tabla.Cell().Element(Celda).Text(valor);
        }
    });

    private static IContainer Etiqueta(IContainer c) =>
        c.Border(0.5f).BorderColor(FlitDocumentTheme.Gris).Background(FlitDocumentTheme.FondoEtiqueta).Padding(3);

    private static IContainer Celda(IContainer c) => c.Border(0.5f).BorderColor(FlitDocumentTheme.Gris).Padding(3);

    private static string Fecha(DateTimeOffset instante) =>
        instante.ToOffset(VigenciaDelDia.OffsetColombia).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Moneda(long valor) => "$ " + valor.ToString("#,0", Pesos);

    private static string ValorTexto(string? valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var pesos)
            ? "$ " + pesos.ToString("#,0.##", Pesos)
            : valor ?? NoRegistra;

    private static string NombreFuenteAvaluo(string? fuente) =>
        fuente is null ? NoRegistra : FuentesAvaluo.GetValueOrDefault(fuente, fuente);

    [GeneratedRegex("[^A-Za-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NoAlfanumerico();
}

/// <summary>
/// Descarga del PDF de un vehículo para la compañía (HU #13848): visibilidad de <see cref="AccesoLote"/> (404 si el lote no se ve o
/// el vehículo no es de ese lote), regla única de emisión <see cref="EmisionPdf"/> (409 <c>VEHICULO_NO_TERMINADO</c>), contenido de
/// <see cref="LectorContenidoReporte"/> con la empresa del claim <c>company_name</c>, huella del vehículo
/// (<see cref="HuellaEvidencia"/> con <see cref="PdfVehiculo.VersionPlantilla"/>) y registro de <see cref="ServicioEvidencias"/>: con
/// la misma huella entrega los bytes guardados sin regenerar; si cambió una celda, genera uno nuevo e invalida el anterior.
/// </summary>
internal sealed class DescargaPdfVehiculo(
    DiagnosticoDb db, UsuarioActual usuario, LectorContenidoReporte lector, ServicioEvidencias evidencias, TimeProvider reloj)
{
    public async Task<ArchivoGenerado> ObtenerAsync(Guid diagnosticoId, Guid vehiculoId, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        var estados = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == cabecera.Id && c.VehiculoId == vehiculoId)
            .Select(c => c.Estado)
            .ToListAsync(ct).ConfigureAwait(false);
        if (!EmisionPdf.Puede(cabecera, estados))
        {
            // Un vehículo de otro lote (u otra compañía) no tiene celdas en este: 404, no 409.
            if (!await db.Set<VehiculoDiagnostico>().AnyAsync(v => v.Id == vehiculoId && v.DiagnosticoId == cabecera.Id, ct).ConfigureAwait(false))
                throw ReglaException.NoEncontrado("El vehículo");
            EmisionPdf.Exigir(cabecera, estados);
        }

        var contenido = await lector.VehiculoAsync(cabecera, vehiculoId, usuario.EmpresaNombre, ct).ConfigureAwait(false);
        var clave = ClaveEvidencia.Pdf(cabecera.Id, vehiculoId);
        var entrada = await HuellaEvidencia.LeerEntradaAsync(db, clave, PdfVehiculo.VersionPlantilla, contenido.ParaHuella(), ct).ConfigureAwait(false);
        var nombre = PdfVehiculo.NombreArchivo(contenido.Encabezado);
        var entregada = await evidencias.ObtenerOGenerarAsync(
            clave, HuellaEvidencia.Calcular(entrada), usuario.Id,
            _ => Task.FromResult(new ArchivoGenerado(PdfVehiculo.Generar(contenido, reloj.GetUtcNow()), nombre, PdfVehiculo.ContentType)),
            ct).ConfigureAwait(false);
        return new ArchivoGenerado(entregada.Contenido, nombre, PdfVehiculo.ContentType);
    }
}
