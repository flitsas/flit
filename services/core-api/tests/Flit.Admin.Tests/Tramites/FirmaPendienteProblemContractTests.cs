using System.Text.Json;
using Flit.Api.Endpoints.Tramites;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// Bug #13194 (review PR #510, MAYOR-1) — contrato del 409 del gate de firma que comparten
/// <c>/transition</c>, <c>/submit</c>, <c>/enviar-al-ot</c> y <c>/plate-flow/complete</c>:
/// <c>title</c> = código, <c>detail</c> = texto y la extensión
/// <c>partesSinFirma: [{ "parte": "comprador", "notificacion": "enviada" }]</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = FirmaPendienteProblem.Crear("firma_pendiente", null, [new ParteSinFirma("comprador", "enviada")]);
/// await r.ExecuteAsync(httpContext); // 409 application/problem+json con partesSinFirma
/// </code>
/// </remarks>
public sealed class FirmaPendienteProblemContractTests
{
    private static async Task<(int Status, JsonElement Body)> EjecutarAsync(IResult result)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        return (ctx.Response.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    [Fact]
    public async Task ConPartes_LlevaLaExtensionPartesSinFirma_YElDetalleDeTexto()
    {
        var (status, body) = await EjecutarAsync(FirmaPendienteProblem.Crear(
            TramiteEstadoErrores.FirmaPendiente,
            null,
            [
                new ParteSinFirma("comprador", FirmaNotificacionEstados.Enviada),
                new ParteSinFirma("vendedor", FirmaNotificacionEstados.YaEnCurso),
            ]));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("title").GetString().Should().Be("firma_pendiente");
        body.GetProperty("detail").GetString().Should()
            .Contain("comprador (notificación: enviada)").And.Contain("vendedor (notificación: ya_en_curso)");

        var partes = body.GetProperty(FirmaPendienteProblem.Extension).EnumerateArray().ToList();
        partes.Should().HaveCount(2);
        partes[0].GetProperty("parte").GetString().Should().Be("comprador");
        partes[0].GetProperty("notificacion").GetString().Should().Be("enviada");
        partes[1].GetProperty("parte").GetString().Should().Be("vendedor");
        partes[1].GetProperty("notificacion").GetString().Should().Be("ya_en_curso");
        partes[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["parte", "notificacion"],
            "el contrato son exactamente estas dos claves, en camelCase");
    }

    [Fact]
    public async Task ConDetalleExplicito_LoConserva()
    {
        var (_, body) = await EjecutarAsync(FirmaPendienteProblem.Crear(
            TramiteEstadoErrores.FirmaPendiente, "detalle del ciclo de vida",
            [new ParteSinFirma("comprador", FirmaNotificacionEstados.Fallida)]));

        body.GetProperty("detail").GetString().Should().Be("detalle del ciclo de vida");
        body.GetProperty(FirmaPendienteProblem.Extension)[0].GetProperty("notificacion").GetString()
            .Should().Be("fallida");
    }

    [Fact]
    public async Task SinPartesRegistradas_SinExtension_YDetalleGenerico()
    {
        var (status, body) = await EjecutarAsync(FirmaPendienteProblem.Crear(
            TramiteEstadoErrores.FirmaPendiente, null, null));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.TryGetProperty(FirmaPendienteProblem.Extension, out _).Should().BeFalse();
        body.GetProperty("detail").GetString().Should().Be(FirmaPendienteProblem.DetalleGenerico);
    }
}
