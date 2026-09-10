using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.RuntConfirmation;

/// <summary>HU #12310 AC5/AC6 — «Consultar ahora»: corre apagada y en tope, cuenta como intento, y rechaza lo que no tiene sentido consultar.</summary>
public sealed class ConsultNowHandlerTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string CambioColorOk = """
        {"ok":true,"data":{"vehiculo":{"placa":"QZU024","mostrarSolicitudes":"SI"},
        "solicitudes":[{"noSolicitud":"302345557","fechaSolicitud":"2026-09-08T15:49:49.000-05:00","estado":"AUTORIZADA","tramitesRealizados":"TRÁMITE CAMBIO COLOR, ","entidad":"STRIA TTEyTTO BELLO"}]}}
        """;

    [Fact]
    public async Task ConTramiteAprobadoEnTope_YInterruptorApagado_ConsultaYDejaIntentoManual()
    {
        var (handler, store) = Build(enabled: false, out var client);
        var c = Candidate("CAMBIO_COLOR") with { RuntAttempts = 10, RuntFlag = "tope" };
        store.Candidates.Add(c);
        client.Result = new RuntRawQueryResult(RuntRawOutcome.Found, CambioColorOk, null);

        var result = await handler.HandleAsync(new ConsultNowCommand(c.InstanceId, Actor), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ConsultNowStatus.Ok);
        result.Attempt!.RequestedBy.Should().Be(Actor);
        result.Attempt.RunId.Should().Be(result.Run!.Id);
        result.Run.Trigger.Should().Be("manual");
        result.Attempt.Verdict.Should().Be("confirmed");
        result.Attempt.AttemptNo.Should().Be(11, "cuenta como intento igual que uno programado");
        store.Candidates.Single().RuntAttempts.Should().Be(11);
        store.Candidates.Single().RuntConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TramiteNoAprobado_Devuelve409()
    {
        var (handler, store) = Build(enabled: true, out _);
        var c = Candidate("CAMBIO_COLOR");
        store.Candidates.Add(c);
        store.Statuses[c.InstanceId] = "entregado";

        var result = await handler.HandleAsync(new ConsultNowCommand(c.InstanceId, Actor), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ConsultNowStatus.Conflict);
        result.ConflictCode.Should().Be("tramite_no_aprobado");
        store.Attempts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("REMATRICULA")]
    [InlineData("CANCELACION_MATRICULA")]
    public async Task TipoFueraDeAlcance_Devuelve409(string type)
    {
        var (handler, store) = Build(enabled: true, out _);
        var c = Candidate(type) with { Family = ProcedureFamily.Matriculas };
        store.Candidates.Add(c);

        var result = await handler.HandleAsync(new ConsultNowCommand(c.InstanceId, Actor), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ConsultNowStatus.Conflict);
        result.ConflictCode.Should().Be("tipo_fuera_de_alcance");
    }

    [Fact]
    public async Task YaConfirmado_Devuelve409()
    {
        var (handler, store) = Build(enabled: true, out _);
        var c = Candidate("CAMBIO_COLOR") with { RuntConfirmedAt = DateTimeOffset.UtcNow };
        store.Candidates.Add(c);

        var result = await handler.HandleAsync(new ConsultNowCommand(c.InstanceId, Actor), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ConsultNowStatus.Conflict);
        result.ConflictCode.Should().Be("ya_confirmado");
    }

    [Fact]
    public async Task Inexistente_Devuelve404()
    {
        var (handler, _) = Build(enabled: true, out _);
        var result = await handler.HandleAsync(new ConsultNowCommand(Guid.NewGuid(), Actor), TestContext.Current.CancellationToken);
        result.Status.Should().Be(ConsultNowStatus.NotFound);
    }

    private static (ConsultNowHandler Handler, FakeRuntConfirmationStore Store) Build(bool enabled, out StubClient client)
    {
        var store = new FakeRuntConfirmationStore();
        client = new StubClient();
        var runner = new RuntConfirmationRunner(
            new StubSettings(new RuntConfirmationSettings { Enabled = enabled, MaxAttempts = 10 }),
            store, client, new RuntConfirmationRunnerOptions(), NullLogger<RuntConfirmationRunner>.Instance);
        return (new ConsultNowHandler(store, runner), store);
    }

    private static RuntConfirmationCandidate Candidate(string type) =>
        new(Guid.CreateVersion7(), Guid.NewGuid(), "FLT-9", type, ProcedureFamily.Otros, null, "QZU024",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), null, DateTimeOffset.UtcNow.AddDays(-10),
            null, new RuntDocument("NIT", "890903938"), null, null, 0, null, null);

    private sealed class StubSettings(RuntConfirmationSettings s) : IRuntConfirmationSettingsRepository
    {
        public Task<RuntConfirmationSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(s);
        public Task SaveAsync(RuntConfirmationSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubClient : IRuntVehicleRawClient
    {
        public RuntRawQueryResult Result { get; set; } = RuntRawQueryResult.Error("sin respuesta");
        public Task<RuntRawQueryResult> ConsultAsync(string providerKey, RuntRawQuery query, CancellationToken ct = default) => Task.FromResult(Result);
    }
}
