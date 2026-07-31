using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11141 — el mecanismo de firma que se MUESTRA debe ser el que se SELECCIONÓ al registrar al
/// actor. La regla vive en un único predicado para que la interfaz y el generador de documentos no
/// puedan divergir; estas pruebas la fijan.
/// </summary>
public sealed class FirmaBaulCoberturaTests
{
    private static ProcedureInstanceActor Actor(string documentType, string? mecanismo, bool conRepresentante = true)
    {
        var metadata = "{}";
        if (conRepresentante)
        {
            var rl = new Dictionary<string, object?>
            {
                ["tipoDocumento"] = "CC",
                ["numeroDocumento"] = "52082029",
                ["nombreCompleto"] = "PADILLA HERNANDEZ ALEXANDRA",
            };
            if (mecanismo is not null)
                rl["mecanismoFirma"] = mecanismo;

            metadata = JsonSerializer.Serialize(new Dictionary<string, object?> { ["representanteLegal"] = rl });
        }

        return new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = documentType,
            DocumentNumber = "900511343",
            FullName = "EMPRESA DEMO S.A.S.",
            Metadata = metadata,
        };
    }

    [Fact]
    public void ConIdentidadSeleccionada_NoAplicaElBaul()
    {
        // El caso reportado: el gestor eligió validación de identidad y la parte salía rotulada como
        // «firmado desde el baúl» porque el representante TENÍA además una firma vigente.
        FirmaBaulCobertura.Aplica(Actor("NIT", MecanismoFirma.Identidad)).Should().BeFalse();
    }

    [Fact]
    public void ConBaulSeleccionado_AplicaElBaul()
    {
        FirmaBaulCobertura.Aplica(Actor("NIT", MecanismoFirma.Baul)).Should().BeTrue();
    }

    [Fact]
    public void SinEleccionExplicita_MantieneLaPrecedenciaDelBaul()
    {
        // Comportamiento previo (HU #11031), que no se toca: sin elección manda el baúl.
        FirmaBaulCobertura.Aplica(Actor("NIT", mecanismo: null)).Should().BeTrue();
        FirmaBaulCobertura.Aplica(Actor("NIT", mecanismo: null, conRepresentante: false)).Should().BeTrue();
    }

    [Theory]
    [InlineData("CC")]
    [InlineData("CE")]
    [InlineData("PAS")]
    public void PersonaNatural_NuncaSeCubreConElBaul(string tipoDocumento)
    {
        // El baúl se consume por el representante legal de una compañía. Una persona natural firma con
        // su validación de identidad, y así lo hace el generador: la vista debe decir lo mismo.
        FirmaBaulCobertura.Aplica(Actor(tipoDocumento, MecanismoFirma.Baul)).Should().BeFalse();
    }

    [Theory]
    [InlineData("NIT")]
    [InlineData("nit")]
    [InlineData(" NIT ")]
    [InlineData("N")]
    public void ReconoceALaPersonaJuridicaEnLasFormasQueLlegaDeLosProveedores(string tipoDocumento)
    {
        // "N" es el código del RUNT para NIT y llega así desde algunos proveedores de consulta.
        FirmaBaulCobertura.EsJuridico(tipoDocumento).Should().BeTrue();
    }

    [Fact]
    public void SinActor_NoAplica()
    {
        FirmaBaulCobertura.Aplica(null).Should().BeFalse();
    }

    [Fact]
    public void MetadataIlegible_MantieneLaPrecedenciaDelBaul()
    {
        // Un jsonb corrupto no puede cambiar el mecanismo de firma de un trámite: se degrada al
        // comportamiento por defecto, no a uno arbitrario.
        var actor = Actor("NIT", null);
        actor.Metadata = "{esto no es json";

        FirmaBaulCobertura.Aplica(actor).Should().BeTrue();
    }
}
