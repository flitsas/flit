using Flit.Diagnostico.Domain.Dictamen;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Catálogo v2 de TRASPASO (ola de ajustes 2026-11-11, punto 2 «Multas bloqueantes»). Las multas del SIMIT (comparendos
/// o sin paz y salvo) del vehículo y del propietario —o del locatario en leasing— bloquean en TRASPASO_STANDARD y en
/// TRASPASO_TRANSFERENCIA_DE_DOMINIO y advierten en TRASPASO_UNILATERAL. La falta de una consulta SIMIT exigida da
/// SIMIT_SIN_VERIFICAR con el mismo efecto. Licencia y acuerdos de pago solo advierten.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --filter FullyQualifiedName~MultasDictamenV2Tests
/// </code>
/// </example>
public sealed class MultasDictamenV2Tests
{
    private const string Standard = "TRASPASO_STANDARD";
    private const string Unilateral = "TRASPASO_UNILATERAL";
    private const string Transferencia = "TRASPASO_TRANSFERENCIA_DE_DOMINIO";

    private static readonly DateOnly Hoy = new(2026, 11, 11);

    private static readonly string[] CodigosV2 =
    [
        "SOAT_NO_VIGENTE",
        "SOAT_MAQUINARIA",
        "RTM_NO_VIGENTE",
        "ESTADO_VEHICULO",
        "SIMIT_VEHICULO",
        "SIMIT_PROPIETARIO",
        "SIMIT_LOCATARIO",
        "SIMIT_SIN_VERIFICAR",
        "SIMIT_LICENCIA",
        "SIMIT_ACUERDO_PAGO",
        "LIMITACION",
        "GRAVAMEN",
        "GARANTIA_MOBILIARIA",
        "PRENDA",
        "INFRACCION_RUNT",
        "LICENCIA_CONDUCTOR",
        "IMPUESTO",
        "FUENTE_PENDIENTE",
    ];

    // ---- AC1 / AC3: multas del vehículo ----

    [Theory]
    [InlineData(Standard, "comparendos")]
    [InlineData(Standard, "paz")]
    [InlineData(Transferencia, "comparendos")]
    [InlineData(Transferencia, "paz")]
    public void Multas_del_vehiculo_bloquean(string tipo, string caso)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(tipo) with { SimitVehiculo = Simit(caso) });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_VEHICULO", "SIMIT_VEHICULO", "BLOQUEA", null));
    }

    // ---- AC2 / AC3: multas del propietario (no leasing) ----

    [Theory]
    [InlineData(Standard, "comparendos")]
    [InlineData(Standard, "paz")]
    [InlineData(Transferencia, "comparendos")]
    [InlineData(Transferencia, "paz")]
    public void Multas_del_propietario_bloquean(string tipo, string caso)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(tipo) with { SimitPropietario = Simit(caso) });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_PROPIETARIO", "SIMIT_PROPIETARIO", "BLOQUEA", null));
    }

    // ---- AC2: multas del locatario (leasing) ----

    [Theory]
    [InlineData(Standard, "comparendos")]
    [InlineData(Standard, "paz")]
    [InlineData(Transferencia, "comparendos")]
    [InlineData(Transferencia, "paz")]
    public void En_leasing_las_multas_del_locatario_bloquean(string tipo, string caso)
    {
        var resultado = EvaluadorDictamen.Evaluar(Leasing(tipo) with { SimitLocatario = Simit(caso) });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_LOCATARIO", "SIMIT_LOCATARIO", "BLOQUEA", null));
    }

    [Fact]
    public void En_leasing_las_multas_del_propietario_no_se_evaluan()
    {
        var resultado = EvaluadorDictamen.Evaluar(Leasing(Standard) with { SimitPropietario = Simit("comparendos") });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().BeEmpty();
    }

    // ---- V2: en unilateral las multas advierten (art. 5.3.2.2 no exige paz y salvo) ----

    [Theory]
    [InlineData("vehiculo")]
    [InlineData("propietario")]
    [InlineData("locatario")]
    public void En_unilateral_las_multas_advierten_y_el_traspaso_sigue_viable(string rol)
    {
        var hechos = rol switch
        {
            "vehiculo" => Base(Unilateral) with { SimitVehiculo = Simit("comparendos") },
            "propietario" => Base(Unilateral) with { SimitPropietario = Simit("paz") },
            _ => Leasing(Unilateral) with { SimitLocatario = Simit("comparendos") },
        };
        var codigo = rol switch
        {
            "vehiculo" => "SIMIT_VEHICULO",
            "propietario" => "SIMIT_PROPIETARIO",
            _ => "SIMIT_LOCATARIO",
        };

        var resultado = EvaluadorDictamen.Evaluar(hechos);

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(new ReglaEvaluada(codigo, codigo, "ADVIERTE", null));
    }

    // ---- AC6: acuerdo de pago con saldo, sin comparendos y con paz y salvo ----

    [Theory]
    [InlineData(Standard)]
    [InlineData(Unilateral)]
    [InlineData(Transferencia)]
    public void Acuerdo_con_saldo_sin_multas_advierte_y_sigue_viable(string tipo)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(tipo) with { SimitPropietario = Simit("saldo") });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_ACUERDO_PAGO", "SIMIT_PROPIETARIO", "ADVIERTE", null));
    }

    [Fact]
    public void Acuerdo_con_saldo_del_vehiculo_y_del_locatario_advierte_con_su_fuente()
    {
        var resultado = EvaluadorDictamen.Evaluar(Leasing(Standard) with
        {
            SimitVehiculo = Simit("saldo"),
            SimitLocatario = Simit("saldo"),
        });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Select(r => (r.Codigo, r.Fuente, r.Efecto)).Should().Equal(
            ("SIMIT_ACUERDO_PAGO", "SIMIT_VEHICULO", "ADVIERTE"),
            ("SIMIT_ACUERDO_PAGO", "SIMIT_LOCATARIO", "ADVIERTE"));
    }

    [Fact]
    public void Acuerdo_con_saldo_y_sin_paz_y_salvo_bloquea_por_la_regla_de_multas()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            SimitPropietario = new HechosSimit(0, false, false, false, [new AcuerdoPago(250_000)]),
        });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Select(r => (r.Codigo, r.Efecto)).Should().Equal(
            ("SIMIT_PROPIETARIO", "BLOQUEA"),
            ("SIMIT_ACUERDO_PAGO", "ADVIERTE"));
    }

    [Fact]
    public void Acuerdo_al_dia_no_dispara()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            SimitPropietario = new HechosSimit(0, true, false, false, [new AcuerdoPago(0)]),
        });

        resultado.Reglas.Should().BeEmpty();
    }

    // ---- AC5: licencia solo advierte ----

    [Theory]
    [InlineData(Standard, "suspendida")]
    [InlineData(Standard, "cancelada")]
    [InlineData(Transferencia, "suspendida")]
    [InlineData(Unilateral, "cancelada")]
    public void Licencia_suspendida_o_cancelada_en_simit_advierte_y_no_cambia_el_veredicto(string tipo, string caso)
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(tipo) with { SimitPropietario = Simit(caso) });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_LICENCIA", "SIMIT_PROPIETARIO", "ADVIERTE", null));
    }

    [Fact]
    public void Licencia_en_simit_junto_a_multas_sigue_bloqueado_por_las_multas()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            SimitPropietario = new HechosSimit(2, true, true, false, []),
        });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Select(r => (r.Codigo, r.Efecto)).Should().Equal(
            ("SIMIT_PROPIETARIO", "BLOQUEA"),
            ("SIMIT_LICENCIA", "ADVIERTE"));
    }

    [Theory]
    [InlineData("SUSPENDIDA", true)]
    [InlineData("VENCIDA", true)]
    [InlineData("CANCELADA", true)]
    [InlineData("ACTIVA", false)]
    public void Licencia_conductor_del_runt_advierte_y_no_cambia_el_veredicto(string estado, bool certificado)
    {
        var viable = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            LicenciaPropietario = new LicenciaConductor(estado, certificado),
        });
        var bloqueado = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            LicenciaPropietario = new LicenciaConductor(estado, certificado),
            SimitVehiculo = Simit("comparendos"),
        });

        viable.Veredicto.Should().Be("VIABLE");
        viable.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("LICENCIA_CONDUCTOR", "RUNT_PROPIETARIO", "ADVIERTE", null));
        bloqueado.Veredicto.Should().Be("BLOQUEADO");
        bloqueado.Reglas.Single(r => r.Codigo == "LICENCIA_CONDUCTOR").Efecto.Should().Be("ADVIERTE");
    }

    [Fact]
    public void Infraccion_del_runt_sigue_advirtiendo()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Standard) with { Runt = Runt() with { Infracciones = 3 } });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("INFRACCION_RUNT", "RUNT", "ADVIERTE", null));
    }

    // ---- AC4: falta una consulta SIMIT exigida ----

    [Theory]
    [InlineData(EstadoFuenteDictamen.Error)]
    [InlineData(EstadoFuenteDictamen.SinResultado)]
    [InlineData(EstadoFuenteDictamen.NoAplica)]
    public void Sin_simit_del_vehiculo_bloquea_y_al_responder_queda_viable(EstadoFuenteDictamen estado)
    {
        var sinConsulta = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            EstadoSimitVehiculo = estado,
            SimitVehiculo = null,
        });
        var respondio = EvaluadorDictamen.Evaluar(Base(Standard));

        sinConsulta.Veredicto.Should().Be("BLOQUEADO");
        sinConsulta.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_SIN_VERIFICAR", "SIMIT_VEHICULO", "BLOQUEA", null));
        sinConsulta.Reglas.Should().NotContain(r => r.Codigo == "FUENTE_PENDIENTE");
        respondio.Veredicto.Should().Be("VIABLE");
        respondio.Reglas.Should().BeEmpty();
    }

    [Fact]
    public void Sin_simit_del_propietario_bloquea_en_transferencia()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Transferencia) with
        {
            EstadoSimitPropietario = EstadoFuenteDictamen.Error,
            SimitPropietario = null,
        });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_SIN_VERIFICAR", "SIMIT_PROPIETARIO", "BLOQUEA", null));
    }

    [Fact]
    public void En_leasing_sin_documento_del_locatario_bloquea_porque_no_se_pudo_verificar()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            Runt = Runt() with { EsLeasing = true, HayDocumentoLocatario = false },
        });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_SIN_VERIFICAR", "SIMIT_LOCATARIO", "BLOQUEA", null));
    }

    [Fact]
    public void En_leasing_con_documento_y_sin_lectura_del_locatario_bloquea_y_al_responder_queda_viable()
    {
        var sinLectura = EvaluadorDictamen.Evaluar(Leasing(Standard) with { SimitLocatario = null });
        var respondio = EvaluadorDictamen.Evaluar(Leasing(Standard));

        sinLectura.Veredicto.Should().Be("BLOQUEADO");
        sinLectura.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_SIN_VERIFICAR", "SIMIT_LOCATARIO", "BLOQUEA", null));
        respondio.Veredicto.Should().Be("VIABLE");
    }

    [Fact]
    public void En_leasing_la_falta_del_simit_del_propietario_no_bloquea()
    {
        var resultado = EvaluadorDictamen.Evaluar(Leasing(Standard) with
        {
            EstadoSimitPropietario = EstadoFuenteDictamen.Error,
            SimitPropietario = null,
        });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().BeEmpty();
    }

    [Fact]
    public void Si_faltan_varias_consultas_simit_hay_un_solo_motivo()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            EstadoSimitVehiculo = EstadoFuenteDictamen.Error,
            SimitVehiculo = null,
            EstadoSimitPropietario = EstadoFuenteDictamen.SinResultado,
            SimitPropietario = null,
        });

        resultado.Veredicto.Should().Be("BLOQUEADO");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_SIN_VERIFICAR", "SIMIT_VEHICULO", "BLOQUEA", null));
    }

    [Fact]
    public void En_unilateral_la_falta_de_simit_advierte()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Unilateral) with
        {
            EstadoSimitVehiculo = EstadoFuenteDictamen.Error,
            SimitVehiculo = null,
        });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("SIMIT_SIN_VERIFICAR", "SIMIT_VEHICULO", "ADVIERTE", null));
    }

    [Fact]
    public void Fuente_pendiente_sigue_para_runt_y_runt_del_propietario_y_no_bloquea()
    {
        var resultado = EvaluadorDictamen.Evaluar(Base(Standard) with
        {
            EstadoRuntPropietario = EstadoFuenteDictamen.Error,
            LicenciaPropietario = null,
        });

        resultado.Veredicto.Should().Be("VIABLE");
        resultado.Reglas.Should().ContainSingle().Which.Should().Be(
            new ReglaEvaluada("FUENTE_PENDIENTE", "RUNT_PROPIETARIO", "ADVIERTE", null));
    }

    // ---- AC7 / AC8: catálogo v2 y plantillas ----

    [Fact]
    public void El_catalogo_vigente_es_la_version_2_con_dieciocho_reglas_por_tipo()
    {
        CatalogoReglasDictamen.Version.Should().Be(2);
        CatalogoReglasDictamen.Filas.Should().HaveCount(54);
        CatalogoReglasDictamen.Filas.Should().OnlyContain(f => f.CatalogoVersion == 2 && f.PlantillaTexto.Length > 0);
        foreach (var tipo in CatalogoReglasDictamen.TiposTramite)
        {
            CatalogoReglasDictamen.Filas.Where(f => f.TipoTramiteCodigo == tipo).Select(f => f.Codigo)
                .Should().Equal(CodigosV2);
        }
    }

    [Theory]
    [InlineData(Standard, "SIMIT_VEHICULO", "BLOQUEA")]
    [InlineData(Standard, "SIMIT_PROPIETARIO", "BLOQUEA")]
    [InlineData(Standard, "SIMIT_LOCATARIO", "BLOQUEA")]
    [InlineData(Standard, "SIMIT_SIN_VERIFICAR", "BLOQUEA")]
    [InlineData(Transferencia, "SIMIT_VEHICULO", "BLOQUEA")]
    [InlineData(Transferencia, "SIMIT_PROPIETARIO", "BLOQUEA")]
    [InlineData(Transferencia, "SIMIT_LOCATARIO", "BLOQUEA")]
    [InlineData(Transferencia, "SIMIT_SIN_VERIFICAR", "BLOQUEA")]
    [InlineData(Unilateral, "SIMIT_VEHICULO", "ADVIERTE")]
    [InlineData(Unilateral, "SIMIT_PROPIETARIO", "ADVIERTE")]
    [InlineData(Unilateral, "SIMIT_LOCATARIO", "ADVIERTE")]
    [InlineData(Unilateral, "SIMIT_SIN_VERIFICAR", "ADVIERTE")]
    [InlineData(Standard, "SIMIT_LICENCIA", "ADVIERTE")]
    [InlineData(Unilateral, "SIMIT_LICENCIA", "ADVIERTE")]
    [InlineData(Transferencia, "SIMIT_LICENCIA", "ADVIERTE")]
    [InlineData(Standard, "SIMIT_ACUERDO_PAGO", "ADVIERTE")]
    [InlineData(Unilateral, "SIMIT_ACUERDO_PAGO", "ADVIERTE")]
    [InlineData(Transferencia, "SIMIT_ACUERDO_PAGO", "ADVIERTE")]
    [InlineData(Standard, "LICENCIA_CONDUCTOR", "ADVIERTE")]
    [InlineData(Standard, "INFRACCION_RUNT", "ADVIERTE")]
    [InlineData(Standard, "SOAT_NO_VIGENTE", "BLOQUEA")]
    [InlineData(Unilateral, "RTM_NO_VIGENTE", "ADVIERTE")]
    [InlineData(Standard, "RTM_NO_VIGENTE", "BLOQUEA")]
    [InlineData(Transferencia, "RTM_NO_VIGENTE", "BLOQUEA")]
    [InlineData(Standard, "SOAT_MAQUINARIA", "ADVIERTE")]
    [InlineData(Unilateral, "SOAT_MAQUINARIA", "ADVIERTE")]
    [InlineData(Standard, "FUENTE_PENDIENTE", "ADVIERTE")]
    public void Efecto_de_cada_regla_v2_por_tipo(string tipo, string codigo, string efecto)
    {
        CatalogoReglasDictamen.Filas.Single(f => f.TipoTramiteCodigo == tipo && f.Codigo == codigo)
            .Efecto.Should().Be(efecto);
    }

    [Fact]
    public void Las_reglas_que_bloquean_no_dicen_que_son_advertencia_y_las_que_advierten_si()
    {
        foreach (var fila in CatalogoReglasDictamen.Filas)
        {
            if (fila.Efecto == "BLOQUEA")
                fila.PlantillaTexto.Should().NotContainEquivalentOf("advertencia", $"{fila.TipoTramiteCodigo}/{fila.Codigo} bloquea");
            else if (fila.Codigo.StartsWith("SIMIT_", StringComparison.Ordinal))
                fila.PlantillaTexto.Should().ContainEquivalentOf("advertencia", $"{fila.TipoTramiteCodigo}/{fila.Codigo} advierte");
        }
    }

    [Fact]
    public void Las_multas_ya_no_mencionan_licencia_ni_acuerdo_y_la_del_vehiculo_no_se_atribuye_a_la_norma()
    {
        foreach (var fila in CatalogoReglasDictamen.Filas.Where(f => f.Codigo is "SIMIT_VEHICULO" or "SIMIT_PROPIETARIO" or "SIMIT_LOCATARIO"))
        {
            fila.PlantillaTexto.Should().ContainEquivalentOf("multas");
            fila.PlantillaTexto.Should().NotContainEquivalentOf("licencia");
            fila.PlantillaTexto.Should().NotContainEquivalentOf("acuerdo");
        }

        foreach (var fila in CatalogoReglasDictamen.Filas.Where(f => f.Codigo == "SIMIT_VEHICULO"))
        {
            fila.PlantillaTexto.Should().NotContainEquivalentOf("norma");
            fila.PlantillaTexto.Should().NotContainEquivalentOf("resolución");
            fila.PlantillaTexto.Should().NotContainEquivalentOf("ley");
            fila.PlantillaTexto.Should().NotContainEquivalentOf("exige");
        }
    }

    [Fact]
    public void Las_plantillas_v2_de_traspaso_standard_son_las_de_la_ficha()
    {
        CatalogoReglasDictamen.Filas.Where(f => f.TipoTramiteCodigo == Standard)
            .ToDictionary(f => f.Codigo, f => f.PlantillaTexto)
            .Should().Equal(PlantillasStandardV2);
    }

    [Fact]
    public void La_version_1_se_conserva_intacta()
    {
        var v1 = CatalogoReglasDictamen.FilasDe(1);

        CatalogoReglasDictamen.FilasV1.Should().Equal(v1);
        v1.Should().HaveCount(42);
        v1.Should().OnlyContain(f => f.CatalogoVersion == 1);
        v1.Where(f => f.Efecto == "BLOQUEA").Select(f => f.Codigo).Distinct()
            .Should().BeEquivalentTo("SOAT_NO_VIGENTE", "RTM_NO_VIGENTE");
        v1.Select(f => f.Codigo).Should().NotContain(["SIMIT_SIN_VERIFICAR", "SIMIT_LICENCIA", "SIMIT_ACUERDO_PAGO"]);
        v1.Single(f => f.TipoTramiteCodigo == Standard && f.Codigo == "SIMIT_VEHICULO").PlantillaTexto.Should().Be(
            "El vehículo tiene comparendos, no está en paz y salvo, figura suspendido o cancelado, "
            + "o tiene un acuerdo de pago que no está al día. Es una advertencia.");
        CatalogoReglasDictamen.FilasDe(2).Should().Equal(CatalogoReglasDictamen.Filas);
    }

    [Fact]
    public void El_historico_suma_v1_y_v2_sin_repetir_clave()
    {
        CatalogoReglasDictamen.Historico.Should().HaveCount(96);
        CatalogoReglasDictamen.Historico.Select(f => (f.CatalogoVersion, f.TipoTramiteCodigo, f.Codigo))
            .Should().OnlyHaveUniqueItems();
    }

    private static readonly Dictionary<string, string> PlantillasStandardV2 = new(StringComparer.Ordinal)
    {
        ["SOAT_NO_VIGENTE"] = "El SOAT no está vigente, así que el traspaso queda bloqueado.",
        ["SOAT_MAQUINARIA"] =
            "La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.",
        ["RTM_NO_VIGENTE"] =
            "La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula "
            + "para motos, motocarros y servicio público; 5 años para particulares), así que el traspaso queda bloqueado.",
        ["ESTADO_VEHICULO"] = "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.",
        ["SIMIT_VEHICULO"] =
            "El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. "
            + "Por política de FLIT el traspaso queda bloqueado hasta que se pongan al día.",
        ["SIMIT_PROPIETARIO"] =
            "El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. "
            + "El traspaso queda bloqueado hasta que se ponga al día.",
        ["SIMIT_LOCATARIO"] =
            "El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. "
            + "El traspaso queda bloqueado hasta que se ponga al día.",
        ["SIMIT_SIN_VERIFICAR"] =
            "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el "
            + "documento del locatario. El traspaso queda bloqueado hasta completar la consulta.",
        ["SIMIT_LICENCIA"] =
            "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto.",
        ["SIMIT_ACUERDO_PAGO"] =
            "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto.",
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

    private static HechosDictamen Base(string tipo) => new(
        tipo,
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

    private static HechosDictamen Leasing(string tipo) => Base(tipo) with
    {
        Runt = Runt() with { EsLeasing = true, HayDocumentoLocatario = true },
        SimitLocatario = SimitLimpio(),
        LicenciaLocatario = new LicenciaConductor("ACTIVA", true),
    };

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
}
