using System.Net;
using Flit.PersonaNatural.Api.Definiciones;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14078 (Feature K, Épica #14021) — el BFF entrega la definición del tipo desde la fachada de core-api (falsa aquí), con
/// caché corta, y no ofrece un tipo no disponible o con una sección que el portal no pinta. Base efímera propia; sin motor se
/// omite en local y falla en CI.
/// </summary>
public sealed class DefinicionesTests : IAsyncLifetime
{
    private PortalHost _host = null!;
    private bool _iniciado;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(params (string Clave, string Valor)[] ajustes)
    {
        _host = await PortalHost.IniciarAsync("pndef", null, ajustes);
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
    }

    private Task<HttpResponseMessage> Pedir(string code, string? organismo = PortalHost.Organismo) =>
        _host.EnviarAsync(HttpMethod.Get, $"/api/v1/persona-natural/tipos-tramite/{code}/definicion" + (organismo is null ? string.Empty : $"?organismoId={organismo}"));

    [Fact]
    public async Task LaDefinicionSale_ConPasosActoresDocumentosYFlags_YSinSesionNiCaptcha()
    {
        await IniciarAsync();

        var r = await Pedir("TRASPASO_STANDARD");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("TRASPASO_STANDARD");
        json.GetProperty("version").GetInt32().Should().Be(1);
        json.GetProperty("huella").GetString().Should().Be("h1");
        json.GetProperty("pasos").GetArrayLength().Should().Be(4);
        json.GetProperty("pasos")[2].GetProperty("campos")[1].GetProperty("opciones")[0].GetString().Should().Be("particular");
        json.GetProperty("actores")[1].GetProperty("rol").GetString().Should().Be("comprador");
        json.GetProperty("actores")[1].GetProperty("max").GetInt32().Should().Be(4);
        json.GetProperty("documentos")[3].GetProperty("condicion").GetString().Should().Contain("tramitador");
        json.GetProperty("flags").GetProperty("requiereComercial").GetBoolean().Should().BeTrue();
        _host.Definiciones.UltimoOrganismo.Should().Be(PortalHost.Organismo);
    }

    [Fact]
    public async Task LaDefinicionSeCacheaUnosSegundos_PorTipoYOrganismo()
    {
        await IniciarAsync();
        var otroOrganismo = "0199b7e0-0000-7000-8000-000000000002";

        await Pedir("TRASPASO_STANDARD");
        await Pedir("traspaso_standard");
        await Pedir("TRASPASO_STANDARD", otroOrganismo);

        _host.Definiciones.Llamadas.Should().Be(2, "la segunda lectura del mismo organismo sale de la caché; otro organismo es otra clave");
    }

    [Fact]
    public async Task SinOrganismo_SePideLaMatrizPorDefecto()
    {
        await IniciarAsync();

        (await Pedir("TRASPASO_STANDARD", organismo: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        _host.Definiciones.UltimoOrganismo.Should().BeNull();
    }

    [Fact]
    public async Task UnTipoNoDisponibleParaElPortal_Responde404ConSuCodigo_YNadaParcial()
    {
        await IniciarAsync();
        _host.Definiciones.PorCodigo["NO_ELEGIBLE"] = DefinicionesDePrueba.UnTitular("NO_ELEGIBLE") with { Disponible = false, MotivoNoDisponible = "exige leasing" };

        var noElegible = await Pedir("NO_ELEGIBLE");
        var desconocido = await Pedir("NO_EXISTE");

        noElegible.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PortalHost.CodigoAsync(noElegible)).Should().Be("tipo_no_disponible");
        (await noElegible.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("pasos");
        (await PortalHost.CodigoAsync(desconocido)).Should().Be("tipo_no_disponible");
    }

    [Fact]
    public async Task UnaSeccionQueElPortalNoPinta_HaceQueElTipoNoSeaDisponibleEntero()
    {
        await IniciarAsync();
        _host.Definiciones.PorCodigo["RARO"] = DefinicionesDePrueba.UnTitular("RARO") with
        {
            Pasos = [new PasoDto("x", "X", ["generic_form", "seccion_inventada"], [])],
        };

        var r = await Pedir("RARO");

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PortalHost.CodigoAsync(r)).Should().Be("tipo_no_disponible");
    }

    [Fact]
    public async Task LosErroresDeLaFachada_SeTraducenConSuCodigoEstable()
    {
        await IniciarAsync();
        _host.Definiciones.ErrorAlPedir = "organismo_no_habilitado";

        var r = await Pedir("TRASPASO_STANDARD");

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("organismo_no_habilitado");
    }

    [Fact]
    public async Task UnOrganismoMalFormado_Responde400SinLlamarALaFachada()
    {
        await IniciarAsync();

        var r = await Pedir("TRASPASO_STANDARD", "no-es-guid");

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _host.Definiciones.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task SinFachadaConfigurada_Responde503ConCodigo_YElServicioArranca()
    {
        _host = await PortalHost.IniciarAsync("pndef", (servicios, host) =>
        {
            servicios.RemoveAll<IFachadaDefiniciones>();
        });
        _iniciado = true;

        var r = await Pedir("TRASPASO_STANDARD");

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PortalHost.CodigoAsync(r)).Should().Be("servicio_no_disponible");
    }

    [Fact]
    public void ElSnapshotSeSerializaYSeLeeSinPerderNada()
    {
        var original = DefinicionesDePrueba.Traspaso();

        var leida = DefinicionesService.Leer(DefinicionesService.Serializar(original));

        leida.Should().BeEquivalentTo(original);
        DefinicionesService.Leer("no es json").Should().BeNull();
    }
}
