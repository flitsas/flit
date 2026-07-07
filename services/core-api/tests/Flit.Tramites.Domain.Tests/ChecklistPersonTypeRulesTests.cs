using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #10542 — supresión del ítem <c>cedulas</c> como carga manual según el tipo de persona
/// del actor. Cubre AC1 (persona natural oculta cédula) y AC3 (persona jurídica la conserva).
/// </summary>
public sealed class ChecklistPersonTypeRulesTests
{
    private static readonly string Traspaso = TramiteTipologiaCatalog.CodigoTraspasoStandard;

    // ── AC1: persona natural → no se exige la cédula como carga manual ─────────────

    [Fact]
    public void BuildOverride_ActorNatural_OcultaCedulas()
    {
        var @override = ChecklistPersonTypeRules.BuildOverride(new string?[] { ActorPersonTypes.Natural });

        @override.Should().NotBeNull();
        @override!.Hide.Should().Contain(ChecklistPersonTypeRules.CedulasItemId);
    }

    [Fact]
    public void Compute_ActorNatural_ChecklistSinCedulas()
    {
        var @override = ChecklistPersonTypeRules.BuildOverride(new string?[] { ActorPersonTypes.Natural });

        var r = ChecklistEngine.ComputeWithOverride(Traspaso, null, null, @override)!;

        r.Items.Should().NotContain(i => i.Item.Id == "cedulas");
        r.FaltanObligatorios.Should().NotContain("cedulas");
    }

    // ── AC3: persona jurídica → el ítem de cédula se conserva ──────────────────────

    [Fact]
    public void BuildOverride_ActorJuridico_SinOverride()
    {
        var @override = ChecklistPersonTypeRules.BuildOverride(new string?[] { ActorPersonTypes.Juridical });

        @override.Should().BeNull();
    }

    [Fact]
    public void Compute_ActorJuridico_ChecklistConservaCedulas()
    {
        var @override = ChecklistPersonTypeRules.BuildOverride(new string?[] { ActorPersonTypes.Juridical });

        var r = ChecklistEngine.ComputeWithOverride(Traspaso, null, null, @override)!;

        r.Items.Should().Contain(i => i.Item.Id == "cedulas");
    }

    [Fact]
    public void BuildOverride_MixtoNaturalYJuridico_ConservaCedulas()
    {
        // Si interviene una persona jurídica, la cédula se mantiene aunque haya un natural.
        var @override = ChecklistPersonTypeRules.BuildOverride(
            new string?[] { ActorPersonTypes.Natural, ActorPersonTypes.Juridical });

        @override.Should().BeNull();
    }

    // ── Backward compatible: sin tipo de persona (legacy) → comportamiento actual ──

    [Fact]
    public void BuildOverride_SinTipoPersona_SinOverride()
    {
        ChecklistPersonTypeRules.BuildOverride(new string?[] { null, null }).Should().BeNull();
        ChecklistPersonTypeRules.BuildOverride(System.Array.Empty<string?>()).Should().BeNull();
    }

    // ── Vocabulario del tipo de persona ───────────────────────────────────────────

    [Theory]
    [InlineData("natural", true)]
    [InlineData("NATURAL", true)]
    [InlineData("juridical", true)]
    [InlineData("juridica", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ActorPersonTypes_IsValid(string? value, bool expected)
    {
        ActorPersonTypes.IsValid(value).Should().Be(expected);
    }

    [Fact]
    public void ActorPersonTypes_Normalize_MinusculasCanonicas()
    {
        ActorPersonTypes.Normalize("NATURAL").Should().Be("natural");
        ActorPersonTypes.Normalize("Juridical").Should().Be("juridical");
        ActorPersonTypes.Normalize("otro").Should().BeNull();
    }
}
