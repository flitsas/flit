using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Dictamen;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Corte 3: las lecturas ya armadas se vuelven <see cref="HechosDictamen"/>. No descifra ni persiste.
/// </summary>
public sealed class LectorHechosDictamenTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 3);

    [Fact]
    public void Soat_vigente_junto_a_otro_no_vigente_no_dispara_ni_la_rtm_vigente()
    {
        var pedido = Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Completado, Detalle(soat: [Soat("VIGENTE"), Soat("NO VIGENTE")])),
        };

        var reglas = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(pedido)).Reglas;

        reglas.Should().NotContain(r => r.Codigo == "SOAT_NO_VIGENTE" || r.Codigo == "RTM_NO_VIGENTE");
    }

    [Fact]
    public void Soat_no_vigente_bloquea()
    {
        var pedido = Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Completado, Detalle(soat: [Soat("NO VIGENTE")])),
        };

        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(pedido));

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().Contain(r => r.Codigo == "SOAT_NO_VIGENTE");
    }

    [Fact]
    public void Fecha_ilegible_deja_la_matricula_en_null_y_la_rtm_se_evalua()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Completado, Detalle(fecha: "ayer", rtm: [Rtm("NO VIGENTE")])),
        });

        hechos.Runt!.FechaMatricula.Should().BeNull();
        EvaluadorDictamen.Evaluar(hechos).Veredicto.Should().Be("BLOQUEADO");
        EvaluadorDictamen.Evaluar(hechos).Reglas.Should().Contain(r => r.Codigo == "RTM_NO_VIGENTE");
    }

    [Fact]
    public void Con_menos_de_cinco_anos_la_rtm_no_vigente_no_dispara()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Completado, Detalle(fecha: "04/11/2021", rtm: [Rtm("NO VIGENTE")])),
        });

        hechos.Runt!.FechaMatricula.Should().Be(new DateOnly(2021, 11, 4));
        EvaluadorDictamen.Evaluar(hechos).Reglas.Should().NotContain(r => r.Codigo == "RTM_NO_VIGENTE");
    }

    [Fact]
    public void Prenda_y_gravamen_en_si_advierten_y_en_no_no()
    {
        var conSi = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Completado, Detalle(prenda: "si", gravamen: "SI")),
        }));
        var conNo = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Completado, Detalle(prenda: "NO", gravamen: "no")),
        }));

        conSi.Reglas.Select(r => r.Codigo).Should().Contain(["PRENDA", "GRAVAMEN"]);
        conNo.Reglas.Should().NotContain(r => r.Codigo == "PRENDA" || r.Codigo == "GRAVAMEN");
    }

    [Fact]
    public void Cuenta_limitaciones_garantias_e_infracciones()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Completado, Detalle(limitaciones: 2, garantias: 1, infracciones: 3)),
        });

        hechos.Runt!.Limitaciones.Should().Be(2);
        hechos.Runt.GarantiasMobiliarias.Should().Be(1);
        hechos.Runt.Infracciones.Should().Be(3);
    }

    [Fact]
    public void Leasing_con_documento_de_locatario_marca_las_dos_banderas()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with { RuntNormalizado = Celda(EstadoCelda.Completado, Vehiculo(true, "1")) });

        hechos.Runt!.EsLeasing.Should().BeTrue();
        hechos.Runt.HayDocumentoLocatario.Should().BeTrue();
    }

    [Fact]
    public void Locatario_sin_numero_no_tiene_documento()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with { RuntNormalizado = Celda(EstadoCelda.Completado, Vehiculo(false, "   ")) });

        hechos.Runt!.HayDocumentoLocatario.Should().BeFalse();
    }

    [Fact]
    public void Runt_completado_sin_detalle_queda_sin_resultado()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with { RuntDetalle = Celda<DetalleRuntReporte>(EstadoCelda.Completado, null) });

        hechos.EstadoRunt.Should().Be(EstadoFuenteDictamen.SinResultado);
        hechos.Runt.Should().BeNull();
    }

    [Fact]
    public void Runt_en_error_ignora_el_detalle()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with { RuntDetalle = Celda(EstadoCelda.Error, Detalle(soat: [Soat("NO VIGENTE")])) });

        hechos.EstadoRunt.Should().Be(EstadoFuenteDictamen.Error);
        hechos.Runt.Should().BeNull();
    }

    [Fact]
    public void Runt_que_no_aplica_queda_en_no_aplica()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with { RuntDetalle = Celda<DetalleRuntReporte>(EstadoCelda.Na, null) });

        hechos.EstadoRunt.Should().Be(EstadoFuenteDictamen.NoAplica);
    }

    [Fact]
    public void Simit_traduce_comparendos_paz_y_salvo_suspension_y_saldos()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            SimitVehiculo = Celda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, [Simit(2, false, true, false, "0", "422652")]),
        });

        hechos.SimitVehiculo.Should().BeEquivalentTo(new HechosSimit(2, false, true, false, [new AcuerdoPago(0), new AcuerdoPago(422_652)]));
        EvaluadorDictamen.Evaluar(hechos).Reglas.Should().Contain(r => r.Codigo == "SIMIT_VEHICULO");
    }

    [Fact]
    public void Pendiente_ilegible_no_queda_en_cero()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            SimitVehiculo = Celda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, [Simit(0, true, false, false, "no-es-un-saldo")]),
        });

        hechos.SimitVehiculo!.Acuerdos.Should().ContainSingle().Which.SaldoPendientePesos.Should().Be(1);
    }

    [Fact]
    public void Dos_personas_simit_se_resumen()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            SimitPropietario = Celda<IReadOnlyList<ResultadoSimitDetalle>>(
                EstadoCelda.Completado,
                [Simit(2, true, false, false), Simit(3, false, false, false, "10")]),
        });

        hechos.SimitPropietario!.CantidadComparendos.Should().Be(5);
        hechos.SimitPropietario.PazYSalvo.Should().BeFalse();
        hechos.SimitPropietario.Acuerdos.Should().ContainSingle().Which.SaldoPendientePesos.Should().Be(10);
    }

    [Fact]
    public void Lista_simit_vacia_con_celda_completada_es_sin_resultado()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            SimitVehiculo = Celda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, []),
        });

        hechos.EstadoSimitVehiculo.Should().Be(EstadoFuenteDictamen.SinResultado);
        hechos.SimitVehiculo.Should().BeNull();
    }

    [Fact]
    public void Licencia_vencida_advierte_por_el_propietario()
    {
        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base() with
        {
            RuntPropietario = Celda<IReadOnlyList<ResultadoRuntPersonaDetalle>>(EstadoCelda.Completado, [Persona("VENCIDA", Cert("VIGENTE"))]),
        }));

        resultado.Reglas.Should().Contain(r => r.Codigo == "LICENCIA_CONDUCTOR" && r.Fuente == "RUNT_PROPIETARIO");
    }

    [Fact]
    public void Licencia_activa_con_certificado_vigente_no_dispara()
    {
        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base()));

        resultado.Reglas.Should().NotContain(r => r.Codigo == "LICENCIA_CONDUCTOR");
    }

    [Fact]
    public void Sin_certificados_advierte()
    {
        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base() with
        {
            RuntPropietario = Celda<IReadOnlyList<ResultadoRuntPersonaDetalle>>(EstadoCelda.Completado, [Persona("ACTIVA")]),
        }));

        resultado.Reglas.Should().Contain(r => r.Codigo == "LICENCIA_CONDUCTOR");
    }

    [Fact]
    public void Certificado_sin_estado_y_vencimiento_posterior_esta_vigente()
    {
        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base() with
        {
            RuntPropietario = Celda<IReadOnlyList<ResultadoRuntPersonaDetalle>>(
                EstadoCelda.Completado,
                [Persona("ACTIVA", Cert(null, "01/01/2027"))]),
        }));

        resultado.Reglas.Should().NotContain(r => r.Codigo == "LICENCIA_CONDUCTOR");
    }

    [Fact]
    public void Propietario_en_error_deja_la_licencia_en_null_y_advierte_la_fuente()
    {
        var hechos = LectorHechosDictamen.Leer(Base() with
        {
            RuntPropietario = Celda<IReadOnlyList<ResultadoRuntPersonaDetalle>>(EstadoCelda.Error, [Persona("ACTIVA", Cert("VIGENTE"))]),
        });

        hechos.LicenciaPropietario.Should().BeNull();
        hechos.EstadoRuntPropietario.Should().Be(EstadoFuenteDictamen.Error);
        EvaluadorDictamen.Evaluar(hechos).Reglas.Should().Contain(r => r.Codigo == "FUENTE_PENDIENTE" && r.Fuente == "RUNT_PROPIETARIO");
    }

    [Fact]
    public void Impuesto_con_monto_advierte_y_sin_celda_no_es_fuente_pendiente()
    {
        var conMonto = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base() with
        {
            Impuesto = Celda(EstadoCelda.Completado, new ResultadoImpuestoDetalle(1500, [])),
        }));
        var sinCelda = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base()));

        conMonto.Reglas.Should().Contain(r => r.Codigo == "IMPUESTO" && r.MontoPesos == 1500);
        sinCelda.Reglas.Should().NotContain(r => r.Codigo == "IMPUESTO" || r.Codigo == "FUENTE_PENDIENTE");
    }

    [Fact]
    public void Las_cuatro_fuentes_en_error_avisan_y_siguen_viables()
    {
        var resultado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(Base() with
        {
            RuntDetalle = Celda(EstadoCelda.Error, Detalle()),
            SimitVehiculo = Celda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Error, [Simit()]),
            RuntPropietario = Celda<IReadOnlyList<ResultadoRuntPersonaDetalle>>(EstadoCelda.Error, [Persona("ACTIVA", Cert("VIGENTE"))]),
            SimitPropietario = Celda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Error, [Simit()]),
        }));

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Select(r => r.Fuente).Should().Equal("RUNT", "SIMIT_VEHICULO", "RUNT_PROPIETARIO", "SIMIT_PROPIETARIO");
    }

    [Fact]
    public void Pedido_null_no_se_lee()
    {
        var acto = () => LectorHechosDictamen.Leer(null!);

        acto.Should().Throw<ArgumentNullException>();
    }

    private static PedidoLectura Base() => new(
        "TRASPASO_STANDARD",
        Hoy,
        Celda(EstadoCelda.Completado, Detalle()),
        Celda(EstadoCelda.Completado, Vehiculo(false, null)),
        Celda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, [Simit()]),
        Celda<IReadOnlyList<ResultadoRuntPersonaDetalle>>(EstadoCelda.Completado, [Persona("ACTIVA", Cert("VIGENTE"))]),
        Celda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, [Simit()]),
        null,
        null,
        null);

    private static LecturaCelda<T> Celda<T>(EstadoCelda estado, T? lectura) => new(estado, lectura);

    private static DetalleRuntReporte Detalle(
        string fecha = "07/01/2020",
        string estado = "ACTIVO",
        string prenda = "NO",
        string gravamen = "NO",
        IReadOnlyList<DetalleRuntSoat>? soat = null,
        IReadOnlyList<DetalleRuntRtm>? rtm = null,
        int limitaciones = 0,
        int garantias = 0,
        int infracciones = 0) => new(
        new DetalleRuntVehiculo(null, null, null, null, null, null, fecha, null),
        new DetalleRuntGeneral(
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, estado, prenda, gravamen),
        new DetalleRuntTecnico(null, null, null, null),
        soat ?? [Soat("VIGENTE")],
        rtm ?? [Rtm("VIGENTE")],
        [],
        [],
        [],
        [],
        Enumerable.Range(0, limitaciones).Select(_ => new DetalleRuntLimitacion(null, null, null, null, null, null, null, null, null)).ToArray(),
        Enumerable.Range(0, garantias).Select(_ => new DetalleRuntGarantiaMobiliaria(null, null, null, null, null)).ToArray(),
        Enumerable.Range(0, infracciones).Select(_ => new DetalleRuntInfraccion(null, null, null, null, null, null, null, null, null)).ToArray());

    private static DetalleRuntSoat Soat(string estado) => new(estado, null, null, null, null, null);

    private static DetalleRuntRtm Rtm(string estado) => new(estado, null, null, null, null);

    private static VehiculoRunt Vehiculo(bool leasing, string? documentoLocatario)
    {
        LocatarioRunt[] locatarios = documentoLocatario is null ? [] : [new LocatarioRunt("CC", documentoLocatario)];
        PropietarioRunt[] propietarios = [new("CC", "1", leasing ? TipoPropiedad.Leasing : TipoPropiedad.Propio, [])];
        return new VehiculoRunt(null, null, null, null, null, propietarios, locatarios, "RUNT", null);
    }

    private static ResultadoSimitDetalle Simit(
        int cantidad = 0, bool? paz = true, bool suspendida = false, bool cancelada = false, params string?[] pendientes) =>
        new(null, cantidad, [], false, PazYSalvo: paz, Suspendida: suspendida, Cancelada: cancelada,
            Acuerdos: pendientes.Select(p => new AcuerdoSimitDetalle(null, null, null, p, null, null)).ToArray());

    private static ResultadoRuntPersonaDetalle Persona(string? estadoLicencia, params CertificadoAptitudDetalle[] certificados)
    {
        LicenciaDetalle[] licencias = estadoLicencia is null ? [] : [new LicenciaDetalle(null, estadoLicencia, null)];
        return new ResultadoRuntPersonaDetalle(true, null, licencias, Certificados: certificados);
    }

    private static CertificadoAptitudDetalle Cert(string? estado, string? vence = null) => new(null, estado, null, vence, null, null, null);
}
