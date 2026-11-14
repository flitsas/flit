using System.Globalization;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Reportes.Marca;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// PDF del panel de deuda de UN vehículo (plan §6.5, HU #14576 AC38) con el armazón de <see cref="PdfVehiculo"/>: membrete FLIT
/// (<see cref="FlitLetterhead"/>) en todas las páginas, fuentes FLIT (<see cref="FuentesFlit"/>) y tablas de <see cref="TablaFlit"/>.
/// Secciones: encabezado con placa y fecha de generación (hora Colombia); dictamen (si hay) con veredicto, versión del catálogo y
/// motivos; una tabla por fuente con su fecha de consulta; y el estado del saneamiento con sus renglones, sin el contenido ni el
/// nombre de las evidencias. Pinta solo lo que trae <see cref="PanelDeudaDto"/>: el documento de persona va completo (como
/// <c>PersonaReporte.Documento</c>, SP-11) y nunca el nombre. El valor del comparendo se pinta como llega (S17). Función pura.
/// </summary>
/// <example>
/// <code>
/// var bytes = PdfDeuda.Generar(panel, reloj.GetUtcNow());
/// var nombre = PdfDeuda.NombreArchivo(panel.Vehiculo.Placa); // deuda-TST731.pdf
/// </code>
/// </example>
internal static class PdfDeuda
{
    public const string ContentType = "application/pdf";

    public const string Titulo = "Deuda del vehículo";

    private const string SinDato = "No hay información";

    /// <summary>Texto de cada estado de fuente del panel.</summary>
    public static readonly IReadOnlyDictionary<string, string> RotulosEstado = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [EstadosDeuda.ConDeuda] = "Con deuda",
        [EstadosDeuda.NoVigente] = "No vigente",
        [EstadosDeuda.SinVerificar] = "Sin verificar",
        [EstadosDeuda.SinHallazgo] = "Sin hallazgo",
        [EstadosDeuda.NoAplica] = "No aplica",
    };

    /// <summary>Título de cada fuente del panel.</summary>
    public static readonly IReadOnlyDictionary<string, string> TitulosFuente = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [FuentesDeuda.SimitVehiculo] = "SIMIT del vehículo",
        [FuentesDeuda.SimitPropietario] = "SIMIT del propietario",
        [FuentesDeuda.SimitLocatario] = "SIMIT del locatario",
        [FuentesDeuda.Impuesto] = "Impuesto vehicular departamental",
        [FuentesDeuda.Soat] = "SOAT",
        [FuentesDeuda.Rtm] = "Revisión técnico-mecánica",
    };

    /// <summary>Texto de los estados del saneamiento (plan §7.1).</summary>
    public static readonly IReadOnlyDictionary<string, string> RotulosSaneamiento = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SOLICITADO"] = "Solicitado",
        ["EN_GESTION"] = "En gestión",
        ["SANEADO"] = "Saneado",
        ["NO_PROCEDE"] = "No procede",
        ["ANULADO"] = "Anulado",
    };

    static PdfDeuda()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary><c>deuda-{placa}.pdf</c> con la placa reducida a <c>[A-Z0-9]</c> (SP-18); sin nada utilizable, <c>deuda-vehiculo.pdf</c>.</summary>
    public static string NombreArchivo(string placa) => $"deuda-{PlacaSegura(placa)}.pdf";

    /// <summary>La placa en mayúsculas con solo <c>[A-Z0-9]</c>; «vehiculo» si no queda nada.</summary>
    internal static string PlacaSegura(string? placa)
    {
        var limpia = new StringBuilder();
        foreach (var c in (placa ?? string.Empty).ToUpperInvariant())
        {
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
                limpia.Append(c);
        }

        return limpia.Length > 0 ? limpia.ToString() : "vehiculo";
    }

    public static byte[] Generar(PanelDeudaDto panel, DateTimeOffset generadoEn)
    {
        ArgumentNullException.ThrowIfNull(panel);
        FuentesFlit.Registrar();
        var generado = generadoEn.ToOffset(VigenciaDelDia.OffsetColombia);
        return Document.Create(documento => documento.Page(page =>
            {
                FlitLetterhead.ApplyTo(page);
                page.Header().Element(FlitLetterhead.Header);
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(FlitDocumentTheme.Gris));
                    t.Span(Titulo + " · Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
                page.Content().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).PaddingVertical(0.4f, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(14);
                    col.Item().AlignCenter().Text(Titulo).Bold().FontSize(16).FontColor(FlitDocumentTheme.PrimaryBlue);
                    col.Item().Element(c => TablaFlit.Pares(c, Encabezado(panel, generado)));
                    if (panel.Dictamen is { } dictamen)
                        col.Item().Element(c => Seccion(c, "Dictamen", null, cuerpo => Dictamen(cuerpo, dictamen)));
                    foreach (var fuente in panel.Fuentes)
                        col.Item().Element(c => Seccion(c, TitulosFuente.GetValueOrDefault(fuente.Fuente, fuente.Fuente), fuente.ConsultadoEn, cuerpo => Fuente(cuerpo, fuente)));
                    col.Item().Element(c => Seccion(c, "Saneamiento", null, cuerpo => Saneamiento(cuerpo, panel.Saneamiento)));
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

    private static List<(string, string)> Encabezado(PanelDeudaDto panel, DateTimeOffset generado)
    {
        var filas = new List<(string, string)> { ("Placa", panel.Vehiculo.Placa), ("Fecha de generación", Fecha(generado)) };
        if (panel.DiagnosticoNombre is { } nombre)
            filas.Add(("Diagnóstico", nombre));
        if (panel.DiagnosticoCreadoEn is { } creado)
            filas.Add(("Fecha del diagnóstico", Fecha(creado)));
        filas.Add(("Tipo de análisis", panel.Modo == "CON_ANALISIS" ? "Diagnóstico con Análisis IA" : "Diagnóstico Simple"));
        return filas;
    }

    private static void Seccion(IContainer contenedor, string titulo, DateTimeOffset? consultado, Action<ColumnDescriptor> cuerpo) =>
        contenedor.EnsureSpace(90).Column(col =>
        {
            col.Spacing(5);
            col.Item().AlignCenter().Text(titulo.ToUpperInvariant()).Bold().FontSize(11).FontColor(FlitDocumentTheme.DarkNavy);
            if (consultado is { } d)
                col.Item().AlignCenter().Text($"Consultado el {Fecha(d)} (hora Colombia)").FontSize(7.5f).FontColor(FlitDocumentTheme.Gris);
            cuerpo(col);
        });

    private static void Dictamen(ColumnDescriptor cuerpo, DictamenDeudaDto dictamen)
    {
        cuerpo.Item().Element(c => TablaFlit.Pares(c,
        [
            ("Veredicto", RotulosDiagnostico.DeVeredicto(dictamen.Veredicto)),
            ("Versión del catálogo", dictamen.CatalogoVersion.ToString(CultureInfo.InvariantCulture)),
        ]));
        cuerpo.Item().Element(c => TablaFlit.Grilla(c, ["Motivo", "Efecto"],
            [.. dictamen.Motivos.Select(m => new[] { Texto(m.Texto), m.Efecto == "BLOQUEA" ? "Bloquea" : "Advierte" })], "Sin motivos", [4, 1]));
    }

    private static void Fuente(ColumnDescriptor cuerpo, FuenteDeudaDto fuente)
    {
        var pares = new List<(string, string)> { ("Estado", RotulosEstado.GetValueOrDefault(fuente.Estado, fuente.Estado)) };
        if (fuente.Persona is { } persona)
            pares.Add(("Documento", $"{persona.TipoDocumento} {persona.Documento}"));
        if (fuente.MotivoNa is { } motivo)
            pares.Add(("Motivo", motivo));
        if (fuente.PazYSalvo is { } paz)
            pares.Add(("Paz y salvo", paz ? "Sí" : "No"));
        if (fuente.MontoTotal is { } monto)
            pares.Add(("Monto total", Moneda(monto)));
        if (fuente.Vence is { } vence)
            pares.Add(("Vence", vence));
        cuerpo.Item().Element(c => TablaFlit.Pares(c, pares));

        if (fuente.Comparendos is { } comparendos)
        {
            cuerpo.Item().Element(c => TablaFlit.Grilla(c, ["Número", "Fecha", "Valor", "Estado"],
                [.. comparendos.Select(x => new[] { Texto(x.Numero), Texto(x.Fecha), Texto(x.Valor), Texto(x.Estado) })], "Sin comparendos"));
            if (fuente.ComparendosTruncados == true)
                cuerpo.Item().AlignCenter().Text("Se muestran los primeros 100 comparendos.").FontSize(8).FontColor(FlitDocumentTheme.Gris);
        }

        if (fuente.AcuerdosPago is { Count: > 0 } acuerdos)
        {
            cuerpo.Item().Element(c => TablaFlit.Grilla(c, ["Acuerdo de pago", "Saldo pendiente"],
                [.. acuerdos.Select(a => new[] { Texto(a.Estado), Texto(a.SaldoPendiente) })]));
        }

        if (fuente.Vigencias is { } vigencias)
        {
            cuerpo.Item().Element(c => TablaFlit.Grilla(c, ["Año", "Valor", "Pagada"],
                [.. vigencias.Select(v => new[] { v.Anio.ToString(CultureInfo.InvariantCulture), Moneda(v.Valor), v.Pagada ? "Sí" : "No" })],
                "No hay vigencias registradas"));
        }
    }

    private static void Saneamiento(ColumnDescriptor cuerpo, SaneamientoDeudaDto? saneamiento)
    {
        if (saneamiento is null)
        {
            cuerpo.Item().Element(c => TablaFlit.SinDatos(c, "Sin saneamiento"));
            return;
        }

        cuerpo.Item().Element(c => TablaFlit.Pares(c, [("Estado", RotulosSaneamiento.GetValueOrDefault(saneamiento.Estado, saneamiento.Estado))]));
        cuerpo.Item().Element(c => TablaFlit.Grilla(c, ["Fuente", "Hallazgo", "Resultado", "Nota", "Evidencia"],
            [.. saneamiento.Renglones.Select(r => new[]
            {
                TitulosFuente.GetValueOrDefault(r.Fuente, r.Fuente), r.Hallazgo, r.Resultado, Texto(r.Nota ?? r.Descripcion), r.ConEvidencia ? "Adjunta" : "Sin evidencia",
            })],
            "Sin renglones", [2, 2, 2, 3, 2]));
    }

    private static string Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? SinDato : valor;

    private static string Fecha(DateTimeOffset instante) =>
        instante.ToOffset(VigenciaDelDia.OffsetColombia).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Moneda(long valor) =>
        "$ " + valor.ToString("#,0", new NumberFormatInfo { NumberGroupSeparator = ".", NumberGroupSizes = [3] });
}
