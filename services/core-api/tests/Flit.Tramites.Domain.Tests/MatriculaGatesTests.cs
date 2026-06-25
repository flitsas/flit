using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class MatriculaGatesTests
{
    private static MatriculaGateContext BaseCtx() => new()
    {
        VehiculoConsultado = true,
        Preflight = new PreflightSnapshot("green", false),
        Comprador = new ParteDatos("C", "222", "c@x.co"),
        RuntComprador = new RuntSnapshot(Consultado: true, "222"),
        IdentidadAprobada = true,
        DocumentosObligatoriosCompletos = true,
    };

    [Fact]
    public void Paso2_DocumentosIncompletos_Bloquea()
    {
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false };
        TraspasoGatesShared(ctx, 2, "documentos_incompletos");
    }

    [Fact]
    public void Paso2_DocumentosCompletos_Avanza()
    {
        MatriculaGates.PasoCompleto(2, BaseCtx()).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso2_DocumentosIncompletos_ForzarNoBypass()
    {
        // Gating ESTRICTO: forzar no omite documentos obligatorios.
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false, ForzarContinuar = true };
        var r = MatriculaGates.PasoCompleto(2, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("documentos_incompletos");
    }

    [Fact]
    public void Paso2_PreflightRed_Bloquea()
    {
        var ctx = BaseCtx() with { Preflight = new PreflightSnapshot("red", false) };
        TraspasoGatesShared(ctx, 2, "preflight_red");
    }

    [Fact]
    public void Paso2_RiesgoPreflightAceptado_BypassPreflightRed()
    {
        // "Asumo el riesgo" levanta el bloqueo de preflight rojo subsanable en el paso 2.
        var ctx = BaseCtx() with { Preflight = new PreflightSnapshot("red", false), RiesgoPreflightAceptado = true };
        MatriculaGates.PasoCompleto(2, ctx).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso2_RiesgoPreflightAceptado_AunExigeDocumentos()
    {
        // El riesgo NO afloja el gating de documentos obligatorios.
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("red", false),
            RiesgoPreflightAceptado = true,
            DocumentosObligatoriosCompletos = false,
        };
        TraspasoGatesShared(ctx, 2, "documentos_incompletos");
    }

    [Fact]
    public void Paso4_RiesgoPreflightAceptado_NoBypassIdentidad()
    {
        // A diferencia de ForzarContinuar, el riesgo NO omite la validación de identidad.
        var ctx = BaseCtx() with { IdentidadAprobada = false, RiesgoPreflightAceptado = true };
        TraspasoGatesShared(ctx, 4, "identidad_pendiente");
    }

    [Fact]
    public void Paso1_SinConsultarVin_Bloquea()
    {
        var ctx = BaseCtx() with { VehiculoConsultado = false };
        TraspasoGatesShared(ctx, 1, "vin_pendiente");
    }

    [Fact]
    public void Paso3_CompradorIncompleto_Bloquea()
    {
        var ctx = BaseCtx() with { Comprador = new ParteDatos("C", "", "c@x.co") };
        TraspasoGatesShared(ctx, 3, "comprador_incompleto");
    }

    [Fact]
    public void Paso4_IdentidadPendiente_Bloquea()
    {
        var ctx = BaseCtx() with { IdentidadAprobada = false };
        TraspasoGatesShared(ctx, 4, "identidad_pendiente");
    }

    [Fact]
    public void Paso4_ForzarContinuar_Bypass()
    {
        var ctx = BaseCtx() with { IdentidadAprobada = false, ForzarContinuar = true };
        MatriculaGates.PasoCompleto(4, ctx).Ok.Should().BeTrue();
    }

    [Fact]
    public void MaxPasoAlcanzable_TodoCompleto_Es5()
    {
        MatriculaGates.MaxPasoAlcanzable(BaseCtx()).Should().Be(MatriculaGates.TotalPasos);
    }

    [Fact]
    public void MaxPasoAlcanzable_SinIdentidad_Es4()
    {
        var ctx = BaseCtx() with { IdentidadAprobada = false };
        MatriculaGates.MaxPasoAlcanzable(ctx).Should().Be(4);
    }

    [Fact]
    public void PasoSoloLectura_AnterioresAlMax_True()
    {
        MatriculaGates.PasoSoloLectura(2, BaseCtx()).Should().BeTrue();
        MatriculaGates.PasoSoloLectura(5, BaseCtx()).Should().BeFalse();
    }

    private static void TraspasoGatesShared(MatriculaGateContext ctx, int paso, string code)
    {
        var r = MatriculaGates.PasoCompleto(paso, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be(code);
    }
}
