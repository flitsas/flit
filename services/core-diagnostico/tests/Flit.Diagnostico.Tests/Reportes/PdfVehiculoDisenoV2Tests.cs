using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// Diseño v2 del PDF del vehículo con el detalle del RUNT (ajuste Caso A, registro diferido 2026-10-27): tras «INFORMACIÓN DEL
/// VEHÍCULO» (con el layout del diseño: licencia de tránsito, clasificación, modelo, fecha de matrícula y organismo a dos columnas)
/// salen, en orden, INFORMACIÓN GENERAL, DATOS TEÓRICOS, DATOS SOAT, DATOS REVISIÓN TECNO-MECÁNICA, SOLICITUDES (una sola vez),
/// GARANTÍAS A FAVOR, ACCIDENTALIDAD y PÓLIZA DE RESPONSABILIDAD CIVIL. Lista vacía o RUNT sin detalle ⇒ la sección con «No
/// registra»; RUNT no completado ⇒ ninguna sección nueva. El detalle del fixture sale del lector real
/// (<see cref="LectorDetalleRunt.Desde"/>) a partir de una respuesta ficticia de la fuente, con un acreedor persona natural con
/// nombre y documento completos que el PDF no debe mostrar.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PdfVehiculoDisenoV2Tests
/// </code>
/// </example>
public sealed partial class PdfVehiculoDisenoV2Tests
{
    private const string NombreAcreedorNatural = "PEDRO FICTICIO ACREEDOR";

    private const string DocumentoAcreedorNatural = "1000005678";

    /// <summary>Títulos de las secciones 2–9, en el orden del diseño.</summary>
    private static readonly string[] TitulosAmpliados =
    [
        "INFORMACIÓN GENERAL", "DATOS TEÓRICOS", "DATOS SOAT", "DATOS REVISIÓN TECNO-MECÁNICA", "SOLICITUDES", "GARANTÍAS A FAVOR",
        "ACCIDENTALIDAD", "PÓLIZA DE RESPONSABILIDAD CIVIL",
    ];

    [Fact]
    public void Con_detalle_pinta_las_secciones_del_runt_en_orden_con_sus_valores()
    {
        var texto = PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(ContenidoConDetalle("ABC123"), GeneradoEn)));

        var desde = texto.IndexOf(PdfDePrueba.Plano("INFORMACIÓN DEL VEHÍCULO"), StringComparison.Ordinal);
        desde.Should().BeGreaterThanOrEqualTo(0, texto);
        foreach (var titulo in TitulosAmpliados.Append("PROPIETARIOS"))
        {
            var i = texto.IndexOf(PdfDePrueba.Plano(titulo), desde + 1, StringComparison.Ordinal);
            i.Should().BeGreaterThan(desde, $"«{titulo}» va después de la sección anterior: {texto}");
            desde = i;
        }

        System.Text.RegularExpressions.Regex.Count(texto, "SOLICITUDES").Should().Be(1, "la sección Solicitudes sale una sola vez");

        var vehiculo = Tramo(texto, "INFORMACIÓN DEL VEHÍCULO", "INFORMACIÓN GENERAL");
        foreach (var esperado in new[]
                 {
                     "Placa Licencia de tránsito Tipo de servicio ABC123 LT-0001 PARTICULAR",
                     "Clasificación Clase vehículo Modelo AUTOMOVIL AUTOMOVIL 2020",
                     "Fecha matrícula Organismo de tránsito 15/03/2020 STRIA PRUEBA",
                     "Titulares Locatarios registrados",
                 })
            PdfDePrueba.Tiene(vehiculo, esperado).Should().BeTrue($"«{esperado}» en Información del vehículo: {vehiculo}");

        var general = Tramo(texto, "INFORMACIÓN GENERAL", "DATOS TEÓRICOS");
        foreach (var esperado in new[]
                 {
                     "Marca Línea Cilindraje CHEVROLET SPARK 1200", "VIN N.º motor N.º chasis", "MOT-0001", "CHS-0001",
                     "Regrabado serie Estado del vehículo", "ACTIVO",
                 })
            PdfDePrueba.Tiene(general, esperado).Should().BeTrue($"«{esperado}» en Información general: {general}");
        PdfDePrueba.Tiene(texto, "N.º serie").Should().BeFalse("el RUNT no trae n.º de serie: va n.º chasis en su lugar");

        PdfDePrueba.Tiene(Tramo(texto, "DATOS TEÓRICOS", "DATOS SOAT"),
            "N.º ejes Capacidad de carga Pasajeros sentados Peso bruto vehicular 2 500 4 1450").Should().BeTrue(texto);

        var soat = Tramo(texto, "DATOS SOAT", "DATOS REVISIÓN TECNO-MECÁNICA");
        PdfDePrueba.Tiene(soat, "Estado N.º póliza Vigencia Expedición Vencimiento Entidad").Should().BeTrue(soat);
        PdfDePrueba.Tiene(soat, "Vigente SOAT-0002 01/06/2026 31/05/2026 31/05/2027 ASEGURADORA FICTICIA").Should().BeTrue(soat);
        PdfDePrueba.Tiene(soat, "No vigente SOAT-0001").Should().BeTrue(soat);
        soat.Should().NotContain("VIGENTE", "el estado se presenta en tipo oración");

        var rtm = Tramo(texto, "DATOS REVISIÓN TECNO-MECÁNICA", "SOLICITUDES");
        PdfDePrueba.Tiene(rtm, "Estado N.º certificado Vigencia Expedición CDA Vigente RTM-0001 10/01/2027 10/01/2026 CDA FICTICIO")
            .Should().BeTrue(rtm);

        var garantias = Tramo(texto, "GARANTÍAS A FAVOR", "ACCIDENTALIDAD");
        PdfDePrueba.Tiene(garantias, "Acreedor Confecámaras Fecha inscripción Tipo y N.º de documento acreedor").Should().BeTrue(garantias);
        PdfDePrueba.Tiene(garantias, "BANCO FICTICIO S.A.").Should().BeTrue(garantias);
        PdfDePrueba.Tiene(garantias, "NIT 900123456").Should().BeTrue(garantias);
        PdfDePrueba.Tiene(garantias, "Persona natural").Should().BeTrue(garantias);
        PdfDePrueba.Tiene(garantias, "CC ****5678").Should().BeTrue(garantias);
        texto.Should().NotContainAny([PdfDePrueba.Plano(NombreAcreedorNatural), DocumentoAcreedorNatural, "FICTICIOACREEDOR"]);

        PdfDePrueba.Tiene(Tramo(texto, "ACCIDENTALIDAD", "PÓLIZA DE RESPONSABILIDAD CIVIL"), "Fecha Gravedad 02/02/2025 SOLO DAÑOS").Should().BeTrue(texto);
    }

    [Fact]
    public void Solicitudes_y_poliza_rc_vacias_salen_con_su_encabezado_y_no_registra()
    {
        var texto = PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(ContenidoConDetalle("ABC123"), GeneradoEn)));

        PdfDePrueba.Tiene(Tramo(texto, "SOLICITUDES", "GARANTÍAS A FAVOR"),
            "Estado Entidad N.º solicitud Fecha solicitud Trámites realizados No registra").Should().BeTrue(texto);
        PdfDePrueba.Tiene(Tramo(texto, "PÓLIZA DE RESPONSABILIDAD CIVIL", "PROPIETARIOS"),
            "Estado N.º póliza Tipo póliza Expedición Vencimiento Entidad No registra").Should().BeTrue(texto);
    }

    [Fact]
    public void Runt_completado_sin_detalle_deja_la_informacion_del_vehiculo_como_hoy_y_las_secciones_con_no_registra()
    {
        var texto = PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido("ABC123"), GeneradoEn)));

        var vehiculo = Tramo(texto, "INFORMACIÓN DEL VEHÍCULO", "INFORMACIÓN GENERAL");
        PdfDePrueba.Tiene(vehiculo, $"Placa VIN Servicio ABC123 {Vin} PARTICULAR").Should().BeTrue(vehiculo);
        PdfDePrueba.Tiene(vehiculo, "Licencia de tránsito").Should().BeFalse("sin detalle queda el layout de hoy");
        var siguientes = TitulosAmpliados.Skip(1).Append("PROPIETARIOS").ToArray();
        for (var i = 0; i < TitulosAmpliados.Length; i++)
        {
            var tramo = Tramo(texto, TitulosAmpliados[i], siguientes[i]);
            PdfDePrueba.Tiene(tramo, "No registra").Should().BeTrue($"«{TitulosAmpliados[i]}» sin detalle dice «No registra»: {tramo}");
        }
    }

    [Theory]
    [InlineData(EstadoSeccion.NoAplica)]
    [InlineData(EstadoSeccion.Pendiente)]
    [InlineData(EstadoSeccion.NoDisponible)]
    public void Runt_no_completado_no_agrega_secciones(EstadoSeccion estado)
    {
        var contenido = ContenidoConDetalle("ABC123");
        var runt = contenido.Secciones[0] with
        {
            Estado = estado,
            Resultado = null,
            Leyenda = estado == EstadoSeccion.NoDisponible ? SeccionReporte.ConsultaNoDisponible : "Sin placa.",
        };
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(contenido with { Secciones = [runt, .. contenido.Secciones.Skip(1)] }, GeneradoEn));

        PdfDePrueba.Tiene(texto, "INFORMACIÓN DEL VEHÍCULO").Should().BeTrue(texto);
        texto.Should().NotContainAny([.. TitulosAmpliados.Select(PdfDePrueba.Plano)]);
    }

    /// <summary>El texto entre el título <paramref name="desde"/> y el siguiente título <paramref name="hasta"/>.</summary>
    private static string Tramo(string texto, string desde, string hasta)
    {
        var inicio = texto.IndexOf(PdfDePrueba.Plano(desde), StringComparison.Ordinal);
        if (inicio < 0)
            throw new InvalidOperationException($"El PDF no tiene «{desde}»: {texto}");
        var fin = texto.IndexOf(PdfDePrueba.Plano(hasta), inicio + PdfDePrueba.Plano(desde).Length, StringComparison.Ordinal);
        if (fin < 0)
            throw new InvalidOperationException($"El PDF no tiene «{hasta}» después de «{desde}»: {texto}");
        return texto[inicio..fin];
    }

    /// <summary>El contenido de muestra con el detalle del RUNT leído de una respuesta ficticia de la fuente.</summary>
    private static ContenidoReporteVehiculo ContenidoConDetalle(string? placa) =>
        Contenido(placa) with { DetalleRunt = LectorDetalleRunt.Desde(RespuestaRunt(placa)) ?? throw new InvalidOperationException("Sin detalle.") };

    /// <summary>Respuesta ficticia de la fuente: 2 SOAT, 1 RTM, garantía NIT y persona natural, accidentalidad; sin solicitudes ni póliza RC.</summary>
    private static ConsultarVehiculoIntempoResponse RespuestaRunt(string? placa)
    {
        var general = new InformacionGeneralIntempo
        {
            NoPlaca = placa ?? string.Empty,
            NoLicenciaTransito = "LT-0001",
            TipoServicio = "PARTICULAR",
            Clasificacion = "AUTOMOVIL",
            ClaseVehiculo = "AUTOMOVIL",
            Modelo = "2020",
            FechaMatricula = "15/03/2020",
            OrganismoTransito = "STRIA PRUEBA",
            Marca = "CHEVROLET",
            Linea = "SPARK",
            Cilidraje = "1200",
            Color = "ROJO",
            TipoCombustible = "GASOLINA",
            TipoCarroceria = "HATCHBACK",
            Puertas = "5",
            ClasicoAntiguo = "NO",
            Repotenciado = "NO",
            NoVin = Vin,
            NoMotor = "MOT-0001",
            NoChasis = "CHS-0001",
            EsRegrabadoVin = "NO",
            EsRegrabadoMotor = "NO",
            EsRegrabadoChasis = "NO",
            EsRegrabadoSerie = "NO",
            EstadoDelVehiculo = "ACTIVO",
        };
        var vehiculo = new VehiculoIntempo
        {
            Plate = placa ?? string.Empty,
            Vin = Vin,
            InformacionGeneral = general,
            DatosTecnicos = new DatosTecnicosIntempo { NoEjes = "2", CapacidadCarga = "500", PasajerosSentados = "4", PesoBrutoVehicular = "1450" },
        };
        vehiculo.Soat.Add(new SoatIntempo
        {
            Estado = "NO VIGENTE",
            NoPoliza = "SOAT-0001",
            FechaVigencia = "01/06/2025",
            FechaExpedicion = "31/05/2025",
            FechaVencimiento = "31/05/2026",
            EntidadExpideSoat = "ASEGURADORA FICTICIA",
        });
        vehiculo.Soat.Add(new SoatIntempo
        {
            Estado = "VIGENTE",
            NoPoliza = "SOAT-0002",
            FechaVigencia = "01/06/2026",
            FechaExpedicion = "31/05/2026",
            FechaVencimiento = "31/05/2027",
            EntidadExpideSoat = "ASEGURADORA FICTICIA",
        });
        vehiculo.TecnoMecanica.Add(new TecnoMecanicaIntempo
        {
            Vigente = "SI",
            NroCertificado = "RTM-0001",
            FechaVigente = "10/01/2027",
            FechaExpedicion = "10/01/2026",
            CdaExpide = "CDA FICTICIO",
        });
        vehiculo.GarantiasFavorDe.Add(new GarantiaFavorDeIntempo
        {
            TipoDocumentoAcreedor = "NIT",
            NumeroDocumentoAcreedor = "900123456",
            Acreedor = "BANCO FICTICIO S.A.",
            Confecamaras = "SI",
            FechaInscripcion = "20/03/2020",
        });
        vehiculo.GarantiasFavorDe.Add(new GarantiaFavorDeIntempo
        {
            TipoDocumentoAcreedor = "Cédula de ciudadanía",
            NumeroDocumentoAcreedor = DocumentoAcreedorNatural,
            Acreedor = NombreAcreedorNatural,
            Confecamaras = "NO",
            FechaInscripcion = "05/07/2022",
        });
        var accidente = new AccidenteIntempo();
        accidente.Campos.Add("fechaAccidente", "02/02/2025");
        accidente.Campos.Add("gravedad", "SOLO DAÑOS");
        vehiculo.AccidentRate.Add(accidente);
        return new ConsultarVehiculoIntempoResponse { Resultado = ResultadoRuntIntempo.Encontrado, Vehiculo = vehiculo };
    }
}
