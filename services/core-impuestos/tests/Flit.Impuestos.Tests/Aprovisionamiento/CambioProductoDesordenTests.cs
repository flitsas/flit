using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (revisión previa al PR: MEN-6) — <see cref="ConsumidorCambioProducto"/> guarda el <c>changedAt</c> del último cambio de
/// producto aplicado e ignora los que llegan después con un <c>changedAt</c> anterior: un «encender» reprocesado después de un
/// «apagar» más nuevo no reactiva ni da de alta.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~CambioProductoDesordenTests
/// </code>
/// </example>
public sealed class CambioProductoDesordenTests
{
    [Fact]
    public async Task Encender_reprocesado_despues_de_apagar_no_reactiva_ni_da_de_alta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Listo, ct);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t2 = t1.AddMinutes(5);

        (await EntornoAprovisionamiento.EntregarAsync(api, Cambio(empresa, encendido: false, t2), ct)).Should().BeTrue();
        (await EntornoAprovisionamiento.EntregarAsync(api, Cambio(empresa, encendido: true, t1), ct)).Should().BeTrue("se confirma, sin efecto");

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Suspendido, "el encendido es anterior al apagado ya aplicado");
        fila.EstadoPrevio.Should().Be(E.Listo);
        fila.Version.Should().Be(2, "solo el apagado subió la versión");
        (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().ContainSingle(h => h.Hacia == E.Suspendido);
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(0);
    }

    [Fact]
    public async Task Un_cambio_sin_efecto_tambien_cuenta_como_el_ultimo_aplicado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Listo, ct);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-10);

        // «Encender» a las t1+5 (ya encendida: sin efecto) y después llega tarde un «apagar» de t1.
        (await EntornoAprovisionamiento.EntregarAsync(api, Cambio(empresa, encendido: true, t1.AddMinutes(5)), ct)).Should().BeTrue();
        (await EntornoAprovisionamiento.EntregarAsync(api, Cambio(empresa, encendido: false, t1), ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo, "el apagado es anterior al último cambio recibido");
    }

    private static EventEnvelope Cambio(Guid empresa, bool encendido, DateTimeOffset cuando) =>
        EntornoAprovisionamiento.Sobre(ConsumidorCambioProducto.TipoCambio, empresa,
            new { tenantId = empresa, productCode = "impuestos", enabled = encendido, changedAt = cuando });
}
