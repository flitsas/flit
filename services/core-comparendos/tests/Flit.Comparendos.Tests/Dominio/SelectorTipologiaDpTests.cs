using Flit.Comparendos.Domain.Dp;
using Flit.Comparendos.Domain.Plazos;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

/// <summary>HU #14608: la decisión de qué derecho de petición aplica a un comparendo (reglas de V1, sin enviar nada).</summary>
public sealed class SelectorTipologiaDpTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 18);
    private static readonly SelectorTipologiaDp Selector = new(ReglasDpAutomatico.V1);

    private static readonly IReadOnlySet<string> Todas = new HashSet<string> { "DCI", "DBN", "DPR", "DLL", "DRP" };

    /// <summary>Comparendo de agente, vigente, de un propietario persona natural y sin nada que aplique: base de cada caso.</summary>
    private static DatosDeSeleccion Base(
        string? codigo = "C29",
        DateOnly? imposicion = null,
        DateOnly? notificacion = null,
        bool? fotomulta = false,
        bool activo = true,
        bool pagado = false,
        bool tieneDp = false,
        bool juridica = false,
        Descuento descuento = Descuento.Ninguno) =>
        new(codigo, imposicion ?? Hoy.AddDays(-10), notificacion ?? Hoy.AddDays(-9), fotomulta, activo, pagado, tieneDp, juridica, descuento);

    private static string[] Codigos(DecisionDp d) => d.Aplicables.Select(a => a.Codigo).ToArray();

    // ---------- AC1: una regla por tipología ----------

    [Fact]
    public void Caducidad_aplica_con_mas_de_un_anio_y_un_dia_desde_la_imposicion()
    {
        // 2025-11-01 + 1 año + 1 día = 2026-11-02, anterior a hoy.
        var d = Selector.Decidir(Base(imposicion: new DateOnly(2025, 11, 1), notificacion: new DateOnly(2025, 11, 3)), Hoy, Todas);

        Codigos(d).Should().Equal("DCI");
        d.Elegida.Should().Be("DCI");
    }

    [Fact]
    public void Caducidad_no_aplica_el_dia_exacto_en_que_vence_el_margen()
    {
        // 2025-11-17 + 1 año + 1 día = 2026-11-18 = hoy: todavía no es anterior.
        var d = Selector.Decidir(Base(imposicion: new DateOnly(2025, 11, 17), notificacion: new DateOnly(2025, 11, 18)), Hoy, Todas);

        Codigos(d).Should().BeEmpty();
        d.Motivo.Should().Be(MotivoNoElegible.SinTipologiaAplicable);
    }

    [Fact]
    public void Caducidad_cuenta_desde_la_notificacion_si_el_abogado_cambia_la_base()
    {
        var reglas = ReglasDpAutomatico.V1 with { BaseCaducidad = BasePlazo.Notificacion };
        var datos = Base(imposicion: new DateOnly(2025, 6, 1), notificacion: new DateOnly(2026, 6, 1));

        // Desde la imposición (V1) ya pasó más de un año; desde la notificación (como el motor de plazos) todavía no.
        // DBN aplica en ambos casos porque la notificación llegó un año después de la imposición.
        Codigos(Selector.Decidir(datos, Hoy, Todas)).Should().Contain("DCI");
        Codigos(new SelectorTipologiaDp(reglas).Decidir(datos, Hoy, Todas)).Should().NotContain("DCI").And.Contain("DBN");
    }

    [Fact]
    public void Mala_notificacion_aplica_cuando_pasan_mas_de_treinta_dias()
    {
        var d = Selector.Decidir(Base(imposicion: new DateOnly(2026, 8, 1), notificacion: new DateOnly(2026, 9, 2)), Hoy, Todas);

        Codigos(d).Should().Equal("DBN");
    }

    [Fact]
    public void Mala_notificacion_no_aplica_con_treinta_dias_justos()
    {
        var d = Selector.Decidir(Base(imposicion: new DateOnly(2026, 8, 1), notificacion: new DateOnly(2026, 8, 31)), Hoy, Todas);

        Codigos(d).Should().BeEmpty();
    }

    [Fact]
    public void Mala_notificacion_excluye_las_infracciones_de_la_c321()
    {
        var d = Selector.Decidir(Base(codigo: "D02", imposicion: new DateOnly(2026, 8, 1), notificacion: new DateOnly(2026, 10, 1)), Hoy, Todas);

        Codigos(d).Should().NotContain("DBN");
    }

    [Fact]
    public void Prescripcion_aplica_con_tres_anios_desde_la_notificacion()
    {
        var d = Selector.Decidir(Base(imposicion: new DateOnly(2023, 10, 1), notificacion: new DateOnly(2023, 11, 18)), Hoy, Todas);

        Codigos(d).Should().Contain("DPR");
    }

    [Fact]
    public void Prescripcion_no_aplica_un_dia_antes()
    {
        var d = Selector.Decidir(Base(imposicion: new DateOnly(2023, 11, 15), notificacion: new DateOnly(2023, 11, 19)), Hoy, Todas);

        Codigos(d).Should().NotContain("DPR");
    }

    [Fact]
    public void Persona_juridica_aplica_a_un_propietario_con_nit()
    {
        var d = Selector.Decidir(Base(juridica: true), Hoy, Todas);

        Codigos(d).Should().Equal("DLL");
    }

    [Fact]
    public void Persona_juridica_no_aplica_a_persona_natural_ni_a_la_c321()
    {
        Selector.Decidir(Base(juridica: false), Hoy, Todas).Aplicables.Should().BeEmpty();
        Codigos(Selector.Decidir(Base(codigo: "C35", juridica: true, imposicion: new DateOnly(2023, 1, 1), notificacion: new DateOnly(2023, 1, 2)), Hoy, Todas))
            .Should().NotContain("DLL");
    }

    [Fact]
    public void Persona_juridica_solo_de_camara_si_el_abogado_lo_exige()
    {
        var reglas = ReglasDpAutomatico.V1 with { DllSoloFotomulta = true };

        new SelectorTipologiaDp(reglas).Decidir(Base(juridica: true, fotomulta: false), Hoy, Todas).Aplicables.Should().BeEmpty();
        Codigos(new SelectorTipologiaDp(reglas).Decidir(Base(juridica: true, fotomulta: true), Hoy, Todas)).Should().Equal("DLL");
    }

    [Theory]
    [InlineData("C35")]
    [InlineData("d02")]
    [InlineData(" D02 ")]
    public void Revocatoria_aplica_a_c35_y_d02_dentro_del_periodo(string codigo)
    {
        var d = Selector.Decidir(Base(codigo: codigo, juridica: true, imposicion: new DateOnly(2021, 5, 4), notificacion: new DateOnly(2021, 5, 5)), Hoy, Todas);

        Codigos(d).Should().Contain("DRP");
    }

    [Theory]
    [InlineData(2020, 1, 5, false)]
    [InlineData(2020, 1, 6, true)]
    [InlineData(2022, 9, 14, true)]
    [InlineData(2022, 9, 15, false)]
    public void Revocatoria_respeta_los_extremos_del_periodo(int anio, int mes, int dia, bool aplica)
    {
        var imposicion = new DateOnly(anio, mes, dia);
        var d = Selector.Decidir(Base(codigo: "C35", juridica: true, imposicion: imposicion, notificacion: imposicion.AddDays(1)), Hoy, Todas);

        Codigos(d).Contains("DRP").Should().Be(aplica);
    }

    [Fact]
    public void Revocatoria_no_aplica_a_otra_infraccion()
    {
        var d = Selector.Decidir(Base(codigo: "C29", juridica: true, imposicion: new DateOnly(2021, 5, 4), notificacion: new DateOnly(2021, 5, 5)), Hoy, Todas);

        Codigos(d).Should().NotContain("DRP");
    }

    [Fact]
    public void Sin_fechas_no_aplican_las_reglas_que_las_necesitan()
    {
        var datos = new DatosDeSeleccion("C29", null, null, false, true, false, false, false, Descuento.Ninguno);

        Selector.Decidir(datos, Hoy, Todas).Aplicables.Should().BeEmpty();
    }

    // ---------- AC2: prioridad por fuerza jurídica ----------

    [Fact]
    public void Con_varias_aplicables_elige_una_sola_en_el_orden_de_prioridad_y_devuelve_todas()
    {
        // Persona jurídica, imposición vieja y notificada tarde y hace más de 3 años: aplican DPR, DCI, DBN y DLL.
        var d = Selector.Decidir(Base(juridica: true, imposicion: new DateOnly(2022, 1, 10), notificacion: new DateOnly(2022, 3, 10)), Hoy, Todas);

        Codigos(d).Should().Equal("DPR", "DCI", "DBN", "DLL");
        d.Elegida.Should().Be("DPR");
        d.Aplicables.Should().OnlyContain(a => a.Razon.Length > 0);
    }

    [Fact]
    public void Si_la_de_mayor_prioridad_no_esta_disponible_elige_la_siguiente()
    {
        var d = Selector.Decidir(
            Base(juridica: true, imposicion: new DateOnly(2022, 1, 10), notificacion: new DateOnly(2022, 3, 10)),
            Hoy,
            new HashSet<string> { "DBN", "DLL" });

        d.Elegida.Should().Be("DBN");
        d.Aplicables.Single(a => a.Codigo == "DPR").Disponible.Should().BeFalse();
    }

    [Fact]
    public void Si_ninguna_aplicable_esta_disponible_lo_dice()
    {
        var d = Selector.Decidir(Base(juridica: true), Hoy, new HashSet<string>());

        d.EsElegible.Should().BeFalse();
        d.Motivo.Should().Be(MotivoNoElegible.SinTipologiaDisponible);
        Codigos(d).Should().Equal("DLL");
    }

    [Fact]
    public void El_orden_de_prioridad_es_un_parametro()
    {
        var reglas = ReglasDpAutomatico.V1 with { Prioridad = ["DLL", "DBN", "DCI", "DPR", "DRP"] };
        var d = new SelectorTipologiaDp(reglas).Decidir(Base(juridica: true, imposicion: new DateOnly(2022, 1, 10), notificacion: new DateOnly(2022, 3, 10)), Hoy, Todas);

        d.Elegida.Should().Be("DLL");
    }

    // ---------- AC3: exclusiones ----------

    [Fact]
    public void Un_comparendo_pagado_no_es_elegible()
    {
        var d = Selector.Decidir(Base(juridica: true, pagado: true), Hoy, Todas);

        d.EsElegible.Should().BeFalse();
        d.Motivo.Should().Be(MotivoNoElegible.Pagado);
        // Aunque no sea elegible, debe poder mostrarse qué se podría alegar.
        Codigos(d).Should().Equal("DLL");
    }

    [Fact]
    public void Un_comparendo_inactivo_no_es_elegible()
    {
        Selector.Decidir(Base(juridica: true, activo: false), Hoy, Todas).Motivo.Should().Be(MotivoNoElegible.Inactivo);
    }

    [Fact]
    public void Un_comparendo_que_ya_tiene_dp_no_es_elegible()
    {
        Selector.Decidir(Base(juridica: true, tieneDp: true), Hoy, Todas).Motivo.Should().Be(MotivoNoElegible.YaTieneDp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void Una_fotomulta_con_el_cincuenta_por_ciento_vigente_espera(bool? fotomulta)
    {
        var d = Selector.Decidir(Base(juridica: true, fotomulta: fotomulta, descuento: Descuento.Cincuenta), Hoy, Todas);

        d.EsElegible.Should().BeFalse();
        d.Motivo.Should().Be(MotivoNoElegible.VentanaDescuento);
    }

    [Fact]
    public void Una_fotomulta_con_el_veinticinco_o_sin_descuento_si_es_elegible()
    {
        Selector.Decidir(Base(juridica: true, fotomulta: true, descuento: Descuento.Veinticinco), Hoy, Todas).EsElegible.Should().BeTrue();
        Selector.Decidir(Base(juridica: true, fotomulta: true, descuento: Descuento.Ninguno), Hoy, Todas).EsElegible.Should().BeTrue();
    }

    [Fact]
    public void Un_comparendo_de_agente_con_el_cincuenta_por_ciento_vigente_no_espera()
    {
        // El traslado al conductor con descuento es de la fotomulta; un comparendo físico no queda detenido por la ventana.
        Selector.Decidir(Base(juridica: true, fotomulta: false, descuento: Descuento.Cincuenta), Hoy, Todas).EsElegible.Should().BeTrue();
    }

    [Fact]
    public void La_exclusion_gana_sobre_cualquier_tipologia_disponible()
    {
        var d = Selector.Decidir(Base(juridica: true, pagado: true, tieneDp: true), Hoy, Todas);

        d.Elegida.Should().BeNull();
    }

    // ---------- AC4: sin tope silencioso ----------

    [Fact]
    public void Evalua_cien_comparendos_sin_limite_oculto()
    {
        var comparendos = Enumerable.Range(0, 100).Select(_ => Base(juridica: true)).ToList();

        var decisiones = comparendos.Select(c => Selector.Decidir(c, Hoy, Todas)).ToList();

        decisiones.Should().HaveCount(100);
        decisiones.Should().OnlyContain(d => d.EsElegible);
    }

    // ---------- Contrato ----------

    [Fact]
    public void Es_una_funcion_pura()
    {
        var datos = Base(juridica: true, imposicion: new DateOnly(2022, 1, 10), notificacion: new DateOnly(2022, 3, 10));

        var a = Selector.Decidir(datos, Hoy, Todas);
        var b = Selector.Decidir(datos, Hoy, Todas);

        a.Elegida.Should().Be(b.Elegida);
        Codigos(a).Should().Equal(Codigos(b));
    }

    [Fact]
    public void Rechaza_argumentos_nulos()
    {
        FluentActions.Invoking(() => Selector.Decidir(null!, Hoy, Todas)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Selector.Decidir(Base(), Hoy, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new SelectorTipologiaDp(null!)).Should().Throw<ArgumentNullException>();
    }
}
