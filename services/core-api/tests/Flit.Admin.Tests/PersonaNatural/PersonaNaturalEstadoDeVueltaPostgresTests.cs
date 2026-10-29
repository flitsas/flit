using System.Text.Json;
using Flit.Admin.Tests.Identity;
using Flit.Api.PersonaNatural;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Escenario = Flit.Admin.Tests.PersonaNatural.PersonaNaturalRadicacionPostgresTests.Escenario;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14051 (Épica #14021) contra Postgres real: el espejo de estado de vuelta que publica core-api. Cada cambio de estado de un
/// trámite creado por el portal y la apertura de una subsanación salen como <c>estado_cambiado</c> (sin datos personales); lo
/// que no es del portal (otro origen, otra empresa, otro trámite) no sale. Y el registro: apagado por defecto.
/// </summary>
public sealed class PersonaNaturalEstadoDeVueltaPostgresTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(_ => { }).ConEmpresaPersonaNatural();


    [Fact]
    public async Task UnCambioDeEstadoDeUnTramiteDelPortal_PublicaEstadoCambiadoConElMotivoDelOrganismo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var id = await e.SembrarTramiteAsync(origen: "persona_natural", externalRef: e.SolicitudId.ToString(), empresa: PlatformTenants.PersonaNatural, ct);
        var outbox = new OutboxFalso();
        var consumidor = new EstadoDelPortalConsumer(e.Db, outbox, Microsoft.Extensions.Logging.Abstractions.NullLogger<EstadoDelPortalConsumer>.Instance);
        var ocurrio = DateTimeOffset.UtcNow.AddMinutes(-1);

        await consumidor.HandleAsync(
            Sobre(PlatformTenants.PersonaNatural, ocurrio),
            new CambioDeEstadoDatos(id, "entregado", "rechazado", "Falta la tarjeta de propiedad legible."), ct);

        var datos = outbox.Eventos.Should().ContainSingle().Which.Datos.Should().BeOfType<EstadoCambiadoDatos>().Which;
        datos.SolicitudId.Should().Be(e.SolicitudId);
        datos.ProcedureInstanceId.Should().Be(id);
        datos.Estado.Should().Be("rechazado");
        datos.Motivo.Should().Be("Falta la tarjeta de propiedad legible.");
        datos.SubsanacionActiva.Should().BeFalse();
        datos.OcurrioEn.Should().Be(ocurrio, "la marca de tiempo del cambio es la del evento de FLIT 2");
    }

    [Fact]
    public async Task UnTramiteQueNoNacioEnElPortal_OLlegaDeOtraEmpresa_NoPublicaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var deFlit2 = await e.SembrarTramiteAsync(origen: null, externalRef: null, empresa: PlatformTenants.PersonaNatural, ct);
        var outbox = new OutboxFalso();
        var consumidor = new EstadoDelPortalConsumer(e.Db, outbox, Microsoft.Extensions.Logging.Abstractions.NullLogger<EstadoDelPortalConsumer>.Instance);

        await consumidor.HandleAsync(Sobre(PlatformTenants.PersonaNatural, DateTimeOffset.UtcNow), new CambioDeEstadoDatos(deFlit2, "entregado", "aprobado", null), ct);
        await consumidor.HandleAsync(Sobre(Guid.NewGuid(), DateTimeOffset.UtcNow), new CambioDeEstadoDatos(deFlit2, "entregado", "aprobado", null), ct);
        await consumidor.HandleAsync(Sobre(PlatformTenants.PersonaNatural, DateTimeOffset.UtcNow), new CambioDeEstadoDatos(Guid.NewGuid(), "entregado", "aprobado", null), ct);

        outbox.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task AbrirUnaSubsanacion_PublicaEstadoCambiadoConSubsanacionActivaYElMotivoDelUltimoRechazo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var id = await e.SembrarTramiteAsync(origen: "persona_natural", externalRef: e.SolicitudId.ToString(), empresa: PlatformTenants.PersonaNatural, ct,
            estado: "rechazado", motivoRechazo: "Adjunte la cédula por ambos lados.");
        var outbox = new OutboxFalso();
        var publicador = new PersonaNaturalSubsanacionPublisher(e.Db, outbox, TimeProvider.System);
        var instancia = await e.Db.Set<ProcedureInstance>().AsNoTracking().FirstAsync(p => p.Id == id, ct);

        await publicador.EnqueueAsync(instancia, ct);

        var datos = outbox.Eventos.Should().ContainSingle().Which.Datos.Should().BeOfType<EstadoCambiadoDatos>().Which;
        datos.Estado.Should().Be("rechazado");
        datos.SubsanacionActiva.Should().BeTrue();
        datos.Motivo.Should().Be("Adjunte la cédula por ambos lados.");
        datos.SolicitudId.Should().Be(e.SolicitudId);
    }

    [Fact]
    public async Task AbrirUnaSubsanacionDeUnTramiteDeFlit2_NoPublicaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var id = await e.SembrarTramiteAsync(origen: "ict", externalRef: Guid.NewGuid().ToString(), empresa: PlatformTenants.PersonaNatural, ct, estado: "rechazado");
        var outbox = new OutboxFalso();
        var instancia = await e.Db.Set<ProcedureInstance>().AsNoTracking().FirstAsync(p => p.Id == id, ct);

        await new PersonaNaturalSubsanacionPublisher(e.Db, outbox, TimeProvider.System).EnqueueAsync(instancia, ct);

        outbox.Eventos.Should().BeEmpty();
    }


    [Fact]
    public void Encendido_RegistraElPublicadorDeSubsanacionYElConsumidorDeEstado_ConLosDosConsumidoresEnTotal()
    {
        var services = new ServiceCollection();

        services.AddPersonaNaturalConsumidor(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PersonaNaturalConsumidorExtensions.ClaveHabilitado] = "true",
            ["Tramites:Bus:Habilitado"] = "true",
            [PersonaNaturalConsumidorExtensions.ClaveDireccion] = "http://core-personanatural:8090",
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://localhost:5672/flit",
            ["Platform:ServiceClient:ClientId"] = "svc-tramites",
            ["Platform:ServiceClient:ClientSecret"] = "x",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://localhost:4002/connect/token",
        }).Build());

        services.Should().Contain(d => d.ServiceType == typeof(Flit.Tramites.Domain.Integration.ITramiteSubsanacionPublisher));
        services.Count(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).Should().Be(2);
    }

    private static Flit.Platform.Sdk.Messaging.EventEnvelope Sobre(Guid empresa, DateTimeOffset ocurrio) =>
        new(Guid.CreateVersion7(), "tramites.procedure.state_changed", 1, ocurrio, empresa, "tramites", "corr", JsonSerializer.SerializeToElement(new { }));
}
