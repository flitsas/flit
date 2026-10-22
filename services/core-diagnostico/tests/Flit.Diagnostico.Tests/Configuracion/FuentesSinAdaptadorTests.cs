using System.Collections.Concurrent;
using System.Net;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;
using static Flit.Diagnostico.Tests.Configuracion.ContratoBackofficeTests;

namespace Flit.Diagnostico.Tests.Configuracion;

/// <summary>
/// HU #13733 (F03-H5), AC5 y AC6 (F03-C8, F03-C9, F03-C15) — guardia de fuentes sin adaptador: el catálogo
/// <see cref="FuentesDisponibles"/> (fuentes con un ejecutor registrado; en D2 solo RUNT) lo consultan el contrato del
/// backoffice y <c>POST /diagnosticos</c>. Habilitar o pedir una fuente sin adaptador responde 422 y no guarda nada.
/// <c>IMPUESTO_DEPARTAMENTAL</c> además exige <c>Diagnostico:Fuentes:ImpuestoDisponible=true</c> (ausente = false). Estas
/// pruebas quitan el doble de <see cref="ApiDePrueba"/> que da a toda fuente por adaptada y usan el registro real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~FuentesSinAdaptadorTests
/// </code>
/// </example>
public sealed class FuentesSinAdaptadorTests
{
    // AC5 (F03-C8) — el backoffice habilita SIMIT_VEHICULO, que aquí no está disponible (en D2 no tenía adaptador; desde HU
    // #13793 lo tiene, pero sin simulación ni dirección de core-consultas es no conectado): 422 y el contrato no cambia.
    [Fact]
    public async Task Contrato_con_fuente_sin_adaptador_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT", "SIMIT_VEHICULO"], "limite_vehiculos_lote": 100 }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await respuesta.Content.ReadAsStringAsync(ct));
        (await Codigo(respuesta, ct)).Should().Be(ServicioConfiguracion.CodigoFuenteNoDisponible);
        var deB = await LeerAsync(api, empresas.B.Id, ct);
        deB.FuentesHabilitadas.Should().Equal(Fuente.Runt);
        deB.LimiteVehiculosLote.Should().Be(500, "un 422 no guarda ningún campo");
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.cambios_contrato", ct)).Should().Be(0);
    }

    // AC5 — con un adaptador registrado para la fuente, el mismo contrato se guarda (la guardia mira el registro, no una lista fija).
    // Desde HU #13791 el adaptador además debe estar conectado: aquí la simulación en DEV (sin ella, SIMIT es no conectada).
    [Fact]
    public async Task Contrato_con_fuente_con_adaptador_se_guarda()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ArrancarAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Flit.Diagnostico.Api.Modulos.Modulos.ClaveSimulacionHabilitada] = "true",
                [Flit.Diagnostico.Api.Modulos.Modulos.ClaveAmbiente] = "DEV",
            });
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, new ConcurrentQueue<string>()).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT", "SIMIT_VEHICULO"] }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        (await LeerAsync(api, empresas.B.Id, ct)).FuentesHabilitadas.Should().Equal(Fuente.Runt, Fuente.SimitVehiculo);
    }

    // AC5 — la compañía tiene SIMIT_VEHICULO habilitada de antes, pero no disponible (no conectada): pedirla al crear es 422.
    [Fact]
    public async Task Crear_diagnostico_con_fuente_sin_adaptador_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.SimitVehiculo] }, ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));

        var respuesta = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT", "SIMIT_VEHICULO"])), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await respuesta.Content.ReadAsStringAsync(ct));
        (await Codigo(respuesta, ct)).Should().Be(ValidadorCabecera.CodigoFuenteNoDisponible);
        (await Cabeceras.ContarAsync(api, empresas.A.Id, ct)).Should().Be(0);
        (await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT"])), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created, "RUNT sí tiene adaptador");
    }

    // AC6 (F03-C9, F03-C15) — con la bandera en false, IMPUESTO_DEPARTAMENTAL es 422 en el contrato y en la creación,
    // aunque haya un adaptador registrado para el impuesto.
    [Fact]
    public async Task Impuesto_con_bandera_false_responde_422_en_contrato_y_en_creacion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ArrancarAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [DisponibilidadFuentes.ClaveImpuestoDisponible] = "false" });
            new EjecutorFuenteDePrueba(Fuente.ImpuestoDepartamental, new ConcurrentQueue<string>()).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.ImpuestoDepartamental] }, ct);

        var contrato = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT", "IMPUESTO_DEPARTAMENTAL"] }"""), ct);
        var creacion = await api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id))
            .PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT", "IMPUESTO_DEPARTAMENTAL"])), ct);

        contrato.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Codigo(contrato, ct)).Should().Be(ServicioConfiguracion.CodigoFuenteNoDisponible);
        (await LeerAsync(api, empresas.B.Id, ct)).FuentesHabilitadas.Should().Equal(Fuente.Runt);
        creacion.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Codigo(creacion, ct)).Should().Be(ValidadorCabecera.CodigoFuenteNoDisponible);
        (await Cabeceras.ContarAsync(api, empresas.A.Id, ct)).Should().Be(0);
    }

    // AC6 — sin la clave en la configuración el impuesto no está disponible; con true y adaptador, sí; sin adaptador, nunca.
    [Fact]
    public void Bandera_ausente_equivale_a_false()
    {
        var conImpuesto = new RegistroFijo(Fuente.Runt, Fuente.ImpuestoDepartamental);
        new FuentesDisponibles(conImpuesto, new DisponibilidadFuentes(new ConfigurationBuilder().Build()))
            .Disponible(Fuente.ImpuestoDepartamental).Should().BeFalse("sin la bandera, el impuesto no está disponible");
        var conBandera = new DisponibilidadFuentes(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DisponibilidadFuentes.ClaveImpuestoDisponible] = "true" }).Build());
        new FuentesDisponibles(conImpuesto, conBandera).Disponible(Fuente.ImpuestoDepartamental).Should().BeTrue();
        new FuentesDisponibles(new RegistroFijo(Fuente.Runt), conBandera).Disponible(Fuente.ImpuestoDepartamental).Should().BeFalse("sin adaptador no basta la bandera");
        new FuentesDisponibles(conImpuesto, conBandera).Disponible(Fuente.SimitVehiculo).Should().BeFalse();
    }

    // El registro real del servicio: tienen adaptador RUNT (HU #13730), desde HU #13792 RUNT_PROPIETARIO y RUNT_LOCATARIO,
    // desde HU #13793 SIMIT_VEHICULO, desde HU #13795 AVALUO y, desde HU #13794, SIMIT_PROPIETARIO y SIMIT_LOCATARIO (cambio
    // por diseño: cada HU de F05 suma el ejecutor de su fuente; solo IMPUESTO_DEPARTAMENTAL sigue sin adaptador).
    [Fact]
    public async Task Tienen_adaptador_todas_las_fuentes_salvo_el_impuesto()
    {
        await using var api = await ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var registro = api.App.Services.GetRequiredService<IRegistroAdaptadores>();
        Enum.GetValues<Fuente>().Where(registro.TieneAdaptador).Should().Equal(
            Fuente.Runt, Fuente.RuntPropietario, Fuente.RuntLocatario, Fuente.SimitVehiculo, Fuente.SimitPropietario, Fuente.SimitLocatario, Fuente.Avaluo);
    }

    /// <summary>API con el registro de adaptadores real (sin el doble de <see cref="ApiDePrueba"/>).</summary>
    private static Task<ApiDePrueba> ArrancarAsync(Action<WebApplicationBuilder>? configurar = null) =>
        ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.RemoveAll<IRegistroAdaptadores>();
            configurar?.Invoke(b);
        });

    private sealed class RegistroFijo(params Fuente[] fuentes) : IRegistroAdaptadores
    {
        public bool TieneAdaptador(Fuente fuente) => fuentes.Contains(fuente);
    }
}
