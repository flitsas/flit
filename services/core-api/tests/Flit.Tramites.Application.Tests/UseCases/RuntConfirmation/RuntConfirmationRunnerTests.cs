using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.RuntConfirmation;

/// <summary>HU #12309 — la corrida: interruptor, universo, consulta por familia, crudo antes de evaluar, marcas, errores y log.</summary>
public sealed class RuntConfirmationRunnerTests
{
    private static readonly Guid Tenant = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Radicado = new(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);

    private const string CambioColorOk = """
        {"ok":true,"data":{"vehiculo":{"placa":"QZU024","estadoAutomotor":"ACTIVO","color":"ROSADO","mostrarSolicitudes":"SI"},
        "solicitudes":[{"noSolicitud":"302345557","fechaSolicitud":"2026-09-08T15:49:49.000-05:00","estado":"AUTORIZADA","tramitesRealizados":"TRÁMITE CAMBIO COLOR, ","entidad":"STRIA TTEyTTO BELLO"}]}}
        """;

    private const string SinSolicitudNueva = """
        {"ok":true,"data":{"vehiculo":{"placa":"QZU024","estadoAutomotor":"ACTIVO","mostrarSolicitudes":"SI"},
        "solicitudes":[{"noSolicitud":"1","fechaSolicitud":"2025-01-01T00:00:00.000-05:00","estado":"AUTORIZADA","tramitesRealizados":"TRÁMITE CAMBIO COLOR, ","entidad":"X"}]}}
        """;

    private const string Rechazada = """
        {"ok":true,"data":{"vehiculo":{"placa":"QZU024","mostrarSolicitudes":"SI"},
        "solicitudes":[{"noSolicitud":"9","fechaSolicitud":"2026-09-05T00:00:00.000-05:00","estado":"RECHAZADA","tramitesRealizados":"TRÁMITE CAMBIO COLOR, ","entidad":"X"}]}}
        """;

    private const string TraspasoOk = """
        {"ok":true,"data":{"vehiculo":{"placa":"PUO271","mostrarSolicitudes":"SI"},
        "solicitudes":[{"noSolicitud":"295751939","fechaSolicitud":"2026-09-11T00:00:00.000-05:00","estado":"AUTORIZADA","tramitesRealizados":"TRÁMITE TRASPASO, ","entidad":"STRIA TTEyTTO ENVIGADO"}]}}
        """;

    private const string MatriculaOk = """
        {"ok":true,"data":{"vehiculo":{"placa":"QZU024","estadoAutomotor":"ACTIVO","mostrarSolicitudes":"SI"},
        "solicitudes":[{"noSolicitud":"301644309","fechaSolicitud":"2026-09-02T10:53:48.000-05:00","estado":"AUTORIZADA","tramitesRealizados":"TRÁMITE MATRÍCULA INICIAL, ","entidad":"STRIA TTEyTTO BELLO"}]}}
        """;

    // ── AC1: interruptor ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Apagada_NoConsultaNada_YDejaFilaSkippedDisabled()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = false });
        store.Candidates.Add(Otros("CAMBIO_COLOR"));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.SkippedReason.Should().Be("disabled");
        run.FinishedAt.Should().NotBeNull();
        client.Calls.Should().BeEmpty();
        store.Attempts.Should().BeEmpty();
        store.Runs.Should().ContainSingle();
    }

    [Fact]
    public async Task AC1_Manual_ConsultaAunqueEsteApagada()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = false });
        var c = Otros("CAMBIO_COLOR");
        store.Candidates.Add(c);
        client.Respond(_ => new(RuntRawOutcome.Found, CambioColorOk, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Manual, [c.InstanceId], RequestedBy: Guid.NewGuid()), TestContext.Current.CancellationToken);

        run.SkippedReason.Should().BeNull();
        run.Consulted.Should().Be(1);
        store.Attempts.Single().RequestedBy.Should().NotBeNull();
    }

    // ── AC2: universo ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_ElUniversoExcluyeConfirmadosTopeNoVerificablesYTiposFuera()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true, MaxAttempts = 3 });
        var entra = Otros("CAMBIO_COLOR");
        store.Candidates.Add(entra);
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { RuntConfirmedAt = DateTimeOffset.UtcNow });
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { RuntAttempts = 3 });
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { RuntFlag = "no_verificable" });
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { RuntFlag = "tope" });
        store.Candidates.Add(Otros("REMATRICULA") with { Family = ProcedureFamily.Matriculas });
        store.Candidates.Add(Otros("CANCELACION_MATRICULA") with { Family = ProcedureFamily.Matriculas });
        client.Respond(_ => new(RuntRawOutcome.Found, CambioColorOk, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.Consulted.Should().Be(1);
        store.Attempts.Single().ProcedureInstanceId.Should().Be(entra.InstanceId);
    }

    [Fact]
    public async Task AC2_DiasDeGracia_DejanFueraAlReciénAprobado()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true, GraceDays = 2 });
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { ApprovedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { ApprovedAt = DateTimeOffset.UtcNow.AddDays(-3) });
        client.Respond(_ => new(RuntRawOutcome.Found, CambioColorOk, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.Consulted.Should().Be(1);
    }

    // ── AC3: consulta según familia ───────────────────────────────────────────────────

    [Fact]
    public async Task AC3_MatriculaPorVin_TraspasoDosPorPlaca_OtrosUnaPorPlacaConPropietario()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true });
        store.Candidates.Add(Matricula());
        store.Candidates.Add(Traspaso());
        store.Candidates.Add(Otros("CAMBIO_COLOR"));
        client.Respond(q => q.Vin is not null
            ? new(RuntRawOutcome.Found, MatriculaOk, null)
            : q.Document!.Number == "111" // vendedor
                ? new(RuntRawOutcome.NotFound, null, "no es propietario")
                : new(RuntRawOutcome.Found, q.Plate == "PUO271" ? TraspasoOk : CambioColorOk, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.Consulted.Should().Be(3);
        run.ProviderCalls.Should().Be(4, "matrícula 1 + traspaso 2 + otros 1");
        client.Calls.Should().ContainSingle(q => q.Vin == "XLRAEL2G0PL523530");
        client.Calls.Where(q => q.Plate == "PUO271").Select(q => q.Document!.Number).Should().BeEquivalentTo(["111", "222"]);
        client.Calls.Should().ContainSingle(q => q.Plate == "QZU024" && q.Document!.Number == "890903938");

        store.Attempts.Select(a => a.QueryKind).Should().BeEquivalentTo(["vin", "plate_pair", "plate"]);
        var traspaso = store.Attempts.Single(a => a.QueryKind == "plate_pair");
        traspaso.Verdict.Should().Be("confirmed");
        traspaso.ReasonText.Should().Contain("propiedad transferida");
        traspaso.SellerRawPayloadId.Should().NotBeNull("la consulta del vendedor, aunque no encontró, deja crudo");
        run.Confirmed.Should().Be(3);
    }

    [Fact]
    public async Task AC3_TraspasoSinDocumentoDelVendedor_EsNoVerificable_SinLlamarAlProveedor()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true });
        store.Candidates.Add(Traspaso() with { Seller = null });

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        client.Calls.Should().BeEmpty();
        run.Unverifiable.Should().Be(1);
        store.Attempts.Single().ReasonText.Should().Contain("vendedor y comprador");
        store.Candidates.Single().RuntFlag.Should().Be("no_verificable");
    }

    // ── AC4: crudo antes de evaluar y campos del intento ─────────────────────────────

    [Fact]
    public async Task AC4_ElCrudoQuedaGuardado_YElIntentoLoReferenciaConTodosSusCampos()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true, ProviderKey = "verifik" });
        store.Candidates.Add(Otros("CAMBIO_COLOR"));
        client.Respond(_ => new(RuntRawOutcome.Found, CambioColorOk, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        var a = store.Attempts.Single();
        a.RawPayloadId.Should().NotBeNull();
        store.Payloads[a.RawPayloadId!.Value].Should().Be(CambioColorOk);
        a.AttemptNo.Should().Be(1);
        a.QueriedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        a.ProviderKey.Should().Be("verifik");
        a.Verdict.Should().Be("confirmed");
        a.ReasonText.Should().Contain("302345557");
        a.RuleVersion.Should().Be(RuntConfirmationRules.Version);
        a.RunId.Should().Be(run.Id);
    }

    // ── AC5: confirmado ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_Confirmado_MarcaElTramite_YLaCorridaSiguienteNoLoConsulta()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true });
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { RuntFlag = "discrepancia" });
        client.Respond(_ => new(RuntRawOutcome.Found, CambioColorOk, null));

        await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);
        var c = store.Candidates.Single();
        c.RuntConfirmedAt.Should().NotBeNull();
        c.RuntFlag.Should().BeNull("un Confirmado limpia la marca previa");

        var second = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);
        second.Consulted.Should().Be(0);
        client.Calls.Should().HaveCount(1);
    }

    // ── AC6: pendiente, discrepancia y tope ──────────────────────────────────────────

    [Fact]
    public async Task AC6_Pendiente_IncrementaIntentos_DiscrepanciaAlLlegarAlUmbral_TopeAlLlegarAlMaximo()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true, DiscrepancyAfterRuns = 2, MaxAttempts = 3 });
        store.Candidates.Add(Otros("CAMBIO_COLOR"));
        client.Respond(_ => new(RuntRawOutcome.Found, SinSolicitudNueva, null));

        await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);
        store.Candidates.Single().RuntAttempts.Should().Be(1);
        store.Candidates.Single().RuntFlag.Should().BeNull();

        await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);
        store.Candidates.Single().RuntAttempts.Should().Be(2);
        store.Candidates.Single().RuntFlag.Should().Be("discrepancia");

        await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);
        store.Candidates.Single().RuntAttempts.Should().Be(3);
        store.Candidates.Single().RuntFlag.Should().Be("tope");
        store.Attempts.Last().FlagApplied.Should().Be("tope");

        var fourth = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);
        fourth.Consulted.Should().Be(0, "al tope sale del universo");
    }

    [Fact]
    public async Task AC6_Rechazada_MarcaDiscrepanciaEnElPrimerIntento()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true, DiscrepancyAfterRuns = 5 });
        store.Candidates.Add(Otros("CAMBIO_COLOR"));
        client.Respond(_ => new(RuntRawOutcome.Found, Rechazada, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.Discrepancies.Should().Be(1);
        store.Candidates.Single().RuntFlag.Should().Be("discrepancia");
        store.Candidates.Single().RuntAttempts.Should().Be(1);
    }

    // ── AC7: error del proveedor no es veredicto ─────────────────────────────────────

    [Fact]
    public async Task AC7_ErrorDelProveedor_DejaIntentoError_SinIncrementarNiMarcar_YVuelveAEntrar()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true });
        store.Candidates.Add(Otros("CAMBIO_COLOR"));
        client.Respond(_ => RuntRawQueryResult.Error("timeout"));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.Errors.Should().Be(1);
        var a = store.Attempts.Single();
        a.Verdict.Should().Be("error");
        a.ReasonText.Should().Contain("timeout");
        var c = store.Candidates.Single();
        c.RuntAttempts.Should().Be(0);
        c.RuntFlag.Should().BeNull();
        c.RuntConfirmedAt.Should().BeNull();

        var second = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);
        second.Consulted.Should().Be(1, "entra en la corrida siguiente");
    }

    [Fact]
    public async Task AC7_Traspaso_ConUnaDeLasDosEnError_EsError()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true });
        store.Candidates.Add(Traspaso());
        client.Respond(q => q.Document!.Number == "111" ? RuntRawQueryResult.Error("504") : new(RuntRawOutcome.Found, TraspasoOk, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.Errors.Should().Be(1);
        run.ProviderCalls.Should().Be(2);
        store.Candidates.Single().RuntAttempts.Should().Be(0);
    }

    // ── AC9: log de cierre ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC9_LaFilaDeLaCorridaCuentaVeredictosYLlamadas()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true, DiscrepancyAfterRuns = 5 });
        for (var i = 0; i < 3; i++) store.Candidates.Add(Otros("CAMBIO_COLOR"));
        store.Candidates.Add(Traspaso());
        store.Candidates.Add(Otros("CAMBIO_COLOR") with { InstanceId = Guid.Parse("cccccccc-0000-0000-0000-000000000009") });
        client.Respond(q =>
            q.Plate == "PUO271" ? (q.Document!.Number == "111" ? new(RuntRawOutcome.NotFound, null, null) : new(RuntRawOutcome.Found, TraspasoOk, null))
            : q.Plate == "QZU024" && q.Document!.Number == "890903938" ? new(RuntRawOutcome.Found, SinSolicitudNueva, null)
            : RuntRawQueryResult.Error("x"));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.FinishedAt.Should().NotBeNull();
        run.ProviderKey.Should().Be("kyverum_runt");
        run.Consulted.Should().Be(5);
        run.ProviderCalls.Should().Be(6, "4 sencillas + 1 traspaso doble");
        run.Confirmed.Should().Be(1);
        run.Pending.Should().Be(4);
        (run.Confirmed + run.Pending + run.Discrepancies + run.Unverifiable + run.Errors).Should().Be(run.Consulted);
    }

    // ── AC10: una sola corrida a la vez ───────────────────────────────────────────────

    [Fact]
    public async Task AC10_ConCorridaEnCurso_LaProgramadaSeSaltaConAlreadyRunning()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true });
        store.RunInProgress = true;
        store.Candidates.Add(Otros("CAMBIO_COLOR"));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.SkippedReason.Should().Be("already_running");
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AC10_LasLlamadasRespetanLaConcurrenciaConfigurada()
    {
        var (runner, store, client) = Build(new RuntConfirmationSettings { Enabled = true }, maxConcurrency: 2);
        for (var i = 0; i < 20; i++) store.Candidates.Add(Otros("CAMBIO_COLOR"));
        client.Delay = TimeSpan.FromMilliseconds(15);
        client.Respond(_ => new(RuntRawOutcome.Found, CambioColorOk, null));

        var run = await runner.RunAsync(new(RuntConfirmationRunTriggers.Scheduled), TestContext.Current.CancellationToken);

        run.Consulted.Should().Be(20);
        client.MaxObservedConcurrency.Should().BeLessThanOrEqualTo(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static (RuntConfirmationRunner Runner, FakeRuntConfirmationStore Store, FakeRawClient Client) Build(
        RuntConfirmationSettings settings, int maxConcurrency = 4)
    {
        var store = new FakeRuntConfirmationStore();
        var client = new FakeRawClient();
        var runner = new RuntConfirmationRunner(
            new FakeSettingsRepo(settings), store, client,
            new RuntConfirmationRunnerOptions { MaxConcurrency = maxConcurrency },
            NullLogger<RuntConfirmationRunner>.Instance);
        return (runner, store, client);
    }

    private static RuntConfirmationCandidate Otros(string type) =>
        new(Guid.CreateVersion7(), Tenant, "FLT-1", type, ProcedureFamily.Otros, null, "QZU024", Radicado, Radicado.AddDays(1), Radicado.AddDays(-1),
            "STRIA TTEyTTO BELLO", new RuntDocument("NIT", "890903938"), null, null, 0, null, null);

    private static RuntConfirmationCandidate Matricula() =>
        new(Guid.CreateVersion7(), Tenant, "FLT-2", "MATRICULA_NUEVA", ProcedureFamily.Matriculas, "XLRAEL2G0PL523530", null, Radicado, Radicado.AddDays(1), Radicado.AddDays(-1),
            "STRIA TTEyTTO BELLO", null, null, new RuntDocument("NIT", "890903938"), 0, null, null);

    private static RuntConfirmationCandidate Traspaso() =>
        new(Guid.CreateVersion7(), Tenant, "FLT-3", "TRASPASO_STANDARD", ProcedureFamily.Traspaso, null, "PUO271", Radicado, Radicado.AddDays(1), Radicado.AddDays(-1),
            "STRIA TTEyTTO ENVIGADO", null, new RuntDocument("CC", "111"), new RuntDocument("CC", "222"), 0, null, null);

    private sealed class FakeSettingsRepo(RuntConfirmationSettings settings) : IRuntConfirmationSettingsRepository
    {
        public Task<RuntConfirmationSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SaveAsync(RuntConfirmationSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeRawClient : IRuntVehicleRawClient
    {
        private Func<RuntRawQuery, RuntRawQueryResult> _respond = _ => RuntRawQueryResult.Error("sin respuesta configurada");
        private int _inFlight;

        public List<RuntRawQuery> Calls { get; } = [];
        public TimeSpan Delay { get; set; }
        public int MaxObservedConcurrency { get; private set; }

        public void Respond(Func<RuntRawQuery, RuntRawQueryResult> respond) => _respond = respond;

        public async Task<RuntRawQueryResult> ConsultAsync(string providerKey, RuntRawQuery query, CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            lock (Calls)
            {
                Calls.Add(query);
                MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, now);
            }

            try
            {
                if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
                return _respond(query);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }
}
