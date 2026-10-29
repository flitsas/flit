using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.PersonaNatural;

/// <summary>HU #14078 (Feature K, Épica #14021) — la definición del portal sale del perfil del tipo, no de una lista por tipo.</summary>
public sealed class DefinicionPersonaNaturalTests
{
    private const string PerfilTraspaso =
        """{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"allowsMultipleBuyer":true,"requiresCommercialValue":true,"requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true}""";

    private const string PerfilUnTitular =
        """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true}""";

    private static ProcedureTypeGateProfile Perfil(string json) => ProcedureTypeGateProfile.FromJson(json);

    [Fact]
    public void Traspaso_PideUnVendedorYUnComprador_AunqueElTipoAdmitaCopropietarios()
    {
        var actores = DefinicionPersonaNatural.Actores(Perfil(PerfilTraspaso), personaNaturalPermitida: true);

        actores.Select(a => a.Rol).Should().Equal("vendedor", "comprador");
        var vendedor = actores[0];
        vendedor.Min.Should().Be(1);
        vendedor.Max.Should().Be(1);
        vendedor.Biometria.Should().BeTrue();
        vendedor.Firma.Should().BeTrue();
        var comprador = actores[1];
        comprador.Max.Should().Be(1, "el portal no pide copropietarios");
        comprador.Biometria.Should().BeTrue();
        comprador.Etiqueta.Should().Be("Comprador");
        actores.Should().OnlyContain(a => a.PersonaNaturalPermitida);
    }

    [Fact]
    public void UnTipoDeUnSoloTitular_PideSoloEseRol_LlamadoTitular_YSinCopropietarios()
    {
        var actores = DefinicionPersonaNatural.Actores(Perfil(PerfilUnTitular), personaNaturalPermitida: true);

        var titular = actores.Should().ContainSingle().Which;
        titular.Rol.Should().Be("comprador", "el titular se persiste como comprador, como hace FLIT 2");
        titular.Etiqueta.Should().Be("Titular");
        (titular.Min, titular.Max).Should().Be((1, 1));
        titular.Biometria.Should().BeTrue();
    }

    [Fact]
    public void LaBiometriaYLaFirma_SoloSeMarcanSiElTipoLasExige_YPorActor()
    {
        var sinBiometria = Perfil("""{"requiresBuyer":true,"requiresBiometrics":false,"biometricActors":["BUYER"]}""");
        var soloComprador = Perfil("""{"requiresSeller":true,"requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"signatureActors":["BUYER"]}""");

        DefinicionPersonaNatural.Actores(sinBiometria, true).Single().Biometria.Should().BeFalse("el perfil apaga la biometría");
        var actores = DefinicionPersonaNatural.Actores(soloComprador, true);
        actores.Single(a => a.Rol == "vendedor").Biometria.Should().BeFalse();
        actores.Single(a => a.Rol == "vendedor").Firma.Should().BeFalse();
        actores.Single(a => a.Rol == "comprador").Firma.Should().BeTrue();
    }

    [Fact]
    public void ElLocatario_SoloApareceSiElPerfilLoPide_YUnPerfilVacioNoPideActores()
    {
        DefinicionPersonaNatural.Actores(Perfil("""{"requiresBuyer":true,"requiresLessee":true}"""), true).Select(a => a.Rol)
            .Should().Equal("comprador", "locatario");
        DefinicionPersonaNatural.Actores(Perfil("{}"), true).Should().BeEmpty();
    }

    [Fact]
    public void LaPersonaNaturalPermitida_SigueALaElegibilidadDelTipo()
    {
        DefinicionPersonaNatural.Actores(Perfil(PerfilUnTitular), personaNaturalPermitida: false)
            .Should().OnlyContain(a => !a.PersonaNaturalPermitida);
    }

    [Fact]
    public void LasCapacidades_UsanLosMismosResolvedoresDeFlit2_ConNombresDelContrato()
    {
        var traspaso = DefinicionPersonaNatural.Capacidades(Perfil(PerfilTraspaso), "TRASPASO");
        var otros = DefinicionPersonaNatural.Capacidades(Perfil("""{"entryMode":"VIN","hasPrendaGate":true,"requiresPlateRequest":true}"""), "OTROS");

        traspaso.EntryMode.Should().Be("PLATE");
        traspaso.RequiereComercial.Should().BeTrue();
        traspaso.RequiereBiometria.Should().BeTrue();
        traspaso.PermiteTransformaciones.Should().BeTrue("la familia TRASPASO acumula complementarios");
        otros.PermiteTransformaciones.Should().BeFalse("la familia OTROS no acumula: el cambio ES el trámite");
        otros.RequierePrenda.Should().BeTrue();
        otros.RequiereSolicitudPlaca.Should().BeTrue();
        otros.EntryMode.Should().Be("VIN");
    }

    [Fact]
    public void SinModoDeEntradaDeclarado_AceptaPlacaOVin()
    {
        DefinicionPersonaNatural.Capacidades(Perfil("{}"), "OTROS").EntryMode.Should().Be("BOTH");
    }

    [Fact]
    public void LasCondicionesDeLosDocumentosCondicionales_SalenDeLasReglasDeFlit2_EnTextoLegible()
    {
        var (porDoc, adicionales) = DefinicionPersonaNatural.CondicionesDocumentales("TRASPASO_STANDARD");

        porDoc["contrato_leasing"].Should().Contain("leasing");
        porDoc["poder_tramitador"].Should().Contain("tramitador");
        porDoc["cedulas"].Should().Contain("No se pide");
        adicionales.Should().Contain(a => a.DocCode == "contrato_leasing" && a.Obligatorio);
        adicionales.Should().NotContain(a => a.DocCode == "cedulas", "una regla que solo oculta no agrega ningún documento");
    }

    [Fact]
    public void UnTipoSinReglasCondicionales_NoTraeCondiciones()
    {
        var (porDoc, adicionales) = DefinicionPersonaNatural.CondicionesDocumentales("CAMBIO_COLOR");

        porDoc.Should().BeEmpty();
        adicionales.Should().BeEmpty();
    }

    [Theory]
    [InlineData("TRASPASO_STANDARD")]
    [InlineData("MATRICULA_NUEVA")]
    [InlineData(CancelacionCausales.TipoCodigo)]
    public void TodaReglaCondicionalDeFlit2_TieneSuTextoPropio_NoElGenerico(string tipo)
    {
        // Si alguien agrega una regla sin darle texto aquí, esta prueba lo dice en vez de salir «según una condición».
        foreach (var regla in ConditionalDocumentRules.For(tipo))
            DefinicionPersonaNatural.TextoDeRegla(regla).Should().NotBe("Se pide solo cuando se cumple una condición del trámite.", regla.Id);
    }
}
