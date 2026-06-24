using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class TraspasoGatesTests
{
    private static TraspasoGateContext BaseCtx() => new()
    {
        TramiteRadicado = true,
        VehiculoConsultado = true,
        Preflight = new PreflightSnapshot("green", ImpuestoVehicularUnknown: false),
        PazSalvoImpuestoVerificado = true,
        Vendedor = new ParteDatos("V", "111", "v@x.co"),
        RuntVendedor = new RuntSnapshot(Consultado: true, "111"),
        Comprador = new ParteDatos("C", "222", "c@x.co"),
        RuntComprador = new RuntSnapshot(Consultado: true, "222"),
        SimitComprador = new SimitSnapshot(Consultado: true, "222", TotalComparendos: 0),
        ValorVenta = 50_000_000m,
        Biometria = new BiometriaSnapshot(Vendedor: true, Comprador: true),
        DocumentosObligatoriosCompletos = true,
    };

    [Fact]
    public void Paso6_SinGateDocumentos_Avanza()
    {
        // Los documentos se exigen ahora en el paso 2; el paso 6 (FUR) ya NO bloquea por docs.
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false };
        TraspasoGates.PasoCompleto(6, ctx).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso6_DocumentosCompletos_Avanza()
    {
        TraspasoGates.PasoCompleto(6, BaseCtx()).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso2_DocumentosIncompletos_Bloquea()
    {
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false };
        var r = TraspasoGates.PasoCompleto(2, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("documentos_incompletos");
    }

    [Fact]
    public void Paso2_DocumentosIncompletos_ForzarNoBypass()
    {
        // Gating ESTRICTO de documentos (igual que matrícula): forzar no omite obligatorios.
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false, ForzarContinuar = true };
        TraspasoGates.PasoCompleto(2, ctx).Code.Should().Be("documentos_incompletos");
    }

    [Fact]
    public void MaxPasoAlcanzable_DocumentosIncompletos_Es2()
    {
        // Documentos viven en el paso 2 → docs incompletos bloquean en 2 → max alcanzable = 2.
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false };
        TraspasoGates.MaxPasoAlcanzable(ctx).Should().Be(2);
    }

    [Fact]
    public void Paso1_SinRadicar_Bloquea()
    {
        var ctx = BaseCtx() with { TramiteRadicado = false };
        var r = TraspasoGates.PasoCompleto(1, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("sin_radicado");
    }

    [Fact]
    public void Paso1_SinConsultaVehiculo_Bloquea()
    {
        // Un traspaso recién creado (sin placa consultada) debe quedarse en el paso 1.
        var ctx = BaseCtx() with { VehiculoConsultado = false };
        var r = TraspasoGates.PasoCompleto(1, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("consulta_pendiente");
    }

    [Fact]
    public void MaxPasoAlcanzable_SinConsultaVehiculo_Es1()
    {
        var ctx = BaseCtx() with { VehiculoConsultado = false };
        TraspasoGates.MaxPasoAlcanzable(ctx).Should().Be(1);
    }

    [Fact]
    public void Paso2_PreflightRed_Bloquea()
    {
        var ctx = BaseCtx() with { Preflight = new PreflightSnapshot("red", false) };
        var r = TraspasoGates.PasoCompleto(2, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("preflight_red");
    }

    [Fact]
    public void Paso2_ForzarContinuar_BypassPreflightRed()
    {
        var ctx = BaseCtx() with { Preflight = new PreflightSnapshot("red", false), ForzarContinuar = true };
        TraspasoGates.PasoCompleto(2, ctx).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso1_ImpuestoUnknownSinPazSalvo_Bloquea()
    {
        // El gate de impuesto se movió al paso 1 (se confirma junto a la consulta/preflight).
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("green", ImpuestoVehicularUnknown: true),
            PazSalvoImpuestoVerificado = false,
        };
        var r = TraspasoGates.PasoCompleto(1, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("impuesto_pendiente");
    }

    [Fact]
    public void Paso1_ImpuestoUnknownConPazSalvo_Avanza()
    {
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("green", ImpuestoVehicularUnknown: true),
            PazSalvoImpuestoVerificado = true,
        };
        TraspasoGates.PasoCompleto(1, ctx).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso3_VendedorIncompleto_Bloquea()
    {
        var ctx = BaseCtx() with { Vendedor = new ParteDatos("V", "111", "no-email") };
        var r = TraspasoGates.PasoCompleto(3, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("vendedor_incompleto");
    }

    [Fact]
    public void Paso3_RuntVendedorDocDistinto_Bloquea()
    {
        var ctx = BaseCtx() with { RuntVendedor = new RuntSnapshot(Consultado: true, "999") };
        TraspasoGates.PasoCompleto(3, ctx).Code.Should().Be("runt_vendedor");
    }

    [Fact]
    public void Paso4_SimitConComparendos_Bloquea()
    {
        var ctx = BaseCtx() with { SimitComprador = new SimitSnapshot(Consultado: true, "222", TotalComparendos: 3) };
        TraspasoGates.PasoCompleto(4, ctx).Code.Should().Be("simit_multas");
    }

    [Fact]
    public void ValidarComercial_Valor0_Bloquea()
    {
        var ctx = BaseCtx() with { ValorVenta = 0m };
        var r = TraspasoGates.ValidarComercial(ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("comercial_valor");
    }

    [Fact]
    public void MaxPasoAlcanzable_SinComercial_Es5()
    {
        var ctx = BaseCtx() with { ValorVenta = 0m };
        TraspasoGates.MaxPasoAlcanzable(ctx).Should().Be(5);
    }

    [Fact]
    public void MaxPasoAlcanzable_TodoCompleto_Es6()
    {
        TraspasoGates.MaxPasoAlcanzable(BaseCtx()).Should().Be(TraspasoGates.TotalPasos);
    }

    [Fact]
    public void PuedeIrAPaso_SaltoA5SinSimit_Bloquea()
    {
        var ctx = BaseCtx() with { SimitComprador = new SimitSnapshot(Consultado: false, "222", 0) };
        var r = TraspasoGates.PuedeIrAPaso(pasoActual: 3, targetPaso: 5, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("simit_pendiente");
    }

    [Fact]
    public void PuedeIrAPaso_RetrocederPermitido()
    {
        TraspasoGates.PuedeIrAPaso(pasoActual: 5, targetPaso: 2, BaseCtx()).Ok.Should().BeTrue();
    }

    [Fact]
    public void PasoSoloLectura_PasosAnterioresAlMax_True()
    {
        var ctx = BaseCtx(); // max = 6
        TraspasoGates.PasoSoloLectura(3, ctx).Should().BeTrue();
        TraspasoGates.PasoSoloLectura(6, ctx).Should().BeFalse();
    }

    [Fact]
    public void DetectarModificacionPasosCerrados_TocaPasoCerrado_Bloquea()
    {
        // maxEditable=5 → pasos 2..4 cerrados.
        var r = TraspasoGates.DetectarModificacionPasosCerrados(
            maxPasoEditable: 5,
            clavesModificadas: new[] { "vendedor" });
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("paso_cerrado");
    }

    [Fact]
    public void DetectarModificacionPasosCerrados_PazSalvoDesdePaso6_Permitido()
    {
        var r = TraspasoGates.DetectarModificacionPasosCerrados(
            maxPasoEditable: 6,
            clavesModificadas: new[] { "paz_salvo_impuesto" },
            pasoPatch: 6);
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public void DetectarModificacionPasosCerrados_ClaveDePasoAbierto_Permitido()
    {
        var r = TraspasoGates.DetectarModificacionPasosCerrados(
            maxPasoEditable: 3,
            clavesModificadas: new[] { "vendedor" }); // paso 3 aún editable
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public void GateFur_SinBiometria_Bloquea()
    {
        TraspasoGates.GateFur(new BiometriaSnapshot(false, false), forzarContinuar: false).Ok.Should().BeFalse();
        TraspasoGates.GateFur(new BiometriaSnapshot(true, true), forzarContinuar: false).Ok.Should().BeTrue();
        TraspasoGates.GateFur(new BiometriaSnapshot(false, false), forzarContinuar: true).Ok.Should().BeTrue();
    }
}
