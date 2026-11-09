using Flit.Tramites.Domain.Tramites.PersonaNatural;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #14469 — el pago que entrega el portal se normaliza y valida antes de guardarse.
/// <para>Uso de ejemplo: <c>PagoPortalTramite.Crear(125500, "caja", "efectivo", null, "2026-11-09T15:30:00Z", "cobro-1")</c>.</para>
/// </summary>
public sealed class PagoPortalTramiteTests
{
    [Fact]
    public void UnPagoEnCajaValido_SeNormaliza()
    {
        var pago = PagoPortalTramite.Crear(125_500, " CAJA ", " Efectivo ", "ignorado", "2026-11-09T15:30:00Z", " cobro-1 ");

        pago.Should().NotBeNull();
        pago!.ValorCop.Should().Be(125_500);
        pago.Medio.Should().Be("caja");
        pago.MedioCaja.Should().Be("efectivo");
        pago.MedioDetalle.Should().BeNull("el detalle solo aplica cuando el medio de caja es «otro»");
        pago.PagadoEn.Should().Be(new DateTimeOffset(2026, 11, 9, 15, 30, 0, TimeSpan.Zero));
        pago.CobroId.Should().Be("cobro-1");
    }

    [Fact]
    public void ElMedioOtro_ConservaElDetalle()
    {
        var pago = PagoPortalTramite.Crear(1, "caja", "otro", "  Billetera  ", "2026-11-09T15:30:00Z", "c");

        pago!.MedioDetalle.Should().Be("Billetera");
    }

    [Fact]
    public void SinCobro_NoLlevaMedioDeCaja()
    {
        var pago = PagoPortalTramite.Crear(0, "sin_cobro", "", "", "2026-11-09T15:30:00Z", "c");

        pago.Should().NotBeNull();
        pago!.MedioCaja.Should().BeNull();
        pago.MedioDetalle.Should().BeNull();
    }

    [Theory]
    [InlineData(-1, "caja", "efectivo", "2026-11-09T15:30:00Z", "c")]
    [InlineData(10, "tarjeta", null, "2026-11-09T15:30:00Z", "c")]
    [InlineData(10, "caja", "cheque", "2026-11-09T15:30:00Z", "c")]
    [InlineData(10, "caja", "efectivo", "no es fecha", "c")]
    [InlineData(10, "caja", "efectivo", "2026-11-09T15:30:00Z", "")]
    [InlineData(10, "caja", "efectivo", "2026-11-09T15:30:00Z", null)]
    public void UnPagoIlegible_NoSeCrea(long valor, string? medio, string? medioCaja, string? fecha, string? cobro) =>
        PagoPortalTramite.Crear(valor, medio, medioCaja, null, fecha, cobro).Should().BeNull();
}
