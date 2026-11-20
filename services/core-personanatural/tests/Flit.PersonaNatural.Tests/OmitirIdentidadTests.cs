using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api.Pruebas;
using Flit.PersonaNatural.Api.Radicacion;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Interruptor de pruebas locales «omitir identidad»: apagado = comportamiento de siempre; encendido en Development = la
/// compuerta pasa y la config pública lo expone; encendido fuera de Development = ignorado.
/// </summary>
public sealed class OmitirIdentidadTests : IdentidadBase
{
    private void Configurar(bool flag, string ambiente)
    {
        Servicios = s => s.AddSingleton<Flit.PersonaNatural.Api.Configuracion.IFachadaConfiguracion>(new FachadaConfiguracionFalsa());
        Ajustes = b =>
        {
            b.Environment.EnvironmentName = ambiente;
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [PruebasPortal.ClaveOmitirIdentidad] = flag ? "true" : "false" });
        };
    }

    private Task<ResultadoCompuerta> EvaluarAsync(Guid solicitud) =>
        EnScopeAsync(sp => sp.GetRequiredService<ICompuertaDeRadicacion>().EvaluarAsync(solicitud, TestContext.Current.CancellationToken));

    [Fact]
    public async Task FlagApagado_LaCompuertaBloqueaComoSiempre()
    {
        Configurar(false, "Development");
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", "pendiente");

        (await EvaluarAsync(solicitud)).Permite.Should().BeFalse();
        App.Services.GetRequiredService<IPruebasPortal>().OmitirIdentidad.Should().BeFalse();
    }

    [Fact]
    public async Task FlagEncendidoEnDevelopment_LaCompuertaPasaConIdentidadesPendientesOSinPersonas()
    {
        Configurar(true, "Development");
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "rechazada");
        await PonerEstadoAsync(solicitud, "comprador", "pendiente");

        (await EvaluarAsync(solicitud)).Permite.Should().BeTrue();
        App.Services.GetRequiredService<IPruebasPortal>().OmitirIdentidad.Should().BeTrue();
    }

    [Fact]
    public async Task FlagEncendidoFueraDeDevelopment_SeIgnora_YLaCompuertaBloquea()
    {
        Configurar(true, "Production");
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", "pendiente");

        (await EvaluarAsync(solicitud)).Permite.Should().BeFalse();
        App.Services.GetRequiredService<IPruebasPortal>().OmitirIdentidad.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigPublica_ExponePruebasSoloConElFlagActivo()
    {
        Configurar(true, "Development");
        if (!await IniciarAsync()) return;
        var c = await Config();
        c.TryGetProperty("pruebas", out var pruebas).Should().BeTrue();
        pruebas.GetProperty("omitirIdentidad").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ConfigPublica_SinFlag_NoTrae_ElBloquePruebas()
    {
        Configurar(false, "Development");
        if (!await IniciarAsync()) return;
        (await Config()).TryGetProperty("pruebas", out _).Should().BeFalse();
    }

    private async Task<JsonElement> Config()
    {
        var r = await Http.GetAsync("/api/v1/persona-natural/configuracion", TestContext.Current.CancellationToken);
        return r.IsSuccessStatusCode
            ? await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)
            : throw new InvalidOperationException($"config {(int)r.StatusCode}");
    }
}
