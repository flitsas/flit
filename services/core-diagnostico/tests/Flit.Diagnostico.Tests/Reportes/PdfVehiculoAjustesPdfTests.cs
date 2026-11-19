using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Reportes.Marca;
using Flit.Diagnostico.Api.Modulos.Tablero;
using FluentAssertions;
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

    private static readonly string[] ColumnasDatosTecnicos =
        ["N.º ejes", "Capacidad de carga", "Pasajeros sentados", "Peso bruto vehicular", "Alto", "Ancho", "Largo", "Rodaje"];

    // 1a — la cabecera deja solo Diagnóstico ID, Placa y Fecha de generación; lo demás sale del PDF.
    [Fact]
    public void Cabecera_solo_trae_diagnostico_id_placa_y_fecha_de_generacion()
    {
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(), GeneradoEn));

        PdfDePrueba.Tiene(texto, $"Diagnóstico ID {Id} Placa TST319 Fecha de generación 12/11/2026 12:09:49 DATOS DEL VEHÍCULO")
            .Should().BeTrue($"la cabecera termina en la fecha de generación: {texto}");
        texto.Should().NotContainAny([.. FilasQuitadas.Select(PdfDePrueba.Plano)]);
    }

    // 1a — el GUID de 36 caracteres cabe en una línea y la tabla, junto al logo, cabe en el ancho del contenido.
    [Fact]
    public void Cabecera_el_id_y_las_etiquetas_caben_en_una_linea()
    {
        var encabezado = Contenido().Encabezado;
        var (etiqueta, valor) = PdfVehiculo.AnchosCabecera(encabezado, GeneradoEn);

        valor.Should().BeGreaterThanOrEqualTo(TablaFlit.AnchoTexto(Id.ToString(), TablaFlit.TamanoTexto) + 10, "el ID no se parte");
        etiqueta.Should().BeGreaterThanOrEqualTo(TablaFlit.AnchoTexto("Fecha de generación", TablaFlit.TamanoTexto, medium: true) + 10);
        (PdfVehiculo.AnchoLogo + etiqueta + valor).Should().BeLessThanOrEqualTo(PdfVehiculo.AnchoContenido, "logo y tabla caben en el ancho");
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

    // 2a — licencias (8 columnas): ninguna palabra del encabezado ni de las celdas queda más ancha que su columna.
    [Fact]
    public void Grilla_de_licencias_ninguna_palabra_mas_ancha_que_su_celda()
    {
        string[][] filas =
        [
            ["70000051", "B1", "Activa", "27/11/2029", "27/11/2024", "DIR TTEyTTO MCPAL BARBOSA", "CONDUCIR CON LENTES", "No hay datos"],
            ["70000051", "C1", "Inactiva", "26/11/2017", "26/11/2014", "STRIA TTOyTTE MCPAL AMALFI", "No hay datos", "No hay datos"],
        ];

        CabenLasPalabras(ColumnasLicencias, filas);
    }

    // 2a — datos técnicos (8 columnas) con celdas vacías: «Capacidad», «vehicular»… no se parten.
    [Fact]
    public void Grilla_de_datos_tecnicos_ninguna_palabra_mas_ancha_que_su_celda()
    {
        CabenLasPalabras(ColumnasDatosTecnicos, [["2", "No hay datos", "5", "1315", "No hay datos", "No hay datos", "No hay datos", "No hay datos"]]);
        CabenLasPalabras(ColumnasDatosTecnicos, []);
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
    public void Version_de_plantilla_sube_por_los_ajustes() => PdfVehiculo.VersionPlantilla.Should().Be(8);

    /// <summary>Cada palabra del encabezado (Poppins Medium al tamaño elegido) y de las celdas (Regular 9,5) cabe en su columna.</summary>
    private static void CabenLasPalabras(string[] columnas, string[][] filas)
    {
        var distribucion = TablaFlit.Distribuir(columnas, filas, PdfVehiculo.AnchoContenido);
        distribucion.Anchos.Should().HaveCount(columnas.Length);
        distribucion.TamanoEncabezado.Should().BeInRange(7f, TablaFlit.TamanoTexto);
        var util = PdfVehiculo.AnchoContenido - ((columnas.Length - 1) * TablaFlit.Separador);
        var total = distribucion.Anchos.Sum();
        for (var i = 0; i < columnas.Length; i++)
        {
            var columna = util * distribucion.Anchos[i] / total;
            var palabra = filas.Select(f => TablaFlit.AnchoPalabraMayor(f[i], TablaFlit.TamanoTexto))
                .Append(TablaFlit.AnchoPalabraMayor(columnas[i], distribucion.TamanoEncabezado, medium: true)).Max();
            columna.Should().BeGreaterThanOrEqualTo(palabra + 10, $"«{columnas[i]}» cabe sin partir palabras");
        }
    }

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
