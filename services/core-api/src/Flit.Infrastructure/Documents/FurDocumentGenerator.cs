using System.Globalization;
using System.Text;
using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Generador PDF real del Formulario Único Nacional (FUN) para matrícula inicial / traspaso y del
/// contrato de compraventa. Usa QuestPDF Community (revenue &lt; USD 1 M/año). El FUR reproduce la
/// grilla de 35 columnas del formulario oficial definida en la plantilla de referencia
/// <c>Documents/Templates/formulario-fur.html</c> mediante <see cref="GridFlow"/>, que emula el
/// algoritmo de layout de tablas HTML (colspan/rowspan) sobre el motor de tablas de QuestPDF.
/// Reemplaza a <see cref="MockFurDocumentGenerator"/> que emitía texto plano (HU #10256).
/// </summary>
public sealed class FurDocumentGenerator : IFurDocumentGenerator
{
    private const int Cols = 35;

    static FurDocumentGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateFur(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = BuildFur(data);
        return new GeneratedDocument("fur", $"fur_{SafeRef(data.ReferenceNumber)}.pdf", "application/pdf", bytes);
    }

    public GeneratedDocument GenerateCompraventa(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = BuildCompraventa(data);
        return new GeneratedDocument("compraventa", $"compraventa_{SafeRef(data.ReferenceNumber)}.pdf", "application/pdf", bytes);
    }

    // ── FUR — Formulario Único Nacional (grilla 35 columnas) ─────────────────

    private static byte[] BuildFur(FurDocumentData data)
    {
        var v = data.Vehiculo;
        var now = DateTime.UtcNow;

        var propietario = data.Partes.FirstOrDefault(p =>
                              Norm(p.Rol).Contains("PROPIETARIO") || Norm(p.Rol).Contains("VENDEDOR"))
                          ?? (data.Partes.Count > 0 ? data.Partes[0] : null);
        var comprador = data.Partes.FirstOrDefault(p => Norm(p.Rol).Contains("COMPRADOR"));
        var isTraspaso = comprador is not null
                         || Norm(data.Modalidad).Contains("TRASPASO")
                         || Norm(data.TipologiaCodigo).Contains("TRASPASO");

        var (placaLetras, placaNumeros) = SplitPlaca(v.Placa);
        var (propAp1, propAp2, propNom) = SplitName(propietario?.Nombre);
        var (compAp1, compAp2, compNom) = SplitName(comprador?.Nombre);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(0.8f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(6).FontFamily(Fonts.Arial));
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        for (var i = 0; i < Cols; i++) c.RelativeColumn(1);
                    });

                    var g = new GridFlow(table, Cols);

                    // Fila 1 — encabezado
                    g.Row();
                    g.Header("MINISTERIO DE TRANSPORTE", 19);
                    g.Header("FORMULARIO DE SOLICITUD DE TRÁMITES DEL REGISTRO NACIONAL AUTOMOTOR", 6, 2);
                    g.Header("1. ORGANISMO DE TRÁNSITO", 8);
                    g.Header("2. PLACA", 2);

                    // Fila 2
                    g.Row();
                    g.Empty(19, 3);
                    g.Label("NOMBRE", 8);
                    g.Label("LETRAS");
                    g.Label("NÚMEROS");

                    // Fila 3
                    g.Row();
                    g.Value(data.Organismo.Nombre, 6);
                    g.Label("CIUDAD", 2);
                    g.Label("CÓDIGO", 3);
                    g.Label("FECHA DE TRÁMITE", 3);
                    g.Value(placaLetras);
                    g.Value(placaNumeros, 1, 2);

                    // Fila 4
                    g.Row();
                    g.Value(data.Organismo.Ciudad, 6);
                    g.Value(data.Organismo.Codigo, 2);
                    g.Spacer(3);
                    g.Value(now.ToString("dd", CultureInfo.InvariantCulture));
                    g.Value(now.ToString("MM", CultureInfo.InvariantCulture));
                    g.Value(now.ToString("yyyy", CultureInfo.InvariantCulture));
                    g.Empty();

                    // Fila 5 — espaciador
                    g.Row();
                    g.Spacer(35);

                    // Fila 6 — secciones 3 / 5 / 6 / 7
                    g.Row();
                    g.Header("3. TRÁMITE SOLICITADO", 21);
                    g.Header("5. MARCA", 3);
                    g.Header("6. LÍNEA", 2);
                    g.Header("7. COMBUSTIBLE", 9);

                    // Fila 7 — opciones de trámite (fila 1) + marca/línea + etiquetas combustible
                    g.Row();
                    g.Label("1");
                    g.Label("MATRÍCULA/REGISTRO", 2);
                    g.Mark(!isTraspaso, 1, 7);
                    g.Label("2");
                    g.Label("TRASPASO", 2);
                    g.Mark(isTraspaso, 1, 7);
                    g.Label("3");
                    g.Label("TRASLADO MATRÍCULA / REGISTRO", 3);
                    g.Mark(false, 1, 7);
                    g.Label("4");
                    g.Label("RADICADO MATRÍCULA / REGISTRO");
                    g.Mark(false, 1, 5);
                    g.Label("5");
                    g.Label("CAMBIO DE COLOR");
                    g.Mark(false, 1, 5);
                    g.Label("6");
                    g.Label("CAMBIO DE SERVICIO");
                    g.Value(v.Marca, 3, 2);
                    g.Value(v.Linea, 2, 2);
                    g.Label("GASOLINA", 2);
                    g.Label("DIESEL");
                    g.Label("GAS.");
                    g.Label("MIXTO");
                    g.Label("ELÉCTRICO");
                    g.Label("HIDRÓG.");
                    g.Label("ETANOL");
                    g.Label("BIODIESEL");

                    // Fila 8 — marcas X de combustible
                    g.Row();
                    g.Mark(IsOpt(v.Combustible, "GASOLINA"), 2);
                    g.Mark(IsOpt(v.Combustible, "DIESEL"));
                    g.Mark(IsOpt(v.Combustible, "GAS"));
                    g.Mark(IsOpt(v.Combustible, "MIXTO"));
                    g.Mark(IsOpt(v.Combustible, "ELECTRICO"));
                    g.Mark(IsOpt(v.Combustible, "HIDROGENO"));
                    g.Mark(IsOpt(v.Combustible, "ETANOL"));
                    g.Mark(IsOpt(v.Combustible, "BIODIESEL"));

                    // Fila 9 — opciones de trámite (fila 2) + 8.colores/9.modelo/10.cilindrada (labels)
                    g.Row();
                    g.Label("7");
                    g.Label("REGRABAR MOTOR", 2);
                    g.Label("8");
                    g.Label("REGRABAR CHASIS", 2);
                    g.Label("9");
                    g.Label("TRANSFORMACIÓN", 3);
                    g.Label("10");
                    g.Label("DUPLICADO LICENCIA TRÁNSITO");
                    g.Label("11");
                    g.Label("INSCRIPC. PRENDA");
                    g.Label("12");
                    g.Label("LEVANTA. PRENDA");
                    g.Label("8. COLORES");
                    g.Spacer(4);
                    g.Label("9. MODELO", 2);
                    g.Label("10. CILINDRADA", 3);

                    // Fila 10 — valores colores/modelo/cilindrada
                    g.Row();
                    g.Value(v.Color, 5, 2);
                    g.Value(v.Modelo, 2, 2);
                    g.Value(v.Cilindraje, 3, 2);

                    // Fila 11 — continuación rowspan
                    g.Row();

                    // Fila 12 — opciones de trámite (fila 3) + etiquetas 11/12/13/14
                    g.Row();
                    g.Label("13");
                    g.Label("CANCELACIÓN MATRÍCULA / REGISTRO", 2);
                    g.Label("14");
                    g.Label("CAMBIO DE PLACAS", 2);
                    g.Label("15");
                    g.Label("DUPLICADO DE PLACAS", 3);
                    g.Label("16");
                    g.Label("REMATRÍCULA");
                    g.Empty();
                    g.Label("17");
                    g.Label("CAMBIO DE CARROCERÍA");
                    g.Empty();
                    g.Label("18");
                    g.Label("OTROS");
                    g.Label("11. CAPACIDAD Kg/Psj", 3);
                    g.Label("12. BLINDAJE SI NO", 4);
                    g.Label("13. DESMONTE BLIND. SI NO", 4);
                    g.Label("14. POTENCIA/HP", 3);

                    // Fila 13 — valores capacidad/potencia
                    g.Row();
                    g.Empty();
                    g.Empty();
                    g.Value(v.Capacidad, 3);
                    g.Label("RESOLUCIÓN No (DD/MM/AÑO)", 4);
                    g.Label("RESOLUCIÓN No (DD/MM/AÑO)", 4);
                    g.Value(null, 3); // potencia no capturada

                    // Fila 14 — espaciador
                    g.Row();
                    g.Spacer(35);

                    // Fila 15 — secciones 4 / 15 / 16
                    g.Row();
                    g.Header("4. CLASE DE VEHÍCULO", 21);
                    g.Header("15. CARROCERÍA", 7);
                    g.Header("16. IDENTIFICACIÓN INTERNA DEL VEHÍCULO", 7);

                    // Fila 16 — clase (etiquetas fila 1) + código + No. motor + regrabado
                    g.Row();
                    g.Label("AUTOMÓVIL", 3);
                    g.Label("BUS", 4);
                    g.Label("BUSETA", 4);
                    g.Label("CAMIÓN", 3);
                    g.Label("CAMIONETA");
                    g.Label("CAMPERO", 3);
                    g.Label("MICROBÚS", 3);
                    g.Label("CÓDIGO", 7);
                    g.Label("No. DE MOTOR", 5);
                    g.Label("REGRABADO", 2);

                    // Fila 17 — clase (X fila 1) + valores
                    g.Row();
                    g.Mark(MatchClase(v.Clase, "AUTOMÓVIL"), 3);
                    g.Mark(MatchClase(v.Clase, "BUS"), 4);
                    g.Mark(MatchClase(v.Clase, "BUSETA"), 4);
                    g.Mark(MatchClase(v.Clase, "CAMIÓN"), 3);
                    g.Mark(MatchClase(v.Clase, "CAMIONETA"));
                    g.Mark(MatchClase(v.Clase, "CAMPERO"), 3);
                    g.Mark(MatchClase(v.Clase, "MICROBÚS"), 3);
                    g.Value(null, 7); // código de carrocería no capturado
                    g.Value(v.NumeroMotor, 5);
                    g.Label("SI    NO", 2);

                    // Fila 18 — clase (etiquetas fila 2) + tipo + No. chasis + regrabado
                    g.Row();
                    g.Label("TRACTOCAMIÓN", 3);
                    g.Label("MOTOCICLETA", 4);
                    g.Label("MOTOCARRO", 4);
                    g.Label("MOTOTRICICLO", 3);
                    g.Label("CUATRIMOTO");
                    g.Label("VOLQUETA", 3);
                    g.Label("OTRO", 3);
                    g.Label("TIPO", 7);
                    g.Label("No. DE CHASIS", 5);
                    g.Label("REGRABADO", 2);

                    // Fila 19 — clase (X fila 2) + valores
                    g.Row();
                    g.Mark(MatchClase(v.Clase, "TRACTOCAMIÓN"), 3);
                    g.Mark(MatchClase(v.Clase, "MOTOCICLETA"), 4);
                    g.Mark(MatchClase(v.Clase, "MOTOCARRO"), 4);
                    g.Mark(MatchClase(v.Clase, "MOTOTRICICLO"), 3);
                    g.Mark(MatchClase(v.Clase, "CUATRIMOTO"));
                    g.Mark(MatchClase(v.Clase, "VOLQUETA"), 3);
                    g.Mark(MatchClase(v.Clase, "OTRO"), 3);
                    g.Value(v.TipoCarroceria, 7, 2);
                    g.Value(v.NumeroChasis, 5, 2);
                    g.Label("SI    NO", 2, 2);

                    // Fila 20 — relleno
                    g.Row();
                    g.Empty(3);
                    g.Empty(4);
                    g.Empty(4);
                    g.Empty(10);

                    // Fila 21 — sección 21 propietario + No. de serie
                    g.Row();
                    g.Header("21. DATOS DEL PROPIETARIO", 21, 2);
                    g.Empty(7);
                    g.Label("No. DE SERIE", 5);
                    g.Label("REGRABADO", 2);

                    // Fila 22
                    g.Row();
                    g.Header("17. IMPORTACIÓN O REMATE", 7);
                    g.Value(v.NumeroSerie, 5);
                    g.Label("SI    NO", 2);

                    // Fila 23 — apellidos/nombres (labels) + importación/remate + VIN
                    g.Row();
                    g.Label("PRIMER APELLIDO", 8);
                    g.Label("SEGUNDO APELLIDO", 7);
                    g.Label("NOMBRES", 6);
                    g.Label("IMPORTACIÓN", 2);
                    g.Label("REMATE", 5);
                    g.Label("No. DE VIN VEHÍCULOS AUTOMOTORES", 7);

                    // Fila 24 — valores apellidos/nombres + cajas importación + VIN
                    g.Row();
                    g.Value(propietario is null ? null : propAp1, 8);
                    g.Value(propietario is null ? null : propAp2, 7);
                    g.Value(propietario is null ? null : propNom, 6);
                    g.Label("MANIF. O ACTA 1", 1, 2);
                    g.Label("DEC. DE IMPOR. 2", 1, 2);
                    g.Label("ACTA 3", 1, 2);
                    g.Label("ENTIDAD 4", 1, 2);
                    g.Label("LUGAR (CIUDAD) 5", 1, 2);
                    g.Label("CÓDIGO 6", 2, 2);
                    g.Value(v.Vin, 7, 2);

                    // Fila 25 — tipos de documento (labels)
                    g.Row();
                    g.Label("C.C", 2);
                    g.Label("NIT");
                    g.Label("N.N", 3);
                    g.Label("PASAPORTE", 3);
                    g.Label("C.EXTRANJ.", 2);
                    g.Label("T.IDENTI.", 3);
                    g.Label("NUIP");
                    g.Label("C. DIPLOMÁTICO", 3);
                    g.Label("No. DOCUMENTO", 3);

                    // Fila 26 — X tipo doc propietario + No. documento + FECHA
                    g.Row();
                    g.Mark(propietario is not null, 2, 2); // C.C por defecto
                    g.Mark(false, 1, 2);
                    g.Mark(false, 3, 2);
                    g.Mark(false, 3, 2);
                    g.Mark(false, 2, 2);
                    g.Mark(false, 3, 2);
                    g.Mark(false, 1, 2);
                    g.Mark(false, 3, 2);
                    g.Value(propietario?.Documento, 3);
                    g.Label("FECHA", 4);
                    g.Empty(7);

                    // Fila 27 — DIA/MES/AÑO + 18. tipo de servicio
                    g.Row();
                    g.Empty(3);
                    g.Empty(3, 3);
                    g.Label("DÍA");
                    g.Label("MES");
                    g.Label("AÑO", 2);
                    g.Header("18. TIPO DE SERVICIO", 7);

                    // Fila 28 — dirección/ciudad/teléfono + servicio (labels)
                    g.Row();
                    g.Label("DIRECCIÓN");
                    g.Value(null, 11, 1, left: true); // dirección no capturada
                    g.Label("CIUDAD");
                    g.Value(null, 5); // ciudad no capturada
                    g.Label("TELÉFONO");
                    g.Value(null, 2); // teléfono no capturado
                    g.Empty(1, 2);
                    g.Empty(1, 2);
                    g.Empty(2, 2);
                    g.Label("PARTICUL");
                    g.Label("PÚBLICO");
                    g.Label("DIPLOMÁT.");
                    g.Label("OFICIAL");
                    g.Label("ESPECIAL");
                    g.Label("OTROS", 2);

                    // Fila 29 — X tipo de servicio
                    g.Row();
                    g.Empty(21);
                    g.Mark(IsOpt(v.TipoServicio, "PARTICULAR"));
                    g.Mark(IsOpt(v.TipoServicio, "PUBLICO"));
                    g.Mark(IsOpt(v.TipoServicio, "DIPLOMATICO"));
                    g.Mark(IsOpt(v.TipoServicio, "OFICIAL"));
                    g.Mark(IsOpt(v.TipoServicio, "ESPECIAL"));
                    g.Mark(IsOpt(v.TipoServicio, "OTROS"), 2);

                    // Fila 30 — firma propietario + 20. datos de alerta
                    g.Row();
                    g.Header("FIRMA DEL PROPIETARIO", 21);
                    g.Header("20. DATOS DE ALERTA", 7);

                    // Fila 31 — cajas de alerta + 19. empresa vinculadora
                    g.Row();
                    g.Empty(21);
                    g.Label("HURTO 1", 1, 4);
                    g.Label("LIM. PROPIEDAD 2", 2, 4);
                    g.Label("EMBARGO 3", 1, 4);
                    g.Label("OTRO 4", 1, 4);
                    g.Label("A FAVOR DE: 5", 2, 4);
                    g.Header("19. EMPRESA VINCULADORA", 7);

                    // Fila 32 — nombre empresa vinculadora + NIT
                    g.Row();
                    g.Empty(21);
                    g.Label("NOMBRE");
                    g.Value(null, 4); // empresa vinculadora no capturada
                    g.Label("NIT", 2, 4);

                    // Fila 33 — sección 22 comprador
                    g.Row();
                    g.Header("22. DATOS DEL COMPRADOR (TRASPASO)", 21, 2);
                    g.Empty(5);

                    // Fila 34
                    g.Row();
                    g.Empty(5);

                    // Fila 35 — apellidos/nombres comprador (labels) + observaciones
                    g.Row();
                    g.Label("PRIMER APELLIDO", 8);
                    g.Label("SEGUNDO APELLIDO", 7);
                    g.Label("NOMBRES", 6);
                    g.Empty(14);

                    // Fila 36 — valores comprador + 23. observaciones
                    g.Row();
                    g.Value(comprador is null ? null : compAp1, 8);
                    g.Value(comprador is null ? null : compAp2, 7, 3);
                    g.Value(comprador is null ? null : compNom, 6, 3);
                    g.Header("23. OBSERVACIONES", 14);

                    // Fila 37 — guía observaciones
                    g.Row();
                    g.Empty(8);
                    g.Label("ESPECIFIQUE LA PALABRA OTRO Y LA TRANSFORMACIÓN EFECTUADA AL VEHÍCULO, AMPLÍE EL TIPO DE ALERTA O LO QUE ESTIME.", 14, 1, left: true);

                    // Fila 38 — texto observaciones
                    g.Row();
                    g.Empty(8);
                    g.Value(data.Causal, 14, 1, left: true);

                    // Fila 39 — tipos de documento comprador (labels)
                    g.Row();
                    g.Label("C.C", 2);
                    g.Label("NIT");
                    g.Label("N.N", 3);
                    g.Label("PASAPORTE", 3);
                    g.Label("C.EXTRANJ.", 2);
                    g.Label("T.IDENTI.", 3);
                    g.Label("NUIP");
                    g.Label("C. DIPLOMÁTICO", 3);
                    g.Label("No. DOCUMENTO", 3);
                    g.Empty(14);

                    // Fila 40 — X tipo doc comprador + No. documento
                    g.Row();
                    g.Mark(comprador is not null, 2, 2); // C.C por defecto
                    g.Mark(false, 1, 2);
                    g.Mark(false, 3, 2);
                    g.Mark(false, 3, 2);
                    g.Mark(false, 2, 2);
                    g.Mark(false, 3, 2);
                    g.Mark(false, 1, 2);
                    g.Mark(false, 3, 2);
                    g.Value(comprador?.Documento, 3, 2);
                    g.Empty(14, 2);

                    // Fila 41 — continuación rowspan
                    g.Row();

                    // Fila 42 — dirección/ciudad/teléfono comprador + observaciones traspaso
                    g.Row();
                    g.Label("DIRECCIÓN");
                    g.Value(null, 11, 1, left: true);
                    g.Label("CIUDAD");
                    g.Value(null, 5);
                    g.Label("TELÉFONO");
                    g.Value(null, 2);
                    g.Label("OBSERVACIONES (PARA TRASPASO DE VEHÍCULOS AUTOMOTORES ANTES DE RUNT)", 14, 1, left: true);

                    // Fila 43 — nota traspaso antes de RUNT
                    g.Row();
                    g.Empty(21);
                    g.Label("SI SU VEHÍCULO FUE MATRICULADO ANTES DEL RUNT, TRANSCRIBA EL TIPO DE CARROCERÍA Y LA CLASE DE VEHÍCULO REGISTRADA EN SU LICENCIA DE TRÁNSITO O CUALQUIER OTRO ASPECTO QUE DÉ EXACTITUD A LA INFORMACIÓN.", 14, 2, left: true);

                    // Fila 44 — firma del comprador
                    g.Row();
                    g.Header("FIRMA DEL COMPRADOR", 21);

                    // Fila 45
                    g.Row();
                    g.Empty(21);
                    g.Empty(14);

                    // Fila 46
                    g.Row();
                    g.Empty(21);
                    g.Empty(14, 2);

                    // Fila 47
                    g.Row();
                    g.Empty(21);

                    // Fila 48
                    g.Row();
                    g.Empty(21);
                    g.Empty(14, 3);

                    // Fila 49 — nota respaldo
                    g.Row();
                    g.Header("NOTA: VER INSTRUCCIONES AL RESPALDO", 21, 2);

                    // Fila 50 — continuación rowspan
                    g.Row();

                    if (data.SellosFirma.Count > 0)
                    {
                        g.Row();
                        g.Value("Sellos de firma electrónica: " + string.Join("  ·  ", data.SellosFirma), 35, 1, left: true);
                    }
                });
            });

            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(0.8f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(7).FontFamily(Fonts.Arial));
                page.Content().Element(RenderInstrucciones);
            });
        }).GeneratePdf();
    }

    private static void RenderInstrucciones(IContainer container) =>
        container.Border(0.7f).Padding(6).Column(col =>
        {
            col.Item().AlignCenter().Text(txt => txt.Span("INSTRUCCIONES").Bold().FontSize(10));
            col.Item().PaddingTop(3).Text(
                "EL FORMULARIO DE SOLICITUD DE TRÁMITES DE VEHÍCULOS ES UN DOCUMENTO A TRAVÉS DEL CUAL LA PERSONA " +
                "NATURAL O JURÍDICA SOLICITA ANTE LA AUTORIDAD COMPETENTE LA REALIZACIÓN DE UN TRÁMITE.");

            foreach (var linea in Instrucciones)
                col.Item().PaddingTop(2).Text(linea);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Border(0.7f).MinHeight(90).Padding(4)
                    .Text(txt => txt.Span("IMPRONTAS DEL MOTOR O SERIE").FontSize(8).Bold());
                row.ConstantItem(10);
                row.RelativeItem().Border(0.7f).MinHeight(90).Padding(4)
                    .Text(txt => txt.Span("IMPRONTAS CHASIS O SERIAL").FontSize(8).Bold());
            });
        });

    // ── Compraventa (traspaso) ──────────────────────────────────────────────

    private static byte[] BuildCompraventa(FurDocumentData data) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Arial));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text(txt => txt.Span("CONTRATO DE COMPRAVENTA").Bold().FontSize(13));
                    col.Item().Text($"Referencia: {data.ReferenceNumber}   |   Modalidad: {data.Modalidad}");

                    col.Item().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(90);
                            c.RelativeColumn();
                        });
                        SimpleLabel(t, "Vehículo (placa)");
                        SimpleValue(t, data.Placa);
                        SimpleLabel(t, "VIN");
                        SimpleValue(t, data.Vin);
                        SimpleLabel(t, "Valor de venta");
                        SimpleValue(t, data.ValorVenta?.ToString("N2", CultureInfo.InvariantCulture));
                        SimpleLabel(t, "Causal");
                        SimpleValue(t, data.Causal);
                    });

                    col.Item().PaddingTop(6).Text(txt => txt.Span("PARTES").Bold().FontSize(10));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });
                        SimpleHeader(t, "Rol");
                        SimpleHeader(t, "Nombre");
                        SimpleHeader(t, "Documento");
                        foreach (var p in data.Partes)
                        {
                            SimpleValue(t, p.Rol);
                            SimpleValue(t, p.Nombre);
                            SimpleValue(t, p.Documento);
                        }
                    });
                });
            });
        }).GeneratePdf();

    private static void SimpleLabel(TableDescriptor table, string label) =>
        table.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(3)
             .Text(txt => txt.Span(label).FontSize(8).Bold().FontColor(Colors.Grey.Darken3));

    private static void SimpleHeader(TableDescriptor table, string label) =>
        table.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(3)
             .Text(txt => txt.Span(label).FontSize(8).Bold());

    private static void SimpleValue(TableDescriptor table, string? value) =>
        table.Cell().Border(0.5f).Padding(3).Text(Val(value));

    // ── GridFlow — emula el layout de tablas HTML (colspan/rowspan) ──────────

    /// <summary>
    /// Coloca celdas sobre una <see cref="TableDescriptor"/> de QuestPDF replicando el algoritmo de
    /// posicionamiento de tablas HTML: una matriz de ocupación detecta los slots cubiertos por
    /// rowspans previos y los colspans se recortan para no solaparse (tolerante a inconsistencias
    /// del HTML de origen). Cada celda recibe Row/Column explícitos (QuestPDF no auto-fluye con spans).
    /// </summary>
    private sealed class GridFlow(TableDescriptor table, int columns)
    {
        private readonly List<bool[]> _rows = [];
        private int _row = -1;
        private int _col;

        public void Row()
        {
            _row++;
            _col = 0;
        }

        public void Header(string text, int colSpan = 1, int rowSpan = 1) =>
            Draw(colSpan, rowSpan, border: true, bg: Colors.Grey.Lighten2,
                content: c => c.AlignCenter().AlignMiddle().Text(t => t.Span(text).Bold().FontSize(6)));

        public void Label(string text, int colSpan = 1, int rowSpan = 1, bool left = false) =>
            Draw(colSpan, rowSpan, border: true, bg: null,
                content: c => Align(c, left).Text(t => t.Span(text).Bold().FontSize(5.5f)));

        public void Value(string? value, int colSpan = 1, int rowSpan = 1, bool left = false) =>
            Draw(colSpan, rowSpan, border: true, bg: Colors.Grey.Lighten5,
                content: c => Align(c, left).Text(t => t.Span(Val(value)).Bold().FontSize(6.5f)));

        public void Mark(bool on, int colSpan = 1, int rowSpan = 1) =>
            Draw(colSpan, rowSpan, border: true, bg: null,
                content: c => c.AlignCenter().AlignMiddle().Text(t => t.Span(on ? "X" : "").Bold().FontSize(9)));

        public void Empty(int colSpan = 1, int rowSpan = 1) =>
            Draw(colSpan, rowSpan, border: true, bg: null, content: c => c.Text(""));

        public void Spacer(int colSpan = 1, int rowSpan = 1) => Place(colSpan, rowSpan);

        private static IContainer Align(IContainer c, bool left) =>
            left ? c.AlignLeft().AlignMiddle() : c.AlignCenter().AlignMiddle();

        private void Draw(int colSpan, int rowSpan, bool border, string? bg, Action<IContainer> content)
        {
            var (r, c, span) = Place(colSpan, rowSpan);
            var cell = table.Cell().Row((uint)(r + 1)).Column((uint)(c + 1)).RowSpan((uint)rowSpan).ColumnSpan((uint)span);
            IContainer styled = cell;
            if (border) styled = styled.Border(0.6f);
            if (bg is not null) styled = styled.Background(bg);
            content(styled.PaddingVertical(1).PaddingHorizontal(1.5f));
        }

        private (int Row, int Col, int Span) Place(int colSpan, int rowSpan)
        {
            var occ = RowAt(_row);
            while (_col < columns && occ[_col]) _col++;
            var start = Math.Min(_col, columns - 1);

            var maxRun = 0;
            while (maxRun < colSpan && start + maxRun < columns && !occ[start + maxRun]) maxRun++;
            var span = Math.Max(1, maxRun);

            for (var dr = 0; dr < rowSpan; dr++)
            {
                var rowOcc = RowAt(_row + dr);
                for (var dc = 0; dc < span && start + dc < columns; dc++)
                    rowOcc[start + dc] = true;
            }

            _col = start + span;
            return (_row, start, span);
        }

        private bool[] RowAt(int r)
        {
            while (_rows.Count <= r) _rows.Add(new bool[columns]);
            return _rows[r];
        }
    }

    // ── Inference helpers ─────────────────────────────────────────────────────

    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value!;

    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().ToUpperInvariant().Trim();
    }

    /// <summary>Marca una casilla por coincidencia normalizada, evitando que "GAS" capture "GASOLINA".</summary>
    private static bool IsOpt(string? value, string option)
    {
        var v = Norm(value);
        var o = Norm(option);
        if (v.Length == 0) return false;
        if (v == o) return true;
        if (o == "GAS") return v == "GAS" || v.StartsWith("GAS ") || v.Contains("GNV") || v.Contains("NATURAL");
        return v.Contains(o) || o.Contains(v);
    }

    private static bool MatchClase(string? value, string option)
    {
        var v = Norm(value);
        if (v.Length == 0) return false;
        if (Norm(option) == "OTRO")
            return ClaseConocidas.All(c => !IsOpt(value, c));
        return IsOpt(value, option);
    }

    private static (string Letras, string Numeros) SplitPlaca(string? placa)
    {
        if (string.IsNullOrWhiteSpace(placa)) return ("-", "-");
        var clean = placa.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
        var letras = new string(clean.TakeWhile(char.IsLetter).ToArray());
        var numeros = new string(clean.SkipWhile(char.IsLetter).ToArray());
        if (letras.Length == 0 || numeros.Length == 0) return (clean, "-");
        return (letras, numeros);
    }

    private static (string Ap1, string Ap2, string Nom) SplitName(string? full)
    {
        if (string.IsNullOrWhiteSpace(full)) return ("-", "-", "-");
        var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => ("-", "-", parts[0]),
            2 => (parts[0], "-", parts[1]),
            3 => (parts[0], parts[1], parts[2]),
            _ => (parts[0], parts[1], string.Join(' ', parts.Skip(2))),
        };
    }

    private static string SafeRef(string reference) =>
        System.Text.RegularExpressions.Regex.Replace(reference, @"[^\w\-.]", "-");

    private static readonly string[] ClaseConocidas =
    [
        "AUTOMÓVIL", "BUS", "BUSETA", "CAMIÓN", "CAMIONETA", "CAMPERO", "MICROBÚS",
        "TRACTOCAMIÓN", "MOTOCICLETA", "MOTOCARRO", "MOTOTRICICLO", "CUATRIMOTO", "VOLQUETA",
    ];

    private static readonly string[] Instrucciones =
    [
        "1. NO ESCRIBA EN ESTE ESPACIO, DEBE SER DILIGENCIADO POR EL ORGANISMO DE TRÁNSITO.",
        "2. ESCRIBA LAS LETRAS Y NÚMEROS DE LAS PLACAS DEL VEHÍCULO.",
        "3. SEÑALE CON UNA EQUIS (X) EL CUADRO CORRESPONDIENTE AL TRÁMITE SOLICITADO.",
        "4. SEÑALE CON UNA EQUIS (X) EL CAMPO CORRESPONDIENTE A LA CLASE DEL VEHÍCULO.",
        "5. ESPECIFIQUE LA MARCA DEL VEHÍCULO. EJEMPLO: CHEVROLET, NISSAN, RENAULT.",
        "6. ESPECIFIQUE LA LÍNEA DEL VEHÍCULO. EJEMPLO: AVEO, SENTRA, LOGAN.",
        "7. SEÑALE CON UNA EQUIS (X) EL TIPO DE COMBUSTIBLE QUE UTILIZA EL VEHÍCULO (GASOLINA, DIESEL).",
        "8. ESPECIFIQUE EL (LOS) COLOR(ES) PREDOMINANTE(S), MÁXIMO TRES.",
        "9. ESPECIFIQUE EL AÑO MODELO DEL VEHÍCULO.",
        "10. ESPECIFIQUE LA CILINDRADA DEL VEHÍCULO.",
        "11. ESPECIFIQUE LA CAPACIDAD: SI ES DE CARGA EN KILOGRAMOS, SI ES DE TRANSPORTE EN PASAJEROS.",
        "12-13. SEÑALE CON UNA EQUIS (X) BLINDAJE O DESMONTE DE BLINDAJE; EN CASO AFIRMATIVO ESPECIFIQUE LA RESOLUCIÓN.",
        "14. ESPECIFIQUE LA POTENCIA EN CABALLOS DE FUERZA (HP).",
        "15. SELECCIONE EL CÓDIGO Y EL TIPO DE CARROCERÍA QUE CORRESPONDE A SU VEHÍCULO.",
        "16. TRANSCRIBA MOTOR, CHASIS, SERIE Y VIN. SI ALGUNO FUE REGRABADO, SEÑALE CON (X) LA CASILLA RESPECTIVA.",
        "17. SI EL VEHÍCULO ES IMPORTADO O DE REMATE, INDIQUE CON (X) EL DOCUMENTO Y LA ENTIDAD RESPECTIVA.",
        "18. ESPECIFIQUE EL TIPO DE SERVICIO: PARTICULAR, PÚBLICO, DIPLOMÁTICO, OFICIAL, ESPECIAL Y OTROS.",
        "19. ESPECIFIQUE EL NOMBRE DE LA EMPRESA VINCULADORA Y SU CORRESPONDIENTE NIT.",
        "20. SEÑALE CON UNA EQUIS (X) EL DATO DE ALERTA: HURTO, LIMITACIÓN DE PROPIEDAD, EMBARGO U OTRO.",
        "21. TRANSCRIBA LOS DATOS DEL PROPIETARIO ACTUAL, SEÑALANDO CON (X) EL TIPO DE DOCUMENTO DE IDENTIDAD.",
        "22. EN CASO DE TRASPASO, TRANSCRIBA LOS DATOS DEL NUEVO PROPIETARIO Y SEÑALE CON (X) EL TIPO DE DOCUMENTO.",
        "23. OBSERVACIONES: ACLARE LA PALABRA OTRO Y DESCRIBA LA TRANSFORMACIÓN EFECTUADA AL VEHÍCULO.",
    ];
}
