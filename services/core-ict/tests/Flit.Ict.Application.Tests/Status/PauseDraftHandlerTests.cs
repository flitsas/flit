using Flit.Ict.Application.Status;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Status;

/// <summary>
/// HU #14668 §4 — PauseDraftHandler: tras delegar a core-api (gRPC PauseDraft),
/// actualiza master.ObservationWhenPaused y registra evento en la timeline ICT.
/// </summary>
public sealed class PauseDraftHandlerTests
{
    private readonly IPreTramiteRepository _repository = Substitute.For<IPreTramiteRepository>();
    private readonly IProcedureDraftClient _draftClient = Substitute.For<IProcedureDraftClient>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PauseDraftHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
    }

    private PauseDraftHandler CreateHandler() => new(_repository, _draftClient, _tenant);

    private ExternalIntegrationMaster MaterializedMaster(Guid? procedureInstanceId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        ProcessStatusId = 2,
        ProcedureInstanceId = procedureInstanceId ?? Guid.NewGuid(),
        ManagerUser = "gestor",
        ManagerMail = "gestor@demo.co",
        CompanyManagerDocument = "901698038",
    };

    private void SetupDraftClientSuccess(Guid instanceId, bool paused)
    {
        _draftClient.PauseDraftAsync(
                _tenantId, instanceId, paused, Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DraftActionResult("borrador", null));
    }

    // ── Éxito ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pause_withObservation_updatesObservationWhenPausedAndRecordsTimeline()
    {
        var master = MaterializedMaster();
        _repository.FindByManagerIdTransactionAsync("TX1", _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        SetupDraftClientSuccess(master.ProcedureInstanceId!.Value, paused: true);

        var (ok, error) = await CreateHandler().HandleAsync("TX1", paused: true, "pausa solicitada", Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();

        // HU #14668 §4 — ObservationWhenPaused actualizado
        master.ObservationWhenPaused.Should().Be("pausa solicitada");
        await _repository.Received(1).SaveAsync(_tenantId, Arg.Any<CancellationToken>());

        // Timeline: stage=pausado, outcome=ok, con detailJson de observation (ASCII para evitar escaping JSON)
        await _repository.Received(1).RecordTimelineEventAsync(
            master.Id, _tenantId, "pausado", "ok",
            Arg.Is<string?>(j => j != null && j.Contains("pausa solicitada")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resume_withNullObservation_doesNotSaveMasterAndRecordsTimelineWithoutDetail()
    {
        var master = MaterializedMaster();
        master.ObservationWhenPaused = "original";
        _repository.FindByManagerIdTransactionAsync("TX2", _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        SetupDraftClientSuccess(master.ProcedureInstanceId!.Value, paused: false);

        var (ok, error) = await CreateHandler().HandleAsync("TX2", paused: false, observation: null, Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();

        // null observation → keep → no SaveAsync del master
        master.ObservationWhenPaused.Should().Be("original"); // sin cambio
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // Timeline siempre se registra
        await _repository.Received(1).RecordTimelineEventAsync(
            master.Id, _tenantId, "reanudado", "ok",
            null, // sin detailJson cuando no hay observation
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resume_withObservation_updatesObservationWhenPausedAndRecordsTimeline()
    {
        var master = MaterializedMaster();
        master.ObservationWhenPaused = "original";
        _repository.FindByManagerIdTransactionAsync("TX3", _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        SetupDraftClientSuccess(master.ProcedureInstanceId!.Value, paused: false);

        var (ok, error) = await CreateHandler().HandleAsync("TX3", paused: false, "motivo reanudacion", Ct);

        ok.Should().BeTrue();
        master.ObservationWhenPaused.Should().Be("motivo reanudacion");
        await _repository.Received(1).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordTimelineEventAsync(
            master.Id, _tenantId, "reanudado", "ok",
            Arg.Is<string?>(j => j != null && j.Contains("motivo reanudacion")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_withEmptyObservation_clearsObservationWhenPausedAndRecordsTimeline()
    {
        var master = MaterializedMaster();
        master.ObservationWhenPaused = "original";
        _repository.FindByManagerIdTransactionAsync("TX4", _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        SetupDraftClientSuccess(master.ProcedureInstanceId!.Value, paused: true);

        // empty → effectiveObs = null → paused=true → SaveAsync (clears)
        var (ok, error) = await CreateHandler().HandleAsync("TX4", paused: true, observation: "   ", Ct);

        ok.Should().BeTrue();
        master.ObservationWhenPaused.Should().BeNull(); // empty → null
        await _repository.Received(1).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
    }

    // ── Errores de pre-condición ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_returns_error()
    {
        _tenant.TenantId.Returns((Guid?)null);

        var (ok, error) = await CreateHandler().HandleAsync("TX", paused: true, "obs", Ct);

        ok.Should().BeFalse();
        error.Should().Be("unauthenticated");
        await _draftClient.DidNotReceive().PauseDraftAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingMaster_returns_not_found()
    {
        _repository.FindByManagerIdTransactionAsync("TX", _tenantId, Arg.Any<CancellationToken>())
            .Returns((ExternalIntegrationMaster?)null);

        var (ok, error) = await CreateHandler().HandleAsync("TX", paused: true, "obs", Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task NotMaterialized_returns_not_materialized()
    {
        var master = new ExternalIntegrationMaster
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcessStatusId = 1,
            ProcedureInstanceId = null, // sin borrador
        };
        _repository.FindByManagerIdTransactionAsync("TX", _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (ok, error) = await CreateHandler().HandleAsync("TX", paused: true, "obs", Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_materialized");
        await _draftClient.DidNotReceive().PauseDraftAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DraftClientError_returns_error_without_updating_master()
    {
        var master = MaterializedMaster();
        master.ObservationWhenPaused = "original";
        _repository.FindByManagerIdTransactionAsync("TX", _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        _draftClient.PauseDraftAsync(
                _tenantId, master.ProcedureInstanceId!.Value, true, Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DraftActionResult(null, "not_in_draft"));

        var (ok, error) = await CreateHandler().HandleAsync("TX", paused: true, "obs", Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_in_draft");
        // No debe actualizar master ni timeline si core-api rechazó
        master.ObservationWhenPaused.Should().Be("original");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordTimelineEventAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
