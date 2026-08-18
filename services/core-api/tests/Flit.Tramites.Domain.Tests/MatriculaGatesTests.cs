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
        Comprador = new ParteDatos("C", "222", "c@x.co", "Bogotá", "Calle 1 # 2-3", "3001234567"),
        RuntComprador = new RuntSnapshot(Consultado: true, "222"),
        IdentidadAprobada = true,
        DocumentosObligatoriosCompletos = true,
    };

    [Fact] // HU #10935 — documentos ahora es el paso 3 (después del comprador).
    public void Paso3_DocumentosIncompletos_Bloquea()
    {
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false };
        TraspasoGatesShared(ctx, 3, "documentos_incompletos");
    }

    [Fact]
    public void Paso3_DocumentosCompletos_Avanza()
    {
        MatriculaGates.PasoCompleto(3, BaseCtx()).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso3_DocumentosIncompletos_ForzarNoBypass()
    {
        // Gating ESTRICTO: forzar no omite documentos obligatorios.
        var ctx = BaseCtx() with { DocumentosObligatoriosCompletos = false, ForzarContinuar = true };
        var r = MatriculaGates.PasoCompleto(3, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("documentos_incompletos");
    }

    [Fact]
    public void Paso3_PreflightRed_Bloquea()
    {
        var ctx = BaseCtx() with { Preflight = new PreflightSnapshot("red", false) };
        TraspasoGatesShared(ctx, 3, "preflight_red");
    }

    [Fact]
    public void Paso3_RiesgoPreflightAceptado_BypassPreflightRed()
    {
        // "Asumo el riesgo" levanta el bloqueo de preflight rojo subsanable en el paso de documentos.
        var ctx = BaseCtx() with { Preflight = new PreflightSnapshot("red", false), RiesgoPreflightAceptado = true };
        MatriculaGates.PasoCompleto(3, ctx).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso3_PreflightProviderError_BloqueaDuroNoSubsanable()
    {
        // Consulta no verificable (error de proveedor) = bloqueo DURO: ni "aceptar riesgo" ni
        // forzar lo levantan. Hay que reejecutar la consulta.
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("red", false, ProviderError: true),
            RiesgoPreflightAceptado = true,
            ForzarContinuar = true,
        };
        TraspasoGatesShared(ctx, 3, "preflight_provider_error");
    }

    [Fact]
    public void Paso3_RiesgoPreflightAceptado_AunExigeDocumentos()
    {
        // El riesgo NO afloja el gating de documentos obligatorios.
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("red", false),
            RiesgoPreflightAceptado = true,
            DocumentosObligatoriosCompletos = false,
        };
        TraspasoGatesShared(ctx, 3, "documentos_incompletos");
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
    public void Paso1_PreflightProviderError_BloqueaDuroNoSubsanable()
    {
        // Consulta del vehículo no verificable (error de proveedor): aunque el VIN esté en
        // field_values (VehiculoConsultado=true), el paso 1 NO se completa. Ni forzar ni aceptar
        // riesgo lo levantan.
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("red", false, ProviderError: true),
            ForzarContinuar = true,
            RiesgoPreflightAceptado = true,
        };
        TraspasoGatesShared(ctx, 1, "preflight_provider_error");
    }

    [Fact]
    public void Paso1_VehiculoNoEncontrado_BloqueaDuroNoSubsanable()
    {
        // El RUNT respondió y el vehículo NO existe: sin vehículo verificado no hay trámite. Es
        // bloqueo DURO — ni forzar ni "aceptar riesgo" lo levantan; hay que corregir el VIN.
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("red", false, VehiculoNoEncontrado: true),
            ForzarContinuar = true,
            RiesgoPreflightAceptado = true,
        };
        TraspasoGatesShared(ctx, 1, "vehiculo_no_encontrado");
    }

    [Fact]
    public void Paso3_VehiculoNoEncontrado_BloqueaDuroNoSubsanable()
    {
        var ctx = BaseCtx() with
        {
            Preflight = new PreflightSnapshot("red", false, VehiculoNoEncontrado: true),
            RiesgoPreflightAceptado = true,
            ForzarContinuar = true,
        };
        TraspasoGatesShared(ctx, 3, "vehiculo_no_encontrado");
    }

    [Fact] // HU #10935 — el comprador ahora es el paso 2 (antes del de documentos).
    public void Paso2_CompradorIncompleto_Bloquea()
    {
        var ctx = BaseCtx() with { Comprador = new ParteDatos("C", "", "c@x.co") };
        TraspasoGatesShared(ctx, 2, "comprador_incompleto");
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

    // HU #11593 — contacto obligatorio (ciudad, dirección, teléfono) en el gate del comprador.

    [Fact]
    public void Paso2_CompradorConSeisDatosCompletos_Avanza()
    {
        MatriculaGates.PasoCompleto(2, BaseCtx()).Ok.Should().BeTrue();
    }

    [Fact]
    public void Paso2_CompradorSinTelefono_Bloquea_YEnumeraTelefono()
    {
        var ctx = BaseCtx() with { Comprador = new ParteDatos("C", "222", "c@x.co", "Bogotá", "Calle 1 # 2-3", null) };
        var r = MatriculaGates.PasoCompleto(2, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("comprador_incompleto");
        r.Message.Should().Contain("teléfono");
    }

    [Fact]
    public void Paso2_CompradorSinCiudadNiDireccion_Bloquea_YEnumeraAmbas()
    {
        var ctx = BaseCtx() with { Comprador = new ParteDatos("C", "222", "c@x.co", null, null, "3001234567") };
        var r = MatriculaGates.PasoCompleto(2, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be("comprador_incompleto");
        r.Message.Should().Contain("ciudad");
        r.Message.Should().Contain("dirección");
    }

    [Fact]
    public void MaxPasoAlcanzable_TramiteEnCursoSinTelefono_SeReabreEnPaso2()
    {
        // AC4 — un trámite en curso que ya tenía el paso de actores "completo" bajo la regla
        // anterior (sin exigir teléfono) se reabre solo: MaxPasoAlcanzable recalcula desde cero
        // en cada evaluación, sin migración ni script.
        var ctx = BaseCtx() with { Comprador = new ParteDatos("C", "222", "c@x.co", "Bogotá", "Calle 1 # 2-3", null) };
        MatriculaGates.MaxPasoAlcanzable(ctx).Should().Be(2);
        MatriculaGates.PasoSoloLectura(2, ctx).Should().BeFalse();
    }

    private static void TraspasoGatesShared(MatriculaGateContext ctx, int paso, string code)
    {
        var r = MatriculaGates.PasoCompleto(paso, ctx);
        r.Ok.Should().BeFalse();
        r.Code.Should().Be(code);
    }
}
