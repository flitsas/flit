using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §5.1, §8) — lo que se decide ANTES de tocar la base, con la API real sin Postgres
/// (<see cref="ApiDePrueba.SinBaseAsync"/>): 403 sin <c>diagnostico.derivacion.crear</c> o con el rol de backoffice (que no lo
/// tiene), 403 <c>DERIVACION_DESDE_BACKOFFICE</c> si la petición trae la empresa explícita del backoffice (<c>?empresa=</c>) aunque
/// se tenga el permiso, 404 con la derivación apagada (por defecto), y el registro: sin <c>Diagnostico:Tramites:Address</c> el
/// cliente no conectado responde TRAMITES_NO_DISPONIBLE; las opciones por defecto (apagada, 15 s) y su validación.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DerivacionSinBaseTests
/// </code>
/// </example>
public sealed class DerivacionSinBaseTests
{
    private static readonly string Ruta = DerivacionDePrueba.Ruta(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task Sin_permiso_o_con_el_rol_de_backoffice_es_403()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: Encendida());
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var backoffice = Permisos.RolesPorDefecto.Single(r => r.Codigo == "backoffice_diagnostico").Permisos;

        (await api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear])).PostAsync(Ruta, null, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Cliente(api.Token(empresa: empresa, permisos: backoffice, roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT")).PostAsync(Ruta, null, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "el rol de backoffice no tiene diagnostico.derivacion.crear (R-6)");
    }

    [Fact]
    public async Task Desde_el_contexto_de_backoffice_es_403_aunque_tenga_el_permiso()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: Encendida());
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        using var cliente = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.DerivacionCrear, Permisos.BackofficeVer], roles: ["admin_diagnostico"], tipoEmpresa: "FLIT"));

        var respuesta = await cliente.PostAsync($"{Ruta}?empresa={Guid.NewGuid()}", null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("DERIVACION_DESDE_BACKOFFICE");
    }

    [Fact]
    public async Task Apagada_por_defecto_la_ruta_no_existe()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Cliente(api.Token(permisos: [Permisos.DerivacionCrear, Permisos.DiagnosticosVer])).PostAsync(Ruta, null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        api.App.Services.GetRequiredService<IOptions<OpcionesDerivacion>>().Value.Habilitada.Should().BeFalse("apagada por defecto (ADR-0084)");
    }

    [Fact]
    public async Task Sin_dirección_de_Trámites_el_cliente_no_conectado_responde_no_disponible_no_transitorio()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [$"{OpcionesDerivacion.Seccion}:Habilitada"] = "true" }));
        await using var scope = api.App.Services.CreateAsyncScope();

        var cliente = scope.ServiceProvider.GetRequiredService<IClienteDerivacionTramites>();
        var r = await cliente.CrearAsync(Guid.NewGuid(), new CrearBorradorDesdeDiagnosticoRequest(), TestContext.Current.CancellationToken);

        cliente.Should().BeOfType<ClienteDerivacionNoConectado>();
        r.Should().BeEquivalentTo(new ResultadoBorrador.NoDisponible(Transitorio: false));
    }

    [Fact]
    public void Opciones_por_defecto_y_validación_del_plazo()
    {
        var porDefecto = new OpcionesDerivacion();
        porDefecto.Habilitada.Should().BeFalse();
        porDefecto.PlazoRpcSegundos.Should().Be(15);
        OpcionesDerivacion.Validar(porDefecto).Should().BeTrue();
        OpcionesDerivacion.Validar(new OpcionesDerivacion { PlazoRpcSegundos = 0 }).Should().BeFalse();
        OpcionesDerivacion.Validar(new OpcionesDerivacion { PlazoRpcSegundos = 121 }).Should().BeFalse();
        OpcionesDerivacion.Validar(new OpcionesDerivacion { UrlTramites = "no es url" }).Should().BeFalse();
        OpcionesDerivacion.Validar(new OpcionesDerivacion { UrlTramites = "https://tramites.flit" }).Should().BeTrue();
    }

    [Fact]
    public void Url_del_trámite_con_y_sin_base()
    {
        var id = Guid.Parse("0199f3a1-7777-7000-8000-000000000077");
        var empresa = Guid.Parse("0199f3a1-8888-7000-8000-000000000088");

        new OpcionesDerivacion { UrlTramites = "https://tramites.flit/" }.UrlTramite(id, empresa)
            .Should().Be($"https://tramites.flit/tramites/{id}?t={empresa}");
        new OpcionesDerivacion().UrlTramite(id, empresa).Should().Be($"/tramites/{id}?t={empresa}");
    }

    private static Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder> Encendida() => b =>
    {
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"{OpcionesDerivacion.Seccion}:Habilitada"] = "true" });
        b.Services.AddSingleton<DerivacionTramitesService.DerivacionTramitesServiceClient>(new DerivacionTramitesFalso());
        b.Services.AddScoped<IClienteDerivacionTramites, ClienteDerivacionTramitesGrpc>();
    };
}
