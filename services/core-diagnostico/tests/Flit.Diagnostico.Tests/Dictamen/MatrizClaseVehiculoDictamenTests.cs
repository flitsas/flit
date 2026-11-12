using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Bug RTM/SOAT por clase de vehículo (ola 2026-11-11, hallazgos V4 y V5, punto 7.4). Dictamen de expert-doc-engine:
/// primera RTM a los 2 años de matrícula para servicio público, moto y motocarro; 5 años para el carro particular;
/// maquinaria y remolque sin RTM. SOAT: remolque exento; maquinaria advierte sin bloquear. En TRASPASO_UNILATERAL la RTM
/// no se valida (Res. 20233040017145, art. 5.3.2.2): RTM_NO_VIGENTE advierte. Clase desconocida conserva los 5 años y
/// sin fecha de matrícula la RTM se exige.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --filter FullyQualifiedName~MatrizClaseVehiculoDictamenTests
/// </code>
/// </example>
public sealed class MatrizClaseVehiculoDictamenTests
{
    private const string Standard = "TRASPASO_STANDARD";
    private const string Unilateral = "TRASPASO_UNILATERAL";
    private const string Transferencia = "TRASPASO_TRANSFERENCIA_DE_DOMINIO";

    private static readonly DateOnly Hoy = new(2026, 11, 11);

    // ---- Clasificación desde los textos del RUNT ----

    [Theory]
    [InlineData("AUTOMOVIL", null, ClaseVehiculoDictamen.Carro)]
    [InlineData("  camioneta ", null, ClaseVehiculoDictamen.Carro)]
    [InlineData("Campero", null, ClaseVehiculoDictamen.Carro)]
    [InlineData("BUS", null, ClaseVehiculoDictamen.Carro)]
    [InlineData("CAMIÓN", null, ClaseVehiculoDictamen.Carro)]
    [InlineData("MOTOCICLETA", null, ClaseVehiculoDictamen.Moto)]
    [InlineData("motocicleta", null, ClaseVehiculoDictamen.Moto)]
    [InlineData("MOTOCARRO", null, ClaseVehiculoDictamen.Motocarro)]
    [InlineData("MOTOTRICICLO", null, ClaseVehiculoDictamen.Motocarro)]
    [InlineData("CUATRIMOTO", null, ClaseVehiculoDictamen.Motocarro)]
    [InlineData("TRICIMOTO", null, ClaseVehiculoDictamen.Motocarro)]
    [InlineData("REMOLQUE", null, ClaseVehiculoDictamen.Remolque)]
    [InlineData("SEMIRREMOLQUE", null, ClaseVehiculoDictamen.Remolque)]
    [InlineData("Semiremolque", null, ClaseVehiculoDictamen.Remolque)]
    [InlineData("MAQUINARIA AGRÍCOLA", null, ClaseVehiculoDictamen.Maquinaria)]
    [InlineData("TRACTOR", "MAQUINARIA AGRICOLA", ClaseVehiculoDictamen.Maquinaria)]
    [InlineData("RETROEXCAVADORA", "Maquinaria de construcción", ClaseVehiculoDictamen.Maquinaria)]
    [InlineData(null, null, ClaseVehiculoDictamen.Desconocida)]
    [InlineData("", "", ClaseVehiculoDictamen.Desconocida)]
    [InlineData("NAVE ESPACIAL", null, ClaseVehiculoDictamen.Desconocida)]
    public void La_clase_del_RUNT_se_normaliza_a_la_clase_del_dictamen(string? clase, string? clasificacion, ClaseVehiculoDictamen esperada)
    {
        ClasificadorVehiculoDictamen.Clase(clase, clasificacion, null).Should().Be(esperada);
    }

    [Theory]
    [InlineData(TipoIdentificador.PlacaMaquinaria, ClaseVehiculoDictamen.Maquinaria)]
    [InlineData(TipoIdentificador.PlacaRemolque, ClaseVehiculoDictamen.Remolque)]
    [InlineData(TipoIdentificador.PlacaCarro, ClaseVehiculoDictamen.Desconocida)]
    [InlineData(TipoIdentificador.PlacaMoto, ClaseVehiculoDictamen.Desconocida)]
    [InlineData(TipoIdentificador.Vin, ClaseVehiculoDictamen.Desconocida)]
    public void Sin_clase_en_el_RUNT_la_placa_de_maquinaria_o_remolque_decide(TipoIdentificador tipo, ClaseVehiculoDictamen esperada)
    {
        ClasificadorVehiculoDictamen.Clase(null, null, tipo).Should().Be(esperada);
    }

    [Fact]
    public void La_clase_del_RUNT_manda_sobre_la_placa()
    {
        ClasificadorVehiculoDictamen.Clase("AUTOMOVIL", null, TipoIdentificador.PlacaRemolque).Should().Be(ClaseVehiculoDictamen.Carro);
    }

    [Theory]
    [InlineData("Particular", ServicioVehiculoDictamen.Particular)]
    [InlineData("PARTICULAR", ServicioVehiculoDictamen.Particular)]
    [InlineData("Público", ServicioVehiculoDictamen.Publico)]
    [InlineData("PUBLICO", ServicioVehiculoDictamen.Publico)]
    [InlineData(" servicio publico ", ServicioVehiculoDictamen.Publico)]
    [InlineData("Oficial", ServicioVehiculoDictamen.Otro)]
    [InlineData("Diplomático", ServicioVehiculoDictamen.Otro)]
    [InlineData(null, ServicioVehiculoDictamen.Desconocido)]
    [InlineData("  ", ServicioVehiculoDictamen.Desconocido)]
    public void El_servicio_del_RUNT_se_normaliza(string? servicio, ServicioVehiculoDictamen esperado)
    {
        ClasificadorVehiculoDictamen.Servicio(servicio).Should().Be(esperado);
    }

    // ---- Matriz RTM: clase × antigüedad ----

    public static TheoryData<string, int> ClaseYAntiguedad()
    {
        var datos = new TheoryData<string, int>();
        foreach (var fila in MatrizClaseVehiculo.Filas)
        {
            foreach (var anios in MatrizClaseVehiculo.Antiguedades)
                datos.Add(fila.Nombre, anios);
        }

        return datos;
    }

    [Theory]
    [MemberData(nameof(ClaseYAntiguedad))]
    public void Rtm_no_vigente_por_clase_y_antiguedad(string nombre, int anios)
    {
        var fila = MatrizClaseVehiculo.Fila(nombre);
        var resultado = EvaluadorDictamen.Evaluar(Hechos(Standard, fila, anios, soat: "VIGENTE", rtm: "NO VIGENTE"));

        var exigida = fila.AniosRtm is { } plazo && anios >= plazo;
        if (exigida)
        {
            resultado.Veredicto.Should().Be("BLOQUEADO", $"{nombre} con {anios} años exige RTM");
            resultado.Reglas.Should().ContainSingle().Which.Should().Be(new ReglaEvaluada("RTM_NO_VIGENTE", "RUNT", "BLOQUEA", null));
        }
        else
        {
            resultado.Veredicto.Should().Be("VIABLE", $"{nombre} con {anios} años no exige RTM");
            resultado.Reglas.Should().BeEmpty();
        }
    }

    [Theory]
    [MemberData(nameof(ClaseYAntiguedad))]
    public void Rtm_vigente_nunca_dispara(string nombre, int anios)
    {
        var resultado = EvaluadorDictamen.Evaluar(Hechos(Standard, MatrizClaseVehiculo.Fila(nombre), anios, soat: "VIGENTE", rtm: "VIGENTE"));

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ClaseVehiculoDictamen.Carro, ServicioVehiculoDictamen.Particular, 5)]
    [InlineData(ClaseVehiculoDictamen.Carro, ServicioVehiculoDictamen.Otro, 5)]
    [InlineData(ClaseVehiculoDictamen.Carro, ServicioVehiculoDictamen.Desconocido, 5)]
    [InlineData(ClaseVehiculoDictamen.Carro, ServicioVehiculoDictamen.Publico, 2)]
    [InlineData(ClaseVehiculoDictamen.Moto, ServicioVehiculoDictamen.Particular, 2)]
    [InlineData(ClaseVehiculoDictamen.Moto, ServicioVehiculoDictamen.Publico, 2)]
    [InlineData(ClaseVehiculoDictamen.Motocarro, ServicioVehiculoDictamen.Desconocido, 2)]
    [InlineData(ClaseVehiculoDictamen.Desconocida, ServicioVehiculoDictamen.Particular, 5)]
    [InlineData(ClaseVehiculoDictamen.Desconocida, ServicioVehiculoDictamen.Publico, 5)]
    public void Plazo_de_la_primera_rtm_en_anios(ClaseVehiculoDictamen clase, ServicioVehiculoDictamen servicio, int anios)
    {
        ClasificadorVehiculoDictamen.AniosPrimeraRtm(clase, servicio).Should().Be(anios);
    }

    [Theory]
    [InlineData(ClaseVehiculoDictamen.Maquinaria)]
    [InlineData(ClaseVehiculoDictamen.Remolque)]
    public void Maquinaria_y_remolque_no_tienen_rtm(ClaseVehiculoDictamen clase)
    {
        ClasificadorVehiculoDictamen.AniosPrimeraRtm(clase, ServicioVehiculoDictamen.Publico).Should().BeNull();
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void Clase_desconocida_conserva_los_cinco_anios(int anios, bool dispara)
    {
        var fila = new FilaMatrizClase("desconocida", null, "Público", ClaseVehiculoDictamen.Desconocida, 5, "SOAT_NO_VIGENTE", "BLOQUEA");

        var resultado = EvaluadorDictamen.Evaluar(Hechos(Standard, fila, anios, soat: "VIGENTE", rtm: "NO VIGENTE"));

        resultado.Reglas.Any(r => r.Codigo == "RTM_NO_VIGENTE").Should().Be(dispara);
    }

    [Theory]
    [InlineData("carro particular", true)]
    [InlineData("moto", true)]
    [InlineData("motocarro", true)]
    [InlineData("maquinaria", false)]
    [InlineData("remolque", false)]
    public void Sin_fecha_de_matricula_la_rtm_se_exige_salvo_en_maquinaria_y_remolque(string nombre, bool dispara)
    {
        var hechos = Hechos(Standard, MatrizClaseVehiculo.Fila(nombre), 1, soat: "VIGENTE", rtm: "NO VIGENTE");
        hechos = hechos with { Runt = hechos.Runt! with { FechaMatricula = null } };

        EvaluadorDictamen.Evaluar(hechos).Reglas.Any(r => r.Codigo == "RTM_NO_VIGENTE").Should().Be(dispara);
    }

    // ---- Matriz SOAT por clase ----

    [Theory]
    [InlineData("carro particular")]
    [InlineData("carro de servicio público")]
    [InlineData("moto")]
    [InlineData("motocarro")]
    [InlineData("maquinaria")]
    [InlineData("remolque")]
    public void Soat_no_vigente_por_clase(string nombre)
    {
        var fila = MatrizClaseVehiculo.Fila(nombre);
        var resultado = EvaluadorDictamen.Evaluar(Hechos(Standard, fila, 1, soat: "NO VIGENTE", rtm: "VIGENTE"));

        if (fila.CodigoSoat is null)
        {
            resultado.Veredicto.Should().Be("VIABLE");
            resultado.Reglas.Should().BeEmpty();
            return;
        }

        resultado.Reglas.Should().ContainSingle().Which.Should().Be(new ReglaEvaluada(fila.CodigoSoat, "RUNT", fila.EfectoSoat!, null));
        resultado.Veredicto.Should().Be(fila.EfectoSoat == "BLOQUEA" ? "BLOQUEADO" : "VIABLE");
    }

    [Fact]
    public void Remolque_sin_ningun_soat_ni_rtm_registrado_queda_viable()
    {
        var hechos = Hechos(Standard, MatrizClaseVehiculo.Fila("remolque"), 6, soat: "NO VIGENTE", rtm: "NO VIGENTE");
        hechos = hechos with { Runt = hechos.Runt! with { EstadosSoat = [], EstadosRtm = [] } };

        var resultado = EvaluadorDictamen.Evaluar(hechos);

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().BeEmpty();
    }

    [Fact]
    public void Maquinaria_con_soat_vigente_no_advierte()
    {
        var resultado = EvaluadorDictamen.Evaluar(Hechos(Standard, MatrizClaseVehiculo.Fila("maquinaria"), 6, soat: "VIGENTE", rtm: "NO VIGENTE"));

        resultado.Reglas.Should().BeEmpty();
    }

    // ---- Unilateral: la RTM no se valida (art. 5.3.2.2) ----

    [Theory]
    [InlineData("carro particular", 6)]
    [InlineData("carro de servicio público", 2)]
    [InlineData("moto", 2)]
    [InlineData("motocarro", 4)]
    public void En_unilateral_la_rtm_no_vigente_advierte(string nombre, int anios)
    {
        var resultado = EvaluadorDictamen.Evaluar(Hechos(Unilateral, MatrizClaseVehiculo.Fila(nombre), anios, soat: "VIGENTE", rtm: "NO VIGENTE"));

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(new ReglaEvaluada("RTM_NO_VIGENTE", "RUNT", "ADVIERTE", null));
    }

    [Theory]
    [InlineData(Standard)]
    [InlineData(Transferencia)]
    public void Fuera_del_unilateral_la_rtm_no_vigente_bloquea(string tipo)
    {
        var resultado = EvaluadorDictamen.Evaluar(Hechos(tipo, MatrizClaseVehiculo.Fila("moto"), 2, soat: "VIGENTE", rtm: "NO VIGENTE"));

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Efecto.Should().Be("BLOQUEA");
    }

    [Fact]
    public void En_unilateral_el_soat_sigue_bloqueando_y_la_maquinaria_sigue_advirtiendo()
    {
        EvaluadorDictamen.Evaluar(Hechos(Unilateral, MatrizClaseVehiculo.Fila("carro particular"), 1, soat: "NO VIGENTE", rtm: "VIGENTE"))
            .Reglas.Should().ContainSingle().Which.Should().Be(new ReglaEvaluada("SOAT_NO_VIGENTE", "RUNT", "BLOQUEA", null));
        EvaluadorDictamen.Evaluar(Hechos(Unilateral, MatrizClaseVehiculo.Fila("maquinaria"), 1, soat: "NO VIGENTE", rtm: "VIGENTE"))
            .Reglas.Should().ContainSingle().Which.Should().Be(new ReglaEvaluada("SOAT_MAQUINARIA", "RUNT", "ADVIERTE", null));
    }

    // ---- Catálogo v2 (aún sin publicar): RTM unilateral y SOAT de maquinaria ----

    [Fact]
    public void El_catalogo_v2_suma_soat_maquinaria_que_advierte_en_los_tres_tipos()
    {
        foreach (var tipo in CatalogoReglasDictamen.TiposTramite)
        {
            var fila = CatalogoReglasDictamen.Filas.Single(f => f.TipoTramiteCodigo == tipo && f.Codigo == "SOAT_MAQUINARIA");
            fila.Efecto.Should().Be("ADVIERTE");
            fila.PlantillaTexto.Should().ContainEquivalentOf("maquinaria").And.ContainEquivalentOf("advertencia");
        }

        CatalogoReglasDictamen.FilasV1.Should().NotContain(f => f.Codigo == "SOAT_MAQUINARIA");
    }

    [Fact]
    public void La_rtm_v2_advierte_solo_en_unilateral_y_no_habla_de_cinco_anios_para_todos()
    {
        foreach (var fila in CatalogoReglasDictamen.Filas.Where(f => f.Codigo == "RTM_NO_VIGENTE"))
        {
            fila.Efecto.Should().Be(fila.TipoTramiteCodigo == Unilateral ? "ADVIERTE" : "BLOQUEA");
            fila.PlantillaTexto.Should().NotContain("tiene 5 años o más de matrícula");
            fila.PlantillaTexto.Should().Contain("2 años").And.Contain("5");
        }

        CatalogoReglasDictamen.Filas.Single(f => f.TipoTramiteCodigo == Unilateral && f.Codigo == "RTM_NO_VIGENTE")
            .PlantillaTexto.Should().ContainEquivalentOf("advertencia");
        CatalogoReglasDictamen.FilasV1.Where(f => f.Codigo == "RTM_NO_VIGENTE").Should().OnlyContain(f => f.Efecto == "BLOQUEA");
    }

    // ---- Lector de hechos: clase y servicio salen del RUNT ----

    [Fact]
    public void El_lector_lleva_clase_y_servicio_del_detalle_RUNT_a_los_hechos()
    {
        var hechos = LectorHechosDictamen.Leer(Pedido(Detalle("MOTOCARRO", null, "Particular", "123ABC"), null));

        hechos.Runt!.Clase.Should().Be(ClaseVehiculoDictamen.Motocarro);
        hechos.Runt.Servicio.Should().Be(ServicioVehiculoDictamen.Particular);
    }

    [Fact]
    public void El_lector_usa_la_clasificacion_y_la_placa_cuando_la_clase_no_dice()
    {
        LectorHechosDictamen.Leer(Pedido(Detalle("TRACTOR", "MAQUINARIA AGRICOLA", null, null), null))
            .Runt!.Clase.Should().Be(ClaseVehiculoDictamen.Maquinaria);
        LectorHechosDictamen.Leer(Pedido(Detalle(null, null, null, "R12345"), null))
            .Runt!.Clase.Should().Be(ClaseVehiculoDictamen.Remolque);
    }

    [Fact]
    public void Sin_clase_en_el_detalle_el_lector_usa_la_del_RUNT_normalizado()
    {
        var normalizado = new VehiculoRunt("ABC123", null, "PUBLICO", "BUSETA", null, [], [], "RUNT", null);

        var hechos = LectorHechosDictamen.Leer(Pedido(Detalle(null, null, null, null), normalizado));

        hechos.Runt!.Clase.Should().Be(ClaseVehiculoDictamen.Carro);
        hechos.Runt.Servicio.Should().Be(ServicioVehiculoDictamen.Publico);
    }

    [Fact]
    public void Un_carro_publico_de_tres_anios_con_rtm_vencida_bloquea_de_punta_a_punta()
    {
        var detalle = Detalle("AUTOMOVIL", null, "Público", "ABC123", fecha: "11/11/2023", rtm: "VENCIDA");

        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Pedido(detalle, null)));

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Codigo.Should().Be("RTM_NO_VIGENTE");
    }

    [Fact]
    public void Un_remolque_sin_soat_queda_viable_de_punta_a_punta()
    {
        var detalle = Detalle("REMOLQUE", null, "Particular", "R12345", fecha: "11/11/2015", soat: "NO VIGENTE", rtm: "NO VIGENTE");

        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Pedido(detalle, null)));

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().BeEmpty();
    }

    // ---- Datos ----

    /// <summary>Hechos limpios (todas las fuentes al día) de la clase de la fila con la antigüedad pedida.</summary>
    internal static HechosDictamen Hechos(string tipo, FilaMatrizClase fila, int anios, string soat, string rtm) => new(
        tipo,
        Hoy,
        EstadoFuenteDictamen.Disponible,
        EstadoFuenteDictamen.Disponible,
        EstadoFuenteDictamen.Disponible,
        EstadoFuenteDictamen.Disponible,
        new HechosRuntVehiculo(
            "ACTIVO",
            Hoy.AddYears(-anios),
            false,
            false,
            [soat],
            [rtm],
            0,
            false,
            0,
            false,
            0,
            ClasificadorVehiculoDictamen.Clase(fila.ClaseRunt, null, null),
            ClasificadorVehiculoDictamen.Servicio(fila.ServicioRunt)),
        new HechosSimit(0, true, false, false, []),
        new HechosSimit(0, true, false, false, []),
        null,
        new LicenciaConductor("ACTIVA", true),
        null,
        0);

    private static PedidoLectura Pedido(DetalleRuntReporte detalle, VehiculoRunt? normalizado) => new(
        Standard,
        Hoy,
        new LecturaCelda<DetalleRuntReporte>(EstadoCelda.Completado, detalle),
        normalizado is null ? null : new LecturaCelda<VehiculoRunt>(EstadoCelda.Completado, normalizado),
        new LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, [Simit()]),
        new LecturaCelda<IReadOnlyList<ResultadoRuntPersonaDetalle>>(EstadoCelda.Completado,
            [new ResultadoRuntPersonaDetalle(true, null, [new LicenciaDetalle(null, "ACTIVA", null)],
                Certificados: [new CertificadoAptitudDetalle(null, "VIGENTE", null, null, null, null, null)])]),
        new LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, [Simit()]),
        null,
        null,
        null);

    private static ResultadoSimitDetalle Simit() =>
        new(null, 0, [], false, PazYSalvo: true, Suspendida: false, Cancelada: false, Acuerdos: []);

    private static DetalleRuntReporte Detalle(
        string? clase, string? clasificacion, string? servicio, string? placa,
        string fecha = "11/11/2025", string soat = "VIGENTE", string rtm = "VIGENTE") => new(
        new DetalleRuntVehiculo(placa, null, servicio, clasificacion, clase, null, fecha, null),
        new DetalleRuntGeneral(
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "ACTIVO", "NO", "NO"),
        new DetalleRuntTecnico(null, null, null, null),
        [new DetalleRuntSoat(soat, null, null, null, null, null)],
        [new DetalleRuntRtm(rtm, null, null, null, null)],
        [],
        [],
        [],
        []);
}
