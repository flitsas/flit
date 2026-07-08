using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #10521 (RF30/31/35) — cómputo del checklist con reglas condicionales por atributo y
/// parámetros por gestora enchufados (<see cref="ChecklistEngine.ComputeConditional"/>).
/// </summary>
public sealed class ChecklistEngineConditionalComputeTests
{
    private static readonly string Traspaso = TramiteTipologiaCatalog.CodigoTraspasoStandard;

    private static ChecklistResultado Compute(
        TramiteDocumentContext context,
        IReadOnlyCollection<CompanyDocumentParam>? parametros = null) =>
        ChecklistEngine.ComputeConditional(
            Traspaso, null, null, context,
            ConditionalDocumentRules.For(Traspaso), parametros)!;

    [Fact]
    public void ContextoVacio_SinParametros_EsIgualAlChecklistBase()
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
    public void Nit_ExigeRuesYCedula_RF35()
    {
        var nit = Compute(new TramiteDocumentContext(EsNit: true));
        nit.Items.Should().Contain(i => i.Item.Id == "rues" && i.Item.Obligatorio);
        nit.Items.Should().Contain(i => i.Item.Id == "cedulas" && i.Item.Obligatorio);
    }

    [Fact]
    public void ParametroGestora_Oculto_QuitaDocumento_RF31()
    {
        var conParam = Compute(
            new TramiteDocumentContext(),
            [new CompanyDocumentParam("soat", CompanyDocumentParamState.Oculto)]);

        conParam.Items.Should().NotContain(i => i.Item.Id == "soat");
    }

    [Fact]
    public void ParametroGestora_Opcional_RelajaObligatoriedad_RF31()
    {
        // "rtm" es obligatorio en el traspaso base; la gestora lo vuelve opcional.
        ChecklistEngine.Compute(Traspaso, null, null)!
            .Items.Single(i => i.Item.Id == "rtm").Item.Obligatorio.Should().BeTrue();

        var conParam = Compute(
            new TramiteDocumentContext(),
            [new CompanyDocumentParam("rtm", CompanyDocumentParamState.Opcional)]);

        conParam.Items.Single(i => i.Item.Id == "rtm").Item.Obligatorio.Should().BeFalse();
        conParam.FaltanObligatorios.Should().NotContain("rtm");
    }
}
