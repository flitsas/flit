using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Reportes.Marca;
using Flit.Diagnostico.Api.Modulos.Tablero;
using FluentAssertions;
using QuestPDF.Fluent;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// Ajustes visuales del PDF del vehículo (oleada «ajustes-pdf-diagnostico», registro diferido 2026-11-18): la cabecera solo trae
/// Diagnóstico ID, Placa y Fecha de generación, con el ID en una línea; la NOTA pasa a un pie compacto «NOTA: …» en todas las
/// páginas; las celdas sin dato dicen «No hay datos»; las grillas reparten el ancho para que ninguna palabra quede más ancha que su
/// celda; las tablas de dos campos van centradas; y las fechas ISO de licencias, certificados y solicitudes de la persona salen
/// dd/MM/yyyy. Sin base de datos: genera el PDF en memoria con un contenido ficticio tipo placa LLZ319 (TST319).
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --sin-bd --filter "FullyQualifiedName~PdfVehiculoAjustesPdfTests"
/// </code>
/// </example>
public sealed class PdfVehiculoAjustesPdfTests
{
    private static readonly DateTimeOffset GeneradoEn = new(2026, 11, 12, 17, 9, 49, TimeSpan.Zero);

    private static readonly Guid Id = Guid.Parse("01a1d04b-3a74-74ad-a6c4-4a8fbb008981");

    private static readonly string[] FilasQuitadas =
        ["Nombre del lote", "Express TST319", "Tipo de análisis", "Diagnóstico Simple", "Fecha y hora de creación", "Creado por"];

    private static readonly string[] ColumnasLicencias =
        ["Número", "Categoría", "Estado", "Vencimiento", "Expedición", "Organismo", "Restricciones", "Suspensión"];

    private static readonly string[][] FilasLicencias =
    [
        ["70000051", "B1", "Activa", "27/11/2029", "27/11/2024", "DIR TTEyTTO MCPAL BARBOSA", "CONDUCIR CON LENTES", "No hay datos"],
        ["70000051", "C1", "Inactiva", "26/11/2017", "26/11/2014", "STRIA TTOyTTE MCPAL AMALFI", "No hay datos", "No hay datos"],
        ["70000051", "B1", "Inactiva", "26/11/2024", "26/11/2014", "STRIA TTOyTTE MCPAL AMALFI", "No hay datos", "No hay datos"],
    ];

    private static readonly string[] ColumnasSoat = ["Estado", "N.º póliza", "Vigencia", "Expedición", "Vencimiento", "Entidad"];

    private static readonly string[][] FilasSoat =
    [
        ["Vigente", "SOAT-0004", "03/02/2026", "02/02/2026", "02/02/2027", "COMPAÑIA FICTICIA DE SEGUROS S A"],
        ["No vigente", "SOAT-0003", "28/01/2025", "27/01/2025", "27/01/2026", "COMPAÑIA FICTICIA DE SEGUROS S A"],
        ["No vigente", "SOAT-0002", "26/10/2023", "02/10/2023", "25/10/2024", "SEGUROS GENERALES FICTICIOS S.A."],
    ];

    private static readonly string[] ColumnasDatosTecnicos =
        ["N.º ejes", "Capacidad de carga", "Pasajeros sentados", "Peso bruto vehicular", "Alto", "Ancho", "Largo", "Rodaje"];

    // 1a — la cabecera deja solo Placa y Fecha y hora de generación (membrete 2026-11-19: el ID pasa al pie); lo demás sale del PDF.
    [Fact]
    public void Cabecera_solo_trae_placa_y_fecha_de_generacion()
    {
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(), GeneradoEn));

        PdfDePrueba.Tiene(texto, "Placa: TST319 Fecha y hora de generación: 12/11/2026 12:09:49 DATOS DEL VEHÍCULO")
            .Should().BeTrue($"la cabecera termina en la fecha de generación: {texto}");
        texto.Should().NotContainAny([.. FilasQuitadas.Select(PdfDePrueba.Plano)]);
    }

    // 1b — la nota es un pie compacto «NOTA: …» en cada página (sin el recuadro de antes, donde «NOTA» iba en su propia línea).
    [Fact]
    public void Nota_va_en_el_pie_de_cada_pagina_como_texto_compacto()
    {
        var contenido = Contenido();
        var paginas = PdfTextExtractor.ExtractPages(PdfVehiculo.Generar(contenido, GeneradoEn));

        paginas.Should().HaveCountGreaterThan(1);
        var nota = PdfDePrueba.Plano("NOTA: " + PdfVehiculo.TextoNota(contenido.Encabezado));
        foreach (var pagina in paginas)
            PdfTextExtractor.Flatten(pagina).Should().Contain(nota);
    }

    // D2 — en las tablas del PDF del vehículo, «No hay datos» en lugar de «No hay información».
    [Fact]
    public void Celdas_sin_dato_dicen_no_hay_datos()
    {
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(), GeneradoEn));

        PdfDePrueba.Tiene(texto, "No hay datos").Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, "No hay información").Should().BeFalse(texto);
        PdfDePrueba.Tiene(texto, "N.º ejes Capacidad de carga Pasajeros sentados Peso bruto vehicular Alto Ancho Largo Rodaje 2 No hay datos 5 1315")
            .Should().BeTrue(texto);
    }

    // 2a / auditoría 2 — licencias (8 columnas): ninguna palabra partida, Organismo en ≤ 3 líneas (2 con estos datos) y los textos
    // cortos («No hay datos», fechas, estados) en una línea.
    [Fact]
    public void Grilla_de_licencias_reparte_el_ancho_hacia_el_texto_largo()
    {
        var distribucion = CabenLasPalabras(ColumnasLicencias, FilasLicencias);

        Lineas(distribucion, ColumnasLicencias, 5, "DIR TTEyTTO MCPAL BARBOSA").Should().BeLessThanOrEqualTo(2);
        Lineas(distribucion, ColumnasLicencias, 5, "STRIA TTOyTTE MCPAL AMALFI").Should().BeLessThanOrEqualTo(2);
        Lineas(distribucion, ColumnasLicencias, 6, "CONDUCIR CON LENTES").Should().BeLessThanOrEqualTo(2);
        foreach (var (columna, texto) in new[] { (3, "27/11/2029"), (4, "27/11/2024"), (2, "Inactiva"), (6, "No hay datos"), (7, "No hay datos"), (0, "70000051") })
            Lineas(distribucion, ColumnasLicencias, columna, texto).Should().Be(1, $"«{texto}» en una línea");
    }

    // 2a / auditoría 4 — datos técnicos: ninguna palabra partida y «No hay datos» en una línea en todas las columnas.
    [Fact]
    public void Grilla_de_datos_tecnicos_deja_no_hay_datos_en_una_linea()
    {
        string[][] filas = [["2", "No hay datos", "5", "1315", "No hay datos", "No hay datos", "No hay datos", "No hay datos"]];
        var distribucion = CabenLasPalabras(ColumnasDatosTecnicos, filas);
        foreach (var columna in new[] { 1, 4, 5, 6, 7 })
            Lineas(distribucion, ColumnasDatosTecnicos, columna, "No hay datos").Should().Be(1, ColumnasDatosTecnicos[columna]);
        CabenLasPalabras(ColumnasDatosTecnicos, []);
    }

    // Auditoría 3 — SOAT: el estado «No vigente» cabe en una línea.
    [Fact]
    public void Grilla_soat_deja_el_estado_en_una_linea()
    {
        var distribucion = CabenLasPalabras(ColumnasSoat, FilasSoat);
        Lineas(distribucion, ColumnasSoat, 0, "No vigente").Should().Be(1);
        distribucion.TamanoCuerpo.Should().Be(TablaFlit.TamanoTexto, "el SOAT cabe sin reducir el cuerpo");
    }

    // Auditoría 5 — un solo tamaño de encabezado en todas las grillas del PDF del vehículo.
    [Fact]
    public void Todas_las_grillas_usan_el_mismo_tamano_de_encabezado()
    {
        foreach (var (columnas, filas) in new[] { (ColumnasLicencias, FilasLicencias), (ColumnasDatosTecnicos, Array.Empty<string[]>()), (ColumnasSoat, FilasSoat), (["Vigencia", "Valor", "Estado"], [["2025", "$ 180.000", "Pagada"]]) })
            PdfVehiculo.DistribuirGrilla(columnas, filas).TamanoEncabezado.Should().Be(PdfVehiculo.TamanoEncabezadoGrilla);
        PdfVehiculo.TamanoEncabezadoGrilla.Should().BeLessThan(TablaFlit.TamanoTexto);
    }

    // CR-2 — una palabra enorme que no cabe: solo esa columna se limita; los encabezados ajenos no se parten.
    [Fact]
    public void Palabra_enorme_no_parte_los_encabezados_ajenos()
    {
        string[] columnas = ["Estado", "Documento", "Detalle"];
        var distribucion = TablaFlit.Distribuir(columnas, [["Vigente", "CC 123", new string('X', 140)]], PdfVehiculo.AnchoContenido, 8);
        var util = PdfVehiculo.AnchoContenido - (2 * TablaFlit.Separador);
        var total = distribucion.Anchos.Sum();
        for (var i = 0; i < 2; i++)
        {
            var columna = util * distribucion.Anchos[i] / total;
            columna.Should().BeGreaterThanOrEqualTo(TablaFlit.AnchoPalabraMayor(columnas[i], 8, medium: true) + (2 * distribucion.Relleno), columnas[i]);
        }
    }

    // 2a — el estimador usa las métricas reales de Poppins (Medium: «Restricciones» ≈ 6,70 em).
    [Fact]
    public void Ancho_estimado_coincide_con_las_metricas_de_poppins()
    {
        TablaFlit.AnchoTexto("Restricciones", 10, medium: true).Should().BeApproximately(67.0f, 1.5f);
        TablaFlit.AnchoTexto("27/11/2029", 10).Should().BeApproximately(51.2f, 1.5f);
        TablaFlit.AnchoPalabraMayor("CONDUCIR CON LENTES", 10).Should().Be(TablaFlit.AnchoTexto("CONDUCIR", 10));
    }

    // 3a/4b — una tabla etiqueta-valor de 2 campos va centrada (margen de media columna a cada lado); con 3 o en varios bloques, sin margen.
    [Theory]
    [InlineData(2, 1, 0.5f)]
    [InlineData(1, 1, 1f)]
    [InlineData(3, 1, 0f)]
    [InlineData(1, 3, 0f)]
    public void Tabla_de_pocos_campos_queda_centrada(int ocupadas, int bloques, float margen) =>
        TablaFlit.MargenCentrado(ocupadas, bloques).Should().Be(margen);

    // D3 — fechas ISO de la fuente a dd/MM/yyyy (fecha tal como la escribe la fuente, sin convertir de zona); lo demás tal cual.
    [Theory]
    [InlineData("2029-11-27T00:00:00.000-05:00", "27/11/2029")]
    [InlineData("2024-11-27T08:45:35.000-05:00", "27/11/2024")]
    [InlineData("2030-01-01", "01/01/2030")]
    [InlineData("03/02/2026", "03/02/2026")]
    [InlineData("2029-13-40T00:00:00", "2029-13-40T00:00:00")]
    [InlineData("SIN FECHA", "SIN FECHA")]
    [InlineData(null, "No hay datos")]
    [InlineData("  ", "No hay datos")]
    public void Fecha_corta_de_la_fuente(string? valor, string esperado) => PdfVehiculo.FechaCorta(valor).Should().Be(esperado);

    // D3 — licencias, certificados de aptitud y solicitudes de la persona con fechas ISO salen dd/MM/yyyy en el PDF.
    [Fact]
    public void Fechas_iso_de_la_persona_salen_dd_MM_yyyy()
    {
        var texto = PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(), GeneradoEn)));

        foreach (var fecha in new[] { "27/11/2029", "27/11/2024", "26/11/2017", "15/03/2021", "15/03/2031", "02/05/2023" })
            PdfDePrueba.Tiene(texto, fecha).Should().BeTrue($"«{fecha}» en el PDF: {texto}");
        texto.Should().NotContainAny("2029-11-27", "T00:00:00", "2021-03-15", "2023-05-02");
    }

    [Fact]
    public void Version_de_plantilla_sube_por_los_ajustes() => PdfVehiculo.VersionPlantilla.Should().Be(9);

    // Ampliación del usuario — la Fecha de la grilla de fuentes del avalúo sale dd/MM/yyyy.
    [Fact]
    public void Fecha_de_la_fuente_del_avaluo_sale_dd_MM_yyyy()
    {
        var avaluo = PdfDePrueba.Seccion(PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(), GeneradoEn))), "AVALÚO COMERCIAL");
        PdfDePrueba.Tiene(avaluo, "Fasecolda OK $ 55.100.000 No hay datos 12/11/2026").Should().BeTrue(avaluo);
        avaluo.Should().NotContain("2026-11-12");
    }

    // CR-1 — sin placa, la cabecera identifica por VIN (y sin VIN, por el identificador).
    [Fact]
    public void Sin_placa_la_cabecera_trae_el_vin()
    {
        var contenido = Contenido();
        var sinPlaca = contenido with { Encabezado = contenido.Encabezado with { Placa = null } };
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(sinPlaca, GeneradoEn));
        PdfDePrueba.Tiene(texto, "VIN: MBHZF63S0PG000001 Fecha y hora de generación").Should().BeTrue(texto);

        var sinVin = contenido with { Encabezado = contenido.Encabezado with { Placa = null, Vin = null, Identificador = "IDENT-0001" } };
        PdfDePrueba.Tiene(PdfDePrueba.Texto(PdfVehiculo.Generar(sinVin, GeneradoEn)), "VIN: IDENT-0001 Fecha y hora de generación").Should().BeTrue();
    }

    // CR-6 — un extremo de la suspensión en blanco sale «—»; el otro, dd/MM/yyyy.
    [Fact]
    public void Suspension_con_un_extremo_en_blanco_sale_raya()
    {
        var contenido = Contenido();
        var licencia = new LicenciaDetalle("B1", "SUSPENDIDA", "2029-11-27", "70000051", null, null, null, "  ", "2025-01-10T00:00:00.000-05:00");
        var persona = contenido.Secciones[1].Personas![0];
        var conSuspension = persona with { Resultado = new ResultadoRuntPersonaDetalle(true, "ACTIVO", [licencia]) };
        var seccion = contenido.Secciones[1] with { Personas = [conSuspension] };
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(contenido with { Secciones = [contenido.Secciones[0], seccion] }, GeneradoEn));
        PdfDePrueba.Tiene(texto, "— – 10/01/2025").Should().BeTrue(texto);
    }

    // Auditoría 1 — el resumen del avalúo y su grilla de fuentes van en la misma página; el propietario, con sus tablas y licencias.
    [Fact]
    public void Avaluo_y_propietario_no_se_parten_entre_paginas()
    {
        var paginas = PdfTextExtractor.ExtractPages(PdfVehiculo.Generar(Contenido(), GeneradoEn)).Select(PdfTextExtractor.Flatten).ToList();

        var avaluo = paginas.Single(p => p.Contains(PdfDePrueba.Plano("AVALÚO COMERCIAL"), StringComparison.Ordinal));
        avaluo.Should().Contain(PdfDePrueba.Plano("Valor sugerido")).And.Contain(PdfDePrueba.Plano("Fuente Estado Valor Muestras Fecha"));
        var propietario = paginas.Single(p => p.Contains(PdfDePrueba.Plano("Propietario 1"), StringComparison.Ordinal));
        propietario.Should().Contain(PdfDePrueba.Plano("PROPIETARIOS")).And.Contain(PdfDePrueba.Plano("Inscrito en el RUNT"))
            .And.Contain(PdfDePrueba.Plano("Número Categoría Estado Vencimiento"));
    }

    // CR-7 — los valores por defecto de TablaFlit pintan lo mismo que la forma de antes (PdfDeuda y PdfReporte no cambian): una
    // grilla sin anchos ni tamaños es idéntica byte a byte a la de columnas iguales y tamaños de siempre, y los pares sin pesos a mitad
    // y mitad. Con metadatos fijos el PDF es determinista.
    [Fact]
    public void Valores_por_defecto_de_tabla_flit_no_cambian_el_dibujo()
    {
        string[] columnas = ["Número", "Fecha", "Valor"];
        string[][] filas = [["TST-1", "2026-09-30", "$ 450.000"], ["TST-2", "2026-10-01", "$ 1.000"]];
        (string, string)[] pares = [("Placa", "TST731"), ("Generado", "14/11/2026 12:30")];

        Pdf(c => TablaFlit.Grilla(c, columnas, filas, "Sin filas")).Should().Equal(
            Pdf(c => TablaFlit.Grilla(c, columnas, filas, "Sin filas", [1, 1, 1], TablaFlit.TamanoTexto, TablaFlit.TamanoTexto, TablaFlit.RellenoCelda)));
        Pdf(c => TablaFlit.Pares(c, pares)).Should().Equal(Pdf(c => TablaFlit.Pares(c, pares, 1, 1)));
        Pdf(c => TablaFlit.Campos(c, pares)).Should().Equal(Pdf(c => TablaFlit.Campos(c, pares, centrar: false)));
        PdfDeuda.Generar(PdfDeudaTests.Panel(), GeneradoEn).Should().Equal(PdfDeuda.Generar(PdfDeudaTests.Panel(), GeneradoEn));
    }

    private static byte[] Pdf(Action<QuestPDF.Infrastructure.IContainer> tabla)
    {
        FuentesFlit.Registrar();
        var fecha = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        return QuestPDF.Fluent.GenerateExtensions.GeneratePdf(QuestPDF.Fluent.Document.Create(d => d.Page(p => p.Content().Padding(20).Element(c => tabla(c))))
            .WithMetadata(new QuestPDF.Infrastructure.DocumentMetadata { CreationDate = fecha, ModifiedDate = fecha }));
    }

    /// <summary>Cada palabra del encabezado (Medium al tamaño elegido) y de las celdas (Regular al tamaño del cuerpo) cabe en su columna.</summary>
    private static TablaFlit.DistribucionGrilla CabenLasPalabras(string[] columnas, string[][] filas)
    {
        var distribucion = PdfVehiculo.DistribuirGrilla(columnas, filas);
        distribucion.Anchos.Should().HaveCount(columnas.Length);
        distribucion.TamanoCuerpo.Should().BeInRange(TablaFlit.TamanoTexto - 1, TablaFlit.TamanoTexto);
        for (var i = 0; i < columnas.Length; i++)
        {
            var columna = Columna(distribucion, columnas.Length, i);
            var palabra = filas.Select(f => TablaFlit.AnchoPalabraMayor(f[i], distribucion.TamanoCuerpo))
                .Append(TablaFlit.AnchoPalabraMayor(columnas[i], distribucion.TamanoEncabezado, medium: true)).Max();
            columna.Should().BeGreaterThanOrEqualTo(palabra + (2 * distribucion.Relleno), $"«{columnas[i]}» cabe sin partir palabras");
        }

        return distribucion;
    }

    private static float Columna(TablaFlit.DistribucionGrilla distribucion, int columnas, int i) =>
        (PdfVehiculo.AnchoContenido - ((columnas - 1) * TablaFlit.Separador)) * distribucion.Anchos[i] / distribucion.Anchos.Sum();

    private static int Lineas(TablaFlit.DistribucionGrilla distribucion, string[] columnas, int i, string texto) =>
        TablaFlit.LineasEnColumna(texto, Columna(distribucion, columnas.Length, i), distribucion.TamanoCuerpo, distribucion.Relleno);

    /// <summary>Contenido ficticio tipo LLZ319: RUNT con detalle (datos técnicos incompletos, 4 SOAT), 1 propietario con 3 licencias ISO, certificado y solicitud, SIMIT y avalúo de 1 fuente.</summary>
    internal static ContenidoReporteVehiculo Contenido()
    {
        var dato = new DateTimeOffset(2026, 11, 12, 15, 5, 0, TimeSpan.Zero);
        var encabezado = new EncabezadoReporte(Id, "Express TST319", "Empresa Demo FLIT", "Usuario Demo",
            dato, "Diagnóstico Simple", "TST319", "PLACA", "TST319", "MBHZF63S0PG000001");
        LicenciaDetalle[] licencias =
        [
            new("B1", "ACTIVA", "2029-11-27T00:00:00.000-05:00", "70000051", "2024-11-27T08:45:35.000-05:00", "DIR TTEyTTO MCPAL BARBOSA", "CONDUCIR CON LENTES"),
            new("C1", "INACTIVA", "2017-11-26T00:00:00.000-05:00", "70000051", "2014-11-26T12:04:36.000-05:00", "STRIA TTOyTTE MCPAL AMALFI"),
            new("B1", "INACTIVA", "2024-11-26T00:00:00.000-05:00", "70000051", "2014-11-26T12:04:36.000-05:00", "STRIA TTOyTTE MCPAL AMALFI"),
        ];
        var persona = new ResultadoRuntPersonaDetalle(true, "ACTIVO", licencias,
            Certificados: [new CertificadoAptitudDetalle("B1", "VIGENTE", "2021-03-15T00:00:00.000-05:00", "2031-03-15T00:00:00.000-05:00", "CONDUCCION", "CEA-0001", "CEA FICTICIO")],
            Solicitudes: [new SolicitudConductorDetalle("EXPEDICION LICENCIA", "APROBADA", "FINALIZADO", "2023-05-02T10:00:00.000-05:00", "STRIA FICTICIA", "R-0001")]);
        var propietario = new PersonaReporte(1, "PROPIETARIO", "CC", "****0951", false, null, "RUNT_PERSONA", EstadoPersonaReporte.Ok, dato, persona);
        return new ContenidoReporteVehiculo(encabezado,
        [
            new("RUNT", "POR_VEHICULO", EstadoSeccion.Completado, null, null, dato,
                new ResultadoRuntDetalle("TST319", "MBHZF63S0PG000001", "PARTICULAR", "AUTOMOVIL", "STRIA TTOyTTE MCPAL SABANETA", [new("CC", "PROPIA")], 0), null),
            new("RUNT_PROPIETARIO", "POR_PERSONA", EstadoSeccion.Completado, null, null, dato, null, [propietario]),
            new("SIMIT_VEHICULO", "POR_VEHICULO", EstadoSeccion.Completado, null, null, dato, new ResultadoSimitDetalle("VERDE", 0, [], false), null),
            new("AVALUO", "POR_VEHICULO", EstadoSeccion.Completado, null, null, dato, new ResultadoAvaluoDetalle(false, 55100000, "fasecolda",
                [new("fasecolda", "OK", 55100000, null, "2026-11-12")]), null),
        ])
        { DetalleRunt = LectorDetalleRunt.Desde(RespuestaRunt()) };
    }

    private static ConsultarVehiculoIntempoResponse RespuestaRunt()
    {
        var vehiculo = new VehiculoIntempo
        {
            Plate = "TST319",
            Vin = "MBHZF63S0PG000001",
            InformacionGeneral = new InformacionGeneralIntempo
            {
                NoPlaca = "TST319", NoLicenciaTransito = "010000048733", TipoServicio = "PARTICULAR", Clasificacion = "AUTOMOVIL",
                ClaseVehiculo = "AUTOMOVIL", Modelo = "2023", FechaMatricula = "27/10/2022", OrganismoTransito = "STRIA TTOyTTE MCPAL SABANETA",
                Marca = "MARCA FICTICIA", Linea = "LINEA", Cilidraje = "1197", Color = "BLANCO", NoVin = "MBHZF63S0PG000001", EstadoDelVehiculo = "ACTIVO",
            },
            DatosTecnicos = new DatosTecnicosIntempo { NoEjes = "2", PasajerosSentados = "5", PesoBrutoVehicular = "1315" },
        };
        (string Estado, string Poliza, string Vigencia, string Expedicion, string Vencimiento, string Entidad)[] soat =
        [
            ("VIGENTE", "SOAT-0004", "03/02/2026", "02/02/2026", "02/02/2027", "COMPAÑIA FICTICIA DE SEGUROS S A"),
            ("NO VIGENTE", "SOAT-0003", "28/01/2025", "27/01/2025", "27/01/2026", "COMPAÑIA FICTICIA DE SEGUROS S A"),
            ("NO VIGENTE", "SOAT-0002", "26/10/2023", "02/10/2023", "25/10/2024", "SEGUROS GENERALES FICTICIOS S.A."),
            ("NO VIGENTE", "SOAT-0001", "26/10/2022", "25/10/2022", "25/10/2023", "SEGUROS GENERALES FICTICIOS S.A."),
        ];
        foreach (var s in soat)
        {
            vehiculo.Soat.Add(new SoatIntempo
            {
                Estado = s.Estado, NoPoliza = s.Poliza, FechaVigencia = s.Vigencia, FechaExpedicion = s.Expedicion,
                FechaVencimiento = s.Vencimiento, EntidadExpideSoat = s.Entidad,
            });
        }

        return new ConsultarVehiculoIntempoResponse { Resultado = ResultadoRuntIntempo.Encontrado, Vehiculo = vehiculo };
    }
}
