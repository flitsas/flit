using Flit.Diagnostico.Domain.Dictamen;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Catálogo v1 y evaluador de TRASPASO (épica #12678, sin HU). Los hechos ya vienen leídos: el evaluador no abre
/// celdas ni llama fuentes. Una regla BLOQUEA deja el veredicto en BLOQUEADO; si solo hay ADVIERTE, o ninguna
/// dispara, el veredicto es VIABLE.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --filter FullyQualifiedName~EvaluadorDictamenTests
/// </code>
/// </example>
public sealed class EvaluadorDictamenTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 3);

    private static readonly string[] Codigos =
    [
        "SOAT_NO_VIGENTE",
        "RTM_NO_VIGENTE",
        "ESTADO_VEHICULO",
        "SIMIT_VEHICULO",
        "SIMIT_PROPIETARIO",
        "SIMIT_LOCATARIO",
        "LIMITACION",
        "GRAVAMEN",
        "GARANTIA_MOBILIARIA",
        "PRENDA",
        "INFRACCION_RUNT",
        "LICENCIA_CONDUCTOR",
        "IMPUESTO",
        "FUENTE_PENDIENTE",
    ];

    [Fact]
    public void Nada_dispara_y_el_veredicto_es_viable()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base());

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().BeEmpty();
    }

    [Theory]
    [InlineData("vigente")]
    [InlineData("NO REQUIERE")]
    [InlineData("no_requiere")]
    public void Soat_cubierto_no_dispara(string estado)
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadosSoat = [estado] }));

        resultado.Reglas.Should().NotContain(x => x.Codigo == "SOAT_NO_VIGENTE");
        resultado.Veredicto.Should().Be("VIABLE");
    }

    [Fact]
    public void Soat_vigente_junto_a_no_vigentes_no_dispara()
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadosSoat = ["NO VIGENTE", "VIGENTE", "VENCIDO"] }));

        resultado.Reglas.Should().NotContain(x => x.Codigo == "SOAT_NO_VIGENTE");
    }

    [Theory]
    [InlineData("NO VIGENTE")]
    [InlineData("VENCIDO")]
    [InlineData("DESCONOCIDO")]
    [InlineData("")]
    [InlineData("VACIO")]
    public void Soat_sin_cobertura_bloquea(string estado)
    {
        var estados = estado == "VACIO" ? Array.Empty<string?>() : new string?[] { estado };
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadosSoat = estados }));

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle(x =>
            x.Codigo == "SOAT_NO_VIGENTE" && x.Efecto == "BLOQUEA" && x.Fuente == "RUNT" && x.MontoPesos == null);
    }

    [Fact]
    public void Soat_con_estado_nulo_bloquea()
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadosSoat = [null] }));

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().Contain(x => x.Codigo == "SOAT_NO_VIGENTE");
    }

    [Fact]
    public void Rtm_con_menos_de_cinco_anos_no_se_evalua()
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with
        {
            FechaMatricula = new DateOnly(2021, 11, 4),
            EstadosRtm = ["NO VIGENTE"],
        }));

        resultado.Reglas.Should().NotContain(x => x.Codigo == "RTM_NO_VIGENTE");
        resultado.Veredicto.Should().Be("VIABLE");
    }

    [Fact]
    public void Rtm_al_cumplir_cinco_anos_bloquea()
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with
        {
            FechaMatricula = new DateOnly(2021, 11, 3),
            EstadosRtm = ["NO VIGENTE"],
        }));

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle(x => x.Codigo == "RTM_NO_VIGENTE" && x.Efecto == "BLOQUEA" && x.Fuente == "RUNT");
    }

    [Fact]
    public void Rtm_sin_fecha_se_evalua()
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { FechaMatricula = null, EstadosRtm = ["VENCIDO"] }));

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().Contain(x => x.Codigo == "RTM_NO_VIGENTE");
    }

    [Fact]
    public void Rtm_vigente_no_dispara_aunque_haya_otra_vencida()
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadosRtm = ["NO VIGENTE", "NO REQUIERE"] }));

        resultado.Reglas.Should().NotContain(x => x.Codigo == "RTM_NO_VIGENTE");
    }

    [Theory]
    [InlineData("ACTIVO")]
    [InlineData("activo")]
    [InlineData("REGISTRADO")]
    public void Estado_activo_o_registrado_no_dispara(string estado)
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadoVehiculo = estado }));

        resultado.Reglas.Should().NotContain(x => x.Codigo == "ESTADO_VEHICULO");
    }

    [Theory]
    [InlineData("INACTIVO")]
    [InlineData("CANCELADO")]
    [InlineData("")]
    public void Estado_distinto_advierte_y_el_traspaso_sigue_viable(string estado)
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadoVehiculo = estado }));

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle(x => x.Codigo == "ESTADO_VEHICULO" && x.Efecto == "ADVIERTE" && x.Fuente == "RUNT");
    }

    [Theory]
    [InlineData("comparendos")]
    [InlineData("paz")]
    [InlineData("suspendida")]
    [InlineData("cancelada")]
    [InlineData("saldo")]
    public void Simit_del_vehiculo_advierte(string caso)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with { SimitVehiculo = Simit(caso) });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle(x =>
            x.Codigo == "SIMIT_VEHICULO" && x.Efecto == "ADVIERTE" && x.Fuente == "SIMIT_VEHICULO" && x.MontoPesos == null);
    }

    [Fact]
    public void Acuerdo_con_saldo_pendiente_en_cero_no_dispara()
    {
        var limpio = new HechosSimit(0, true, false, false, [new AcuerdoPago(0)]);
        var resultado = EvaluadorDictamen.Evaluar(Base() with { SimitVehiculo = limpio, SimitPropietario = limpio });

        resultado.Reglas.Should().BeEmpty();
    }

    [Fact]
    public void En_leasing_no_se_evalua_el_simit_del_propietario()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with
        {
            Runt = Runt() with { EsLeasing = true, HayDocumentoLocatario = true },
            SimitPropietario = Simit("comparendos"),
            SimitLocatario = SimitLimpio(),
            LicenciaLocatario = new LicenciaConductor("ACTIVA", true),
        });

        resultado.Reglas.Should().BeEmpty();
        resultado.Veredicto.Should().Be("VIABLE");
    }

    [Fact]
    public void En_leasing_sin_documento_de_locatario_advierte_que_no_se_pudo_consultar()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with
        {
            Runt = Runt() with { EsLeasing = true, HayDocumentoLocatario = false },
        });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle(x => x.Codigo == "SIMIT_LOCATARIO" && x.Fuente == "SIMIT_LOCATARIO");
    }

    [Fact]
    public void Locatario_con_documento_y_saldo_pendiente_advierte()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with
        {
            Runt = Runt() with { HayDocumentoLocatario = true },
            SimitLocatario = Simit("saldo"),
        });

        resultado.Reglas.Should().Contain(x => x.Codigo == "SIMIT_LOCATARIO");
    }

    [Fact]
    public void Sin_leasing_y_sin_documento_no_se_evalua_el_locatario()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with { SimitLocatario = Simit("comparendos") });

        resultado.Reglas.Should().NotContain(x => x.Codigo == "SIMIT_LOCATARIO");
    }

    [Fact]
    public void Limitacion_gravamen_garantia_prenda_e_infraccion_advierten()
    {
        var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with
        {
            Limitaciones = 1,
            TieneGravamenes = true,
            GarantiasMobiliarias = 1,
            TienePrenda = true,
            Infracciones = 1,
        }));

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Select(x => x.Codigo).Should().Equal(
            "LIMITACION", "GRAVAMEN", "GARANTIA_MOBILIARIA", "PRENDA", "INFRACCION_RUNT");
        resultado.Reglas.Should().OnlyContain(x => x.Efecto == "ADVIERTE" && x.Fuente == "RUNT");
    }

    [Theory]
    [InlineData("SUSPENDIDA")]
    [InlineData("VENCIDA")]
    [InlineData("CANCELADA")]
    public void Licencia_mala_advierte(string estado)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with { LicenciaPropietario = new LicenciaConductor(estado, true) });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle(x =>
            x.Codigo == "LICENCIA_CONDUCTOR" && x.Fuente == "RUNT_PROPIETARIO" && x.Efecto == "ADVIERTE");
    }

    [Fact]
    public void Sin_certificado_de_aptitud_vigente_advierte()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with { LicenciaPropietario = new LicenciaConductor("ACTIVA", false) });

        resultado.Reglas.Should().Contain(x => x.Codigo == "LICENCIA_CONDUCTOR");
    }

    [Fact]
    public void En_leasing_se_mira_tambien_la_licencia_del_locatario()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with
        {
            Runt = Runt() with { EsLeasing = true, HayDocumentoLocatario = true },
            SimitLocatario = SimitLimpio(),
            LicenciaLocatario = new LicenciaConductor("VENCIDA", true),
        });

        resultado.Reglas.Should().ContainSingle(x => x.Codigo == "LICENCIA_CONDUCTOR" && x.Fuente == "RUNT_LOCATARIO");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void Impuesto_en_cero_o_sin_monto_no_dispara(long? monto)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with { ImpuestoMontoPesos = monto });

        resultado.Reglas.Should().NotContain(x => x.Codigo == "IMPUESTO");
    }

    [Fact]
    public void Deuda_sin_monto_dispara_impuesto_y_sin_deuda_no()
    {
        var conDeuda = EvaluadorDictamen.Evaluar(Base() with { ImpuestoTieneDeuda = true, ImpuestoMontoPesos = null });
        var sinDeuda = EvaluadorDictamen.Evaluar(Base() with { ImpuestoTieneDeuda = false, ImpuestoMontoPesos = 0 });

        conDeuda.Reglas.Should().ContainSingle(x => x.Codigo == "IMPUESTO" && x.Fuente == "IMPUESTO_DEPARTAMENTAL" && x.MontoPesos == null);
        sinDeuda.Reglas.Should().NotContain(x => x.Codigo == "IMPUESTO");
    }

    [Fact]
    public void Impuesto_en_error_dentro_del_alcance_deja_fuente_pendiente_y_na_o_fuera_no()
    {
        var error = EvaluadorDictamen.Evaluar(Base() with { EstadoImpuesto = EstadoFuenteDictamen.Error });
        var na = EvaluadorDictamen.Evaluar(Base() with { EstadoImpuesto = EstadoFuenteDictamen.NoAplica });
        var fuera = EvaluadorDictamen.Evaluar(Base());

        error.Reglas.Should().Contain(x => x.Codigo == "FUENTE_PENDIENTE" && x.Fuente == "IMPUESTO_DEPARTAMENTAL");
        na.Reglas.Should().NotContain(x => x.Fuente == "IMPUESTO_DEPARTAMENTAL");
        fuera.Reglas.Should().NotContain(x => x.Fuente == "IMPUESTO_DEPARTAMENTAL");
    }

    [Fact]
    public void Impuesto_mayor_que_cero_advierte_con_el_monto()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with { ImpuestoMontoPesos = 1500 });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle(x =>
            x.Codigo == "IMPUESTO" && x.Efecto == "ADVIERTE" && x.Fuente == "IMPUESTO_DEPARTAMENTAL" && x.MontoPesos == 1500);
    }

    [Theory]
    [InlineData(EstadoFuenteDictamen.Error)]
    [InlineData(EstadoFuenteDictamen.NoAplica)]
    [InlineData(EstadoFuenteDictamen.SinResultado)]
    public void Fuente_obligatoria_pendiente_advierte_y_no_bloquea(EstadoFuenteDictamen estado)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with
        {
            EstadoRunt = estado,
            Runt = Runt() with { EstadosSoat = [], EstadosRtm = [] },
        });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle(x =>
            x.Codigo == "FUENTE_PENDIENTE" && x.Efecto == "ADVIERTE" && x.Fuente == "RUNT");
    }

    [Fact]
    public void Las_cuatro_fuentes_pendientes_avisan_en_orden_y_siguen_viables()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with
        {
            EstadoRunt = EstadoFuenteDictamen.Error,
            EstadoSimitVehiculo = EstadoFuenteDictamen.NoAplica,
            EstadoRuntPropietario = EstadoFuenteDictamen.SinResultado,
            EstadoSimitPropietario = EstadoFuenteDictamen.Error,
            Runt = null,
            SimitVehiculo = null,
            SimitPropietario = null,
            LicenciaPropietario = null,
        });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Select(x => x.Fuente).Should().Equal(
            "RUNT", "SIMIT_VEHICULO", "RUNT_PROPIETARIO", "SIMIT_PROPIETARIO");
        resultado.Reglas.Should().OnlyContain(x => x.Codigo == "FUENTE_PENDIENTE" && x.Efecto == "ADVIERTE");
    }

    [Fact]
    public void El_soat_bloquea_aunque_otra_fuente_solo_advierte()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base() with
        {
            EstadoSimitPropietario = EstadoFuenteDictamen.Error,
            SimitPropietario = null,
            Runt = Runt() with { EstadosSoat = ["NO VIGENTE"] },
        });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Select(x => (x.Codigo, x.Efecto)).Should().Equal(
            ("SOAT_NO_VIGENTE", "BLOQUEA"),
            ("FUENTE_PENDIENTE", "ADVIERTE"));
    }

    [Fact]
    public void Los_tres_tipos_de_traspaso_comparten_el_veredicto()
    {
        foreach (var tipo in CatalogoReglasDictamen.TiposTramite)
        {
            var resultado = EvaluadorDictamen.Evaluar(ConRunt(r => r with { EstadosSoat = ["NO VIGENTE"] }) with
            {
                TipoTramiteCodigo = tipo,
            });

            resultado.Veredicto.Should().Be("BLOQUEADO");
            resultado.Reglas.Select(x => x.Codigo).Should().Equal("SOAT_NO_VIGENTE");
        }
    }

    [Fact]
    public void El_catalogo_repite_las_catorce_reglas_en_los_tres_tipos()
    {
        CatalogoReglasDictamen.Version.Should().Be(1);
        CatalogoReglasDictamen.TiposTramite.Should().Equal(
            "TRASPASO_STANDARD", "TRASPASO_UNILATERAL", "TRASPASO_TRANSFERENCIA_DE_DOMINIO");
        CatalogoReglasDictamen.Filas.Should().HaveCount(42);
        CatalogoReglasDictamen.Filas
            .Select(f => (f.CatalogoVersion, f.TipoTramiteCodigo, f.Codigo))
            .Should().OnlyHaveUniqueItems();

        foreach (var tipo in CatalogoReglasDictamen.TiposTramite)
        {
            var filas = CatalogoReglasDictamen.Filas.Where(f => f.TipoTramiteCodigo == tipo).ToList();
            filas.Select(f => f.Codigo).Should().Equal(Codigos);
            filas.Should().OnlyContain(f => f.CatalogoVersion == 1 && f.PlantillaTexto.Length > 0);
            filas.Single(f => f.Codigo == "SOAT_NO_VIGENTE").Efecto.Should().Be("BLOQUEA");
            filas.Single(f => f.Codigo == "RTM_NO_VIGENTE").Efecto.Should().Be("BLOQUEA");
            filas.Where(f => f.Codigo is not ("SOAT_NO_VIGENTE" or "RTM_NO_VIGENTE"))
                .Should().OnlyContain(f => f.Efecto == "ADVIERTE");
        }
    }

    [Fact]
    public void Las_plantillas_son_las_de_la_ficha()
    {
        var filas = CatalogoReglasDictamen.Filas.Where(f => f.TipoTramiteCodigo == "TRASPASO_STANDARD");

        filas.ToDictionary(f => f.Codigo, f => f.PlantillaTexto).Should().Equal(Plantillas);
    }

    [Fact]
    public void Un_tipo_de_tramite_desconocido_no_se_evalua()
    {
        var acto = () => EvaluadorDictamen.Evaluar(Base() with { TipoTramiteCodigo = "MATRICULA" });

        acto.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Sin_hechos_no_se_evalua()
    {
        var acto = () => EvaluadorDictamen.Evaluar(null!);

        acto.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Runt_disponible_sin_lectura_no_se_evalua()
    {
        var acto = () => EvaluadorDictamen.Evaluar(Base() with { Runt = null });

        acto.Should().Throw<ArgumentException>();
    }

    private static HechosDictamen Base() => new(
        "TRASPASO_STANDARD",
        Hoy,
        EstadoFuenteDictamen.Disponible,
        EstadoFuenteDictamen.Disponible,
        EstadoFuenteDictamen.Disponible,
        EstadoFuenteDictamen.Disponible,
        Runt(),
        SimitLimpio(),
        SimitLimpio(),
        null,
        new LicenciaConductor("ACTIVA", true),
        null,
        0);

    private static HechosDictamen ConRunt(Func<HechosRuntVehiculo, HechosRuntVehiculo> ajustar) =>
        Base() with { Runt = ajustar(Runt()) };

    private static HechosRuntVehiculo Runt() => new(
        "ACTIVO",
        new DateOnly(2020, 1, 7),
        false,
        false,
        ["VIGENTE"],
        ["VIGENTE"],
        0,
        false,
        0,
        false,
        0);

    private static HechosSimit SimitLimpio() => new(0, true, false, false, []);

    private static HechosSimit Simit(string caso) => caso switch
    {
        "comparendos" => new HechosSimit(1, true, false, false, []),
        "paz" => new HechosSimit(0, false, false, false, []),
        "suspendida" => new HechosSimit(0, true, true, false, []),
        "cancelada" => new HechosSimit(0, true, false, true, []),
        "saldo" => new HechosSimit(0, true, false, false, [new AcuerdoPago(1)]),
        _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso de SIMIT no previsto."),
    };

    private static readonly Dictionary<string, string> Plantillas = new(StringComparer.Ordinal)
    {
        ["SOAT_NO_VIGENTE"] = "El SOAT no está vigente, así que el traspaso queda bloqueado.",
        ["RTM_NO_VIGENTE"] =
            "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.",
        ["ESTADO_VEHICULO"] = "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.",
        ["SIMIT_VEHICULO"] =
            "El vehículo tiene comparendos, no está en paz y salvo, figura suspendido o cancelado, o tiene un acuerdo de pago que no está al día. Es una advertencia.",
        ["SIMIT_PROPIETARIO"] =
            "El propietario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia.",
        ["SIMIT_LOCATARIO"] =
            "El locatario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia. En leasing, si no hay documento, no se pudo consultar su deuda.",
        ["LIMITACION"] = "El vehículo tiene una limitación a la propiedad. Es una advertencia.",
        ["GRAVAMEN"] = "El vehículo tiene un gravamen registrado. Es una advertencia.",
        ["GARANTIA_MOBILIARIA"] = "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.",
        ["PRENDA"] = "El vehículo tiene una prenda registrada. Es una advertencia.",
        ["INFRACCION_RUNT"] =
            "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.",
        ["LICENCIA_CONDUCTOR"] =
            "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.",
        ["IMPUESTO"] = "El vehículo tiene impuesto departamental pendiente. Es una advertencia.",
        ["FUENTE_PENDIENTE"] =
            "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.",
    };
}
