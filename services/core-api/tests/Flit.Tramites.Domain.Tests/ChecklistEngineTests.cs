using System.Collections.Generic;
using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class ChecklistEngineTests
{
    [Fact]
    public void Compute_TipologiaDesconocida_Null()
    {
        ChecklistEngine.Compute("no_existe", null).Should().BeNull();
    }

    [Fact]
    public void Compute_SinMarcas_ObligatoriosFaltantes()
    {
        var r = ChecklistEngine.Compute(TramiteTipologiaCatalog.CodigoTraspasoStandard, null)!;

        r.Completo.Should().BeFalse();
        r.ObligatoriosSatisfechos.Should().Be(0);
        r.FaltanObligatorios.Should().Contain("contrato_compraventa");
        r.FaltanObligatorios.Should().NotContain("cert_tradicion"); // opcional, no bloquea
    }

    [Fact]
    public void Compute_DocAutoMarcaItem_ViaDocumento()
    {
        var r = ChecklistEngine.Compute(
            TramiteTipologiaCatalog.CodigoTraspasoStandard,
            null,
            new[] { "soat" })!;

        var soat = r.Items.Single(i => i.Item.Id == "soat");
        soat.Satisfecho.Should().BeTrue();
        soat.Via.Should().Be(ChecklistVia.Documento);
    }

    [Fact]
    public void Compute_MarcaManual_ViaManual()
    {
        var estado = new Dictionary<string, bool> { ["cedulas"] = true };
        var r = ChecklistEngine.Compute(TramiteTipologiaCatalog.CodigoTraspasoStandard, estado)!;

        var cedulas = r.Items.Single(i => i.Item.Id == "cedulas");
        cedulas.Satisfecho.Should().BeTrue();
        cedulas.Via.Should().Be(ChecklistVia.Manual);
    }

    [Fact]
    public void Compute_TodosObligatorios_Completo()
    {
        var estado = new Dictionary<string, bool>
        {
            ["contrato_compraventa"] = true,
            ["impronta"] = true,
            ["soat"] = true,
            ["rtm"] = true,
            ["paz_salvo"] = true,
            ["cedulas"] = true,
        };
        var r = ChecklistEngine.Compute(TramiteTipologiaCatalog.CodigoTraspasoStandard, estado)!;

        r.Completo.Should().BeTrue();
        r.FaltanObligatorios.Should().BeEmpty();
    }

    [Fact]
    public void Merge_Hide_QuitaItem()
    {
        var baseItems = TramiteTipologiaCatalog.Get(TramiteTipologiaCatalog.CodigoTraspasoStandard)!.Checklist;
        var merged = ChecklistEngine.Merge(baseItems, new ChecklistOverride(Hide: new[] { "rtm" }));

        merged.Should().NotContain(i => i.Id == "rtm");
    }

    [Fact]
    public void Merge_Require_FuerzaObligatorio()
    {
        var baseItems = TramiteTipologiaCatalog.Get(TramiteTipologiaCatalog.CodigoTraspasoStandard)!.Checklist;
        var merged = ChecklistEngine.Merge(baseItems, new ChecklistOverride(Require: new[] { "cert_tradicion" }));

        merged.Single(i => i.Id == "cert_tradicion").Obligatorio.Should().BeTrue();
    }

    [Fact]
    public void Merge_Add_AgregaSinDuplicar()
    {
        var baseItems = TramiteTipologiaCatalog.Get(TramiteTipologiaCatalog.CodigoTraspasoStandard)!.Checklist;
        var nuevo = new ChecklistItem("extra_local", "Requisito local", Obligatorio: true);
        var duplicado = new ChecklistItem("soat", "SOAT (dup)", Obligatorio: true);

        var merged = ChecklistEngine.Merge(baseItems, new ChecklistOverride(Add: new[] { nuevo, duplicado }));

        merged.Count(i => i.Id == "soat").Should().Be(1);
        merged.Should().Contain(i => i.Id == "extra_local");
    }

    [Fact]
    public void ComputeWithOverride_HideQuitaObligatorioDelGate()
    {
        // Sin el override, contrato_compraventa es obligatorio y faltaría.
        var r = ChecklistEngine.ComputeWithOverride(
            TramiteTipologiaCatalog.CodigoTraspasoStandard,
            new Dictionary<string, bool>
            {
                ["impronta"] = true,
                ["soat"] = true,
                ["rtm"] = true,
                ["paz_salvo"] = true,
                ["cedulas"] = true,
            },
            null,
            new ChecklistOverride(Hide: new[] { "contrato_compraventa" }))!;

        r.Completo.Should().BeTrue();
    }
}
