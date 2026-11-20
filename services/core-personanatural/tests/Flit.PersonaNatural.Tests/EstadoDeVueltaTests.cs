using System.Text.Json;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Radicados;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14050 y #14051 (Épica #14021) — el espejo de lo que core-api resuelve y de los cambios de estado del trámite creado por
/// el portal. Host real con base efímera (sin Postgres se omite en local y falla en CI); los eventos se entregan directo al
/// consumidor, como lo hace el SDK tras su bandeja. Ordenado por la marca de tiempo del cambio, idempotente y solo para la
/// empresa de Persona Natural.
/// </summary>
public sealed class EstadoDeVueltaTests : IdentidadBase
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tramite = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

    // ── Resolución de la solicitud (D4 → BFF) ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnTramiteCreado_PasaLaSolicitudACreadaConElRadicadoYElIdDelTramite()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync(EstadoSolicitud.EnCola);
        var evento = Guid.CreateVersion7();

        await Resolver(solicitud, evento, new SolicitudResuelta(solicitud, "creada", Tramite, "TRM-2026-000123", "entregado", null, null, T0));

        var s = await LeerAsync(solicitud);
        s.Estado.Should().Be("creada");
        s.ProcedureInstanceId.Should().Be(Tramite);
        s.ReferenceNumber.Should().Be("TRM-2026-000123");
        s.EstadoTramite.Should().Be("entregado");
        var h = (await HistorialAsync(solicitud)).Should().ContainSingle().Which;
        h.Should().BeEquivalentTo(new { Desde = "en_cola", Hasta = "creada", Origen = "flit2", EventId = (Guid?)evento, OcurrioEn = T0 });
    }

    [Fact]
    public async Task UnRechazoDeNegocio_PasaLaSolicitudARechazadaCreacion_ConElMotivoEnElHistorial_SinEspejoDeTramite()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync(EstadoSolicitud.EnCola);
        // Un cambio de estado llegó antes de la resolución: no debe sobrevivir a un rechazo.
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "preparado", false, null, T0));

        await Resolver(solicitud, Guid.CreateVersion7(),
            new SolicitudResuelta(solicitud, "rechazada", null, null, null, "tipo_no_disponible", "El tipo de trámite ya no está disponible para el portal.", T0.AddSeconds(5)));

        var s = await LeerAsync(solicitud);
        s.Estado.Should().Be("rechazada_creacion");
        s.ProcedureInstanceId.Should().BeNull();
        s.ReferenceNumber.Should().BeNull();
        s.EstadoTramite.Should().BeNull();
        (await HistorialAsync(solicitud)).Last().Motivo.Should().Be("El tipo de trámite ya no está disponible para el portal.");
    }

    [Fact]
    public async Task LaResolucionRepetidaOTardia_OdeOtraEmpresa_NoCambiaNada()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync(EstadoSolicitud.EnCola);
        var creada = new SolicitudResuelta(solicitud, "creada", Tramite, "TRM-1", "entregado", null, null, T0);

        await Resolver(solicitud, Guid.CreateVersion7(), creada, empresa: Guid.NewGuid());
        (await LeerAsync(solicitud)).Estado.Should().Be("en_cola");

        await Resolver(solicitud, Guid.CreateVersion7(), creada);
        await Resolver(solicitud, Guid.CreateVersion7(), creada with { Resultado = "rechazada", ProcedureInstanceId = null, Codigo = "x" });
        await Resolver(Guid.NewGuid(), Guid.CreateVersion7(), creada with { SolicitudId = Guid.NewGuid() });

        (await LeerAsync(solicitud)).Estado.Should().Be("creada", "ya resuelta: un segundo evento no la cambia");
        (await HistorialAsync(solicitud)).Should().ContainSingle();
    }

    // ── Cambios de estado (D5) ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnCambioDeEstado_ActualizaElEspejoYElHistorialConLaFechaDelCambio()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarCreadaAsync();

        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "aprobado", false, null, T0.AddHours(1)));

        var s = await LeerAsync(solicitud);
        s.EstadoTramite.Should().Be("aprobado");
        s.SubsanacionActiva.Should().BeFalse();
        var h = (await HistorialAsync(solicitud)).Last();
        h.Should().BeEquivalentTo(new { Desde = "entregado", Hasta = "aprobado", Origen = "flit2", OcurrioEn = T0.AddHours(1) });
        MacroEstados.Resolver(s.Estado, s.EstadoTramite).Macro.Should().Be(MacroEstado.Finalizado);
    }

    [Fact]
    public async Task LaSubsanacion_QuedaConSuMotivoEnLenguajeClaro_YSeLimpiaAlReRadicar()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarCreadaAsync();

        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "rechazado", false, "Falta la tarjeta de propiedad.", T0.AddHours(1)));
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "rechazado", true, "Falta la tarjeta de propiedad.", T0.AddHours(2)));

        var s = await LeerAsync(solicitud);
        s.EstadoTramite.Should().Be("rechazado");
        s.SubsanacionActiva.Should().BeTrue();
        s.SubsanacionMotivo.Should().Be("Falta la tarjeta de propiedad.");
        MacroEstados.Resolver(s.Estado, s.EstadoTramite).Macro.Should().Be(MacroEstado.Subsanacion);

        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "entregado", false, null, T0.AddHours(3)));

        s = await LeerAsync(solicitud);
        s.EstadoTramite.Should().Be("entregado");
        s.SubsanacionActiva.Should().BeFalse();
        s.SubsanacionMotivo.Should().BeNull();
        MacroEstados.Resolver(s.Estado, s.EstadoTramite).Macro.Should().Be(MacroEstado.EnRevisionOt);
    }

    [Fact]
    public async Task UnEventoViejoODuplicadoOFueraDeOrden_ConservaElEstadoMasReciente()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarCreadaAsync();
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "aprobado", false, null, T0.AddHours(5)));
        var filas = (await HistorialAsync(solicitud)).Count;

        // Más viejo que el que ya se aplicó (llegó tarde).
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "entregado", false, null, T0.AddHours(2)));
        // Mismo instante y mismo contenido (otro eventId).
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "aprobado", false, null, T0.AddHours(5)));

        (await LeerAsync(solicitud)).EstadoTramite.Should().Be("aprobado");
        (await HistorialAsync(solicitud)).Count.Should().Be(filas);
    }

    [Fact]
    public async Task ElCambioDeOtraEmpresa_DeOtroTramite_DeUnaSolicitudDesconocidaOUnaRechazadaAlCrear_SeIgnora()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarCreadaAsync();
        var (rechazada, _) = await SembrarAsync(EstadoSolicitud.RechazadaCreacion);
        var antes = (await HistorialAsync(solicitud)).Count;

        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "aprobado", false, null, T0.AddHours(1)), empresa: Guid.NewGuid());
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Guid.NewGuid(), "TRM-9", "aprobado", false, null, T0.AddHours(1)));
        await Estado(Guid.NewGuid(), new EstadoDeTramiteCambiado(Guid.NewGuid(), Tramite, "TRM-1", "aprobado", false, null, T0.AddHours(1)));
        await Estado(rechazada, new EstadoDeTramiteCambiado(rechazada, Tramite, "TRM-1", "anulado", false, "x", T0.AddHours(1)));
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "", false, null, T0.AddHours(1)));

        (await LeerAsync(solicitud)).EstadoTramite.Should().Be("entregado");
        (await HistorialAsync(solicitud)).Count.Should().Be(antes);
        (await LeerAsync(rechazada)).EstadoTramite.Should().BeNull();
    }

    [Fact]
    public async Task UnCambioQueLlegaAntesDeLaResolucion_SeAplica_YLaResolucionNoLoPisa()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync(EstadoSolicitud.EnCola);
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "entregado", false, null, T0));

        await Resolver(solicitud, Guid.CreateVersion7(), new SolicitudResuelta(solicitud, "creada", Tramite, "TRM-1", "preparado", null, null, T0.AddSeconds(3)));

        var s = await LeerAsync(solicitud);
        s.Estado.Should().Be("creada");
        s.EstadoTramite.Should().Be("entregado", "el estado inicial del evento de resolución solo rellena un espejo vacío");
        // Y el siguiente cambio se ordena contra el cambio de estado, no contra la fila de resolución (que es posterior).
        await Estado(solicitud, new EstadoDeTramiteCambiado(solicitud, Tramite, "TRM-1", "aprobado", false, null, T0.AddSeconds(1)));
        (await LeerAsync(solicitud)).EstadoTramite.Should().Be("aprobado");
    }

    [Fact]
    public async Task ElServicioRegistraLosDosConsumidoresDelCaminoDeVuelta()
    {
        if (!await IniciarAsync()) return;

        using var scope = App.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<SolicitudResueltaConsumer>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<EstadoDeTramiteConsumer>().Should().NotBeNull();
        EstadoDeVueltaEventos.ColaResolucion.Should().Be("personanatural.resolucion");
        EstadoDeVueltaEventos.ColaEstado.Should().Be("personanatural.estado");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(Guid Solicitud, string Token)> SembrarCreadaAsync()
    {
        var sembrada = await SembrarAsync(EstadoSolicitud.Creada);
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
        var s = await db.Set<SolicitudTramite>().SingleAsync(x => x.Id == sembrada.Solicitud, TestContext.Current.CancellationToken);
        s.ProcedureInstanceId = Tramite;
        s.ReferenceNumber = "TRM-1";
        s.EstadoTramite = "entregado";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return sembrada;
    }

    private Task<int> Resolver(Guid solicitud, Guid evento, SolicitudResuelta datos, Guid? empresa = null) =>
        EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<SolicitudResueltaConsumer>().HandleAsync(Sobre(evento, empresa ?? PlatformTenants.PersonaNatural, EstadoDeVueltaEventos.TipoResuelta), datos, TestContext.Current.CancellationToken);
            await sp.GetRequiredService<PersonaNaturalDb>().SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });

    private Task<int> Estado(Guid solicitud, EstadoDeTramiteCambiado datos, Guid? empresa = null) =>
        EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<EstadoDeTramiteConsumer>().HandleAsync(Sobre(Guid.CreateVersion7(), empresa ?? PlatformTenants.PersonaNatural, EstadoDeVueltaEventos.TipoEstado), datos, TestContext.Current.CancellationToken);
            await sp.GetRequiredService<PersonaNaturalDb>().SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });

    private static EventEnvelope Sobre(Guid evento, Guid empresa, string tipo) =>
        new(evento, tipo, 1, DateTimeOffset.UtcNow, empresa, "tramites", "corr", JsonSerializer.SerializeToElement(new { }));

    private Task<SolicitudTramite> LeerAsync(Guid solicitud) =>
        EnScopeAsync(sp => sp.GetRequiredService<PersonaNaturalDb>().Set<SolicitudTramite>().AsNoTracking().SingleAsync(x => x.Id == solicitud, TestContext.Current.CancellationToken));

    private Task<List<HistorialEstado>> HistorialAsync(Guid solicitud) =>
        EnScopeAsync(sp => sp.GetRequiredService<PersonaNaturalDb>().Set<HistorialEstado>().AsNoTracking()
            .Where(h => h.SolicitudId == solicitud).OrderBy(h => h.OcurrioEn).ToListAsync(TestContext.Current.CancellationToken));
}
