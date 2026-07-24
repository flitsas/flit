using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #10871 — checklist HÍBRIDO de subsanación: <c>{ "motivo": "...", "items": [{ "campo": "...",
/// "detalle": "..." }] }</c>, persistido en <c>procedure_instance_status_history.metadata</c>.
/// </summary>
public sealed class SubsanacionObservationTests
{
    [Fact]
    public void ToJson_ConMotivoEItems_SerializaElShapeHibrido()
    {
        var observation = new SubsanacionObservation
        {
            Motivo = "Faltan documentos",
            Items =
            [
                new SubsanacionObservationItem { Campo = "factura", Detalle = "Falta la factura de compra" },
            ],
        };

        var json = observation.ToJson();

        var roundTripped = SubsanacionObservation.FromJson(json);
        roundTripped.Should().NotBeNull();
        roundTripped!.Motivo.Should().Be("Faltan documentos");
        roundTripped.Items.Should().ContainSingle();
        roundTripped.Items[0].Campo.Should().Be("factura");
        roundTripped.Items[0].Detalle.Should().Be("Falta la factura de compra");
    }

    [Fact]
    public void ToJson_SoloMotivo_ItemsQuedaVacio()
    {
        var observation = new SubsanacionObservation { Motivo = "Rechazado por Quipux" };

        var roundTripped = SubsanacionObservation.FromJson(observation.ToJson());

        roundTripped.Should().NotBeNull();
        roundTripped!.Motivo.Should().Be("Rechazado por Quipux");
        roundTripped.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ esto no es json valido")]
    public void FromJson_NuloVacioOCorrupto_DevuelveNull(string? json)
    {
        SubsanacionObservation.FromJson(json).Should().BeNull();
    }
}
