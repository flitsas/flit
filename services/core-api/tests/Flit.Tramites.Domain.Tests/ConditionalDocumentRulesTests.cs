using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #10521 (RF30/33/34/35/37/38/39) — motor de reglas condicionales y parámetros por gestora.
/// </summary>
public sealed class ConditionalDocumentRulesTests
{
    private static ChecklistItem Opt(string id) => new(id, id, Obligatorio: false, DocTipo: id);
    private static ChecklistItem Obl(string id) => new(id, id, Obligatorio: true, DocTipo: id);

    // ── ApplyConditional (mecánica pura) ──────────────────────────────────────

    [Fact]
    public void ApplyConditional_SinReglas_DejaLaListaIgual()
    {
        var baseItems = new[] { Opt("factura") };
        ChecklistEngine.ApplyConditional(baseItems, new TramiteDocumentContext(), null)
            .Should().BeEquivalentTo(baseItems);
        ChecklistEngine.ApplyConditional(baseItems, new TramiteDocumentContext(), [])
            .Should().BeEquivalentTo(baseItems);
    }

    [Fact]
    public void ApplyConditional_Require_FuerzaObligatorioOAgrega()
    {
        var rules = new[]
        {
            new ConditionalRule("r", c => c.EsImportado, ConditionalEffect.Require, Obl("aduana")),
        };

        // Condición NO se cumple ⇒ no se agrega.
        ChecklistEngine.ApplyConditional([], new TramiteDocumentContext(EsImportado: false), rules)
            .Should().BeEmpty();

        // Condición se cumple, ítem ausente ⇒ se agrega obligatorio.
        var added = ChecklistEngine.ApplyConditional([], new TramiteDocumentContext(EsImportado: true), rules);
        added.Should().ContainSingle(i => i.Id == "aduana" && i.Obligatorio);

        // Ítem presente opcional ⇒ pasa a obligatorio.
        var forced = ChecklistEngine.ApplyConditional(
            [Opt("aduana")], new TramiteDocumentContext(EsImportado: true), rules);
        forced.Single(i => i.Id == "aduana").Obligatorio.Should().BeTrue();
    }

    [Fact]
    public void ApplyConditional_Hide_QuitaElItem()
    {
        var rules = new[]
        {
            new ConditionalRule("r", c => c.EsPersonaNatural, ConditionalEffect.Hide, Opt("cedulas")),
        };

        ChecklistEngine.ApplyConditional([Obl("cedulas")], new TramiteDocumentContext(EsPersonaNatural: true), rules)
            .Should().NotContain(i => i.Id == "cedulas");
        ChecklistEngine.ApplyConditional([Obl("cedulas")], new TramiteDocumentContext(EsPersonaNatural: false), rules)
            .Should().Contain(i => i.Id == "cedulas");
    }

    // ── Escenarios reales del catálogo ────────────────────────────────────────

    [Fact]
    public void Nit_ExigeRuesYCedula_PersonaNaturalOcultaCedula()
    {
        var rules = ConditionalDocumentRules.For(TramiteTipologiaCatalog.CodigoMatriculaInicial);

        var nit = ChecklistEngine.ApplyConditional([], new TramiteDocumentContext(EsNit: true), rules);
        nit.Should().Contain(i => i.Id == "rues" && i.Obligatorio);
        nit.Should().Contain(i => i.Id == "cedulas" && i.Obligatorio);

        // Persona natural: no exige cédula manual (identidad digital) ⇒ se oculta si estaba.
        var pn = ChecklistEngine.ApplyConditional(
            [Obl("cedulas")], new TramiteDocumentContext(EsPersonaNatural: true), rules);
        pn.Should().NotContain(i => i.Id == "cedulas");
    }

    [Fact]
    public void Tramitador_Carroceria_ExigenSusDocumentos()
    {
        // La prenda (RF37) ya no se resuelve por el checklist condicional; vive en su propio
        // agregado (ProcedureInstancePrenda / PrendaGate, Feature #10585).
        var rules = ConditionalDocumentRules.For(TramiteTipologiaCatalog.CodigoMatriculaInicial);

        var ctx = new TramiteDocumentContext(TieneTramitador: true, CambioCarroceria: true);
        var items = ChecklistEngine.ApplyConditional([], ctx, rules);

        items.Should().Contain(i => i.Id == "poder_tramitador" && i.Obligatorio);
        items.Should().Contain(i => i.Id == "factura_carroceria" && i.Obligatorio);
    }

    [Fact]
    public void Traspaso_Leasing_ExigeContratoYDeclaracion()
    {
        var rules = ConditionalDocumentRules.For(TramiteTipologiaCatalog.CodigoTraspasoStandard);

        var items = ChecklistEngine.ApplyConditional([], new TramiteDocumentContext(TieneLeasing: true), rules);
        items.Should().Contain(i => i.Id == "contrato_leasing" && i.Obligatorio);
        items.Should().Contain(i => i.Id == "declaracion_arrendadora" && i.Obligatorio);

        // Sin leasing ⇒ no aparecen.
        ChecklistEngine.ApplyConditional([], new TramiteDocumentContext(), rules)
            .Should().NotContain(i => i.Id == "contrato_leasing");
    }

    [Fact]
    public void ContextoVacio_NoAgregaNingunDocumentoCondicional()
    {
        var rules = ConditionalDocumentRules.For(TramiteTipologiaCatalog.CodigoMatriculaInicial);
        ChecklistEngine.ApplyConditional([Opt("factura")], new TramiteDocumentContext(), rules)
            .Should().ContainSingle(i => i.Id == "factura");
    }

    [Fact]
    public void For_TipologiaDesconocida_SinReglas()
    {
        ConditionalDocumentRules.For("no_existe").Should().BeEmpty();
        ConditionalDocumentRules.For(null).Should().BeEmpty();
    }

    // ── Parámetros por gestora ────────────────────────────────────────────────

    [Fact]
    public void ApplyCompanyParams_SinParametros_DejaLaListaIgual()
    {
        var baseItems = new[] { Opt("soat"), Obl("factura") };
        ChecklistEngine.ApplyCompanyParams(baseItems, null).Should().BeEquivalentTo(baseItems);
        ChecklistEngine.ApplyCompanyParams(baseItems, []).Should().BeEquivalentTo(baseItems);
    }

    [Fact]
    public void ApplyCompanyParams_OcultoObligatorioOpcional()
    {
        var baseItems = new[] { Opt("soat"), Obl("factura"), Opt("cepd") };
        var parametros = new[]
        {
            new CompanyDocumentParam("soat", CompanyDocumentParamState.Obligatorio),
            new CompanyDocumentParam("factura", CompanyDocumentParamState.Opcional),
            new CompanyDocumentParam("cepd", CompanyDocumentParamState.Oculto),
        };

        var result = ChecklistEngine.ApplyCompanyParams(baseItems, parametros);

        result.Single(i => i.Id == "soat").Obligatorio.Should().BeTrue();
        result.Single(i => i.Id == "factura").Obligatorio.Should().BeFalse();
        result.Should().NotContain(i => i.Id == "cepd");
    }
}
