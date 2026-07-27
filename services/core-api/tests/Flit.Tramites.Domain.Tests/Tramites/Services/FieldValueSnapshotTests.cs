using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Tramites.Services;

/// <summary>
/// HU #10872 (AC1) — snapshot puro de field_values y su diff: baseline (al entrar a subsanación) vs.
/// estado actual (al re-radicar). Es la entrada de <see cref="SubsanacionGateMap.ResolveGates"/>.
/// </summary>
public sealed class FieldValueSnapshotTests
{
    private static ProcedureInstanceFieldValue Fv(string key, string? text, string? json = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureInstanceId = Guid.NewGuid(),
        FieldKey = key,
        ValueText = text,
        ValueJson = json,
    };

    [Fact]
    public void Capture_UsaValueText_ComoValorCanonico()
    {
        var snapshot = FieldValueSnapshot.Capture([Fv("vin", "1HGCM82633A004352")]);

        snapshot.Should().ContainKey("vin").WhoseValue.Should().Be("1HGCM82633A004352");
    }

    [Fact]
    public void Capture_SinValueText_CaeAValueJson()
    {
        var snapshot = FieldValueSnapshot.Capture([Fv("payload", text: null, json: "{\"a\":1}")]);

        snapshot["payload"].Should().Be("{\"a\":1}");
    }

    [Fact]
    public void Capture_ClavesCaseInsensitive()
    {
        var snapshot = FieldValueSnapshot.Capture([Fv("VIN", "abc")]);

        snapshot.Should().ContainKey("vin");
    }

    [Fact]
    public void Diff_SinCambios_DevuelveConjuntoVacio()
    {
        var baseline = FieldValueSnapshot.Capture([Fv("vin", "abc"), Fv("transit_office_id", "x")]);
        var current = FieldValueSnapshot.Capture([Fv("vin", "abc"), Fv("transit_office_id", "x")]);

        FieldValueSnapshot.Diff(baseline, current).Should().BeEmpty();
    }

    [Fact]
    public void Diff_ValorModificado_MarcaLaClaveComoCambiada()
    {
        var baseline = FieldValueSnapshot.Capture([Fv("vin", "abc")]);
        var current = FieldValueSnapshot.Capture([Fv("vin", "def")]);

        FieldValueSnapshot.Diff(baseline, current).Should().ContainSingle().Which.Should().Be("vin");
    }

    [Fact]
    public void Diff_CampoAgregado_SeMarcaComoCambiado()
    {
        var baseline = FieldValueSnapshot.Capture([Fv("vin", "abc")]);
        var current = FieldValueSnapshot.Capture([Fv("vin", "abc"), Fv("color", "rojo")]);

        FieldValueSnapshot.Diff(baseline, current).Should().ContainSingle().Which.Should().Be("color");
    }

    [Fact]
    public void Diff_CampoRemovido_SeMarcaComoCambiado()
    {
        var baseline = FieldValueSnapshot.Capture([Fv("vin", "abc"), Fv("color", "rojo")]);
        var current = FieldValueSnapshot.Capture([Fv("vin", "abc")]);

        FieldValueSnapshot.Diff(baseline, current).Should().ContainSingle().Which.Should().Be("color");
    }

    [Fact]
    public void Diff_ClavesCaseInsensitive_NoMarcaCambioPorSoloElCasing()
    {
        var baseline = FieldValueSnapshot.Capture([Fv("VIN", "abc")]);
        var current = FieldValueSnapshot.Capture([Fv("vin", "abc")]);

        FieldValueSnapshot.Diff(baseline, current).Should().BeEmpty();
    }
}
