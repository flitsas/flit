using System.Linq;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Causal de la cancelación de matrícula: qué se declara y qué documentos exige.
///
/// <para>La casilla 13 del FUR es una sola para cuatro trámites que el organismo tramita distinto, y
/// cada causal se acredita diferente. Estos tests fijan la tabla 5 de
/// <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>: cada causal exige TODOS sus documentos —no uno
/// cualquiera— y ninguno de los de las otras.</para>
/// </summary>
public sealed class CancelacionCausalTests
{
    [Theory]
    [InlineData("DECISION_JUDICIAL", CancelacionCausal.DecisionJudicial)]
    [InlineData("decision_judicial", CancelacionCausal.DecisionJudicial)]
    [InlineData("  PERDIDA_TOTAL_FUERZA_MAYOR  ", CancelacionCausal.PerdidaTotalFuerzaMayor)]
    [InlineData("PERDIDA_TOTAL_ACCIDENTE", CancelacionCausal.PerdidaTotalAccidente)]
    [InlineData("DECISION_VOLUNTARIA", CancelacionCausal.DecisionVoluntaria)]
    public void Parse_ReconoceLosCodigosDelCatalogo(string valor, CancelacionCausal esperada) =>
        CancelacionCausales.Parse(valor).Should().Be(esperada);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PERDIDA_TOTAL")]
    [InlineData("cualquier_cosa")]
    public void Parse_ValorAusenteOIrreconocible_NoAdivinaCausal(string? valor)
    {
        CancelacionCausales.Parse(valor).Should().Be(CancelacionCausal.Ninguna);
        CancelacionCausales.DocumentosExigidos(valor).Should().BeEmpty(
            "sin causal declarada no se exige ningún documento de causal");
    }

    [Fact]
    public void Codigos_YToCodigo_SonElMismoContrato()
    {
        CancelacionCausales.Codigos.Should().HaveCount(4);
        foreach (var codigo in CancelacionCausales.Codigos)
            CancelacionCausales.ToCodigo(CancelacionCausales.Parse(codigo)).Should().Be(codigo);

        CancelacionCausales.ToCodigo(CancelacionCausal.Ninguna).Should().BeNull();
    }

    [Fact]
    public void DecisionJudicial_ExigeSoloElActoJudicial() =>
        CancelacionCausales.DocumentosExigidos(CancelacionCausal.DecisionJudicial)
            .Should().Equal(CancelacionCausales.DocActoDecisionJudicial);

    [Theory]
    [InlineData(CancelacionCausal.PerdidaTotalFuerzaMayor)]
    [InlineData(CancelacionCausal.PerdidaTotalAccidente)]
    public void PerdidaTotal_ExigeLosTresCertificados(CancelacionCausal causal) =>
        CancelacionCausales.DocumentosExigidos(causal).Should().Equal(
            CancelacionCausales.DocCertificadoDijin,
            CancelacionCausales.DocCertificadoAseguradoraPerito,
            CancelacionCausales.DocCertificadoAutoridadAdministrativa);

    [Fact]
    public void DecisionVoluntaria_ExigeSoloElCertificadoDeLaDijin() =>
        CancelacionCausales.DocumentosExigidos(CancelacionCausal.DecisionVoluntaria)
            .Should().Equal(CancelacionCausales.DocCertificadoDijin);

    // ── Reglas condicionales sobre el checklist ───────────────────────────────

    private static ChecklistItem Opt(string id) => new(id, id, Obligatorio: false, DocTipo: id);

    private static System.Collections.Generic.IReadOnlyList<ChecklistItem> Aplicar(
        CancelacionCausal causal)
    {
        // Checklist base del trámite: el certificado de tradición (obligatorio de base) y los cuatro
        // documentos de causal, opcionales, tal como los deja el DDL 91.
        ChecklistItem[] baseItems =
        [
            new("cert_tradicion", "cert_tradicion", Obligatorio: true, DocTipo: "cert_tradicion"),
            .. CancelacionCausales.TodosLosDocumentos.Select(Opt),
        ];

        return ChecklistEngine.ApplyConditional(
            baseItems,
            new TramiteDocumentContext(CancelacionCausal: causal),
            ConditionalDocumentRules.For(CancelacionCausales.TipoCodigo));
    }

    [Theory]
    [InlineData(CancelacionCausal.DecisionJudicial)]
    [InlineData(CancelacionCausal.PerdidaTotalFuerzaMayor)]
    [InlineData(CancelacionCausal.PerdidaTotalAccidente)]
    [InlineData(CancelacionCausal.DecisionVoluntaria)]
    public void ConCausal_ExigeLosSuyosYQuitaLosDeLasOtras(CancelacionCausal causal)
    {
        var items = Aplicar(causal);
        var exigidos = CancelacionCausales.DocumentosExigidos(causal);

        foreach (var doc in exigidos)
            items.Should().ContainSingle(i => i.Id == doc && i.Obligatorio,
                "{0} acredita la causal {1}", doc, causal);

        foreach (var doc in CancelacionCausales.TodosLosDocumentos.Except(exigidos))
            items.Should().NotContain(i => i.Id == doc,
                "{0} no aplica a {1} y mostrarlo como opcional es ruido", doc, causal);

        items.Should().ContainSingle(i => i.Id == "cert_tradicion" && i.Obligatorio,
            "el certificado de tradición es de base en las cuatro causales");
    }

    [Fact]
    public void SinCausal_NoExigeNiOcultaNada()
    {
        var items = Aplicar(CancelacionCausal.Ninguna);

        items.Should().HaveCount(5, "el checklist queda como el base");
        foreach (var doc in CancelacionCausales.TodosLosDocumentos)
            items.Should().ContainSingle(i => i.Id == doc && !i.Obligatorio);
    }

    [Fact]
    public void OtrosTipos_NoCarganLasReglasDeCancelacion()
    {
        ConditionalDocumentRules.For("DUPLICADO_PLACA").Should().BeEmpty();
        ConditionalDocumentRules.For(CancelacionCausales.TipoCodigo).Should().NotBeEmpty();
    }
}
