using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #10521 (RF30/35) — cómputo del checklist con reglas condicionales por atributo
/// (<see cref="ChecklistEngine.ComputeConditional"/>).
/// </summary>
public sealed class ChecklistEngineConditionalComputeTests
{
    private static readonly string Traspaso = TramiteTipologiaCatalog.CodigoTraspasoStandard;

    private static ChecklistResultado Compute(TramiteDocumentContext context) =>
        ChecklistEngine.ComputeConditional(
            Traspaso, null, null, context,
            ConditionalDocumentRules.For(Traspaso))!;

    [Fact]
    public void ContextoVacio_EsIgualAlChecklistBase()
    {
        var conditional = Compute(new TramiteDocumentContext());
        var plano = ChecklistEngine.Compute(Traspaso, null, null)!;

        conditional.Items.Select(i => i.Item.Id)
            .Should().BeEquivalentTo(plano.Items.Select(i => i.Item.Id));
    }

    [Fact]
    public void PersonaNatural_OcultaCedulaManual_RF35()
    {
        // El traspaso base exige "cedulas"; para persona natural (identidad digital) se oculta.
        ChecklistEngine.Compute(Traspaso, null, null)!.Items.Should().Contain(i => i.Item.Id == "cedulas");

        var pn = Compute(new TramiteDocumentContext(EsPersonaNatural: true));
        pn.Items.Should().NotContain(i => i.Item.Id == "cedulas");
    }

    [Fact]
    public void Nit_NoCargaRuesNiCedula_RF35()
    {
        // Ajuste RUES: el actor NIT no carga Certificado RUES ni el documento de identidad (lo cubre el
        // certificado_rues autogenerado). En traspaso la cédula viene del checklist base ⇒ se oculta.
        var nit = Compute(new TramiteDocumentContext(EsNit: true));
        nit.Items.Should().NotContain(i => i.Item.Id == "rues");
        nit.Items.Should().NotContain(i => i.Item.Id == "cedulas");
    }
}
