using Flit.Admin.Application.Ict;
using Flit.Admin.Domain.Ict;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Ict;

/// <summary>
/// Uso de ejemplo:
/// var handler = new GetIctJobCatalogHandler(repo);
/// var items = await handler.HandleAsync();
/// </summary>
public sealed class GetIctJobCatalogHandlerTests
{
    [Fact]
    public async Task Catalogo_incluye_los_seis_jobs_con_owner_core_ict()
    {
        var repo = Substitute.For<IIctJobRunRepository>();
        repo.GetLatestByJobAsync(Arg.Any<CancellationToken>()).Returns([]);

        var items = await new GetIctJobCatalogHandler(repo).HandleAsync(TestContext.Current.CancellationToken);

        items.Should().HaveCount(6);
        items.Select(i => i.DisplayName).Should().Contain(
        [
            "BusinessValidation",
            "ExternalValidation",
            "Orchestrator",
            "SendToCoreApi",
            "WebhookNotification",
            "Retention",
        ]);
        items.Should().OnlyContain(i => i.Owner == IctJobCatalog.Owner);
        items.Should().OnlyContain(i => i.Types.Count > 0);
    }

    [Fact]
    public async Task Orchestrator_es_mixto_y_aclara_runt_vs_confirmacion()
    {
        var repo = Substitute.For<IIctJobRunRepository>();
        repo.GetLatestByJobAsync(Arg.Any<CancellationToken>()).Returns([]);

        var items = await new GetIctJobCatalogHandler(repo).HandleAsync(TestContext.Current.CancellationToken);
        var orch = items.Single(i => i.Key == "orchestrator");

        orch.Types.Should().Contain(IctJobWorkTypes.Bd);
        orch.Types.Should().Contain(IctJobWorkTypes.EndpointInterno);
        orch.Types.Should().Contain(IctJobWorkTypes.EndpointExterno);
        orch.Notes.Should().Contain("RUNT");
        orch.Notes.Should().Contain("confirmacion-runt");
        orch.Notes.Should().Contain("No es Lambda");
    }

    [Fact]
    public async Task Pipeline_adjunta_ultimo_run_sin_error_message_y_retention_queda_sin_bitacora()
    {
        var repo = Substitute.For<IIctJobRunRepository>();
        var started = DateTimeOffset.Parse("2026-09-11T15:00:00Z");
        repo.GetLatestByJobAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new IctJobRunSummary("orchestrator", started, 1200, "ok"),
            new IctJobRunSummary("retention", started, 90, "ok"),
        ]);

        var items = await new GetIctJobCatalogHandler(repo).HandleAsync(TestContext.Current.CancellationToken);

        var orch = items.Single(i => i.Key == "orchestrator");
        orch.LastRun.Should().NotBeNull();
        orch.LastRun!.Outcome.Should().Be("ok");
        orch.LastRun.DurationMs.Should().Be(1200);
        orch.LastRun.StartedAt.Should().Be(started);

        var retention = items.Single(i => i.Key == "retention");
        retention.HasPipelineRuns.Should().BeFalse();
        retention.LastRun.Should().BeNull();

        typeof(IctJobLastRunView).GetProperties().Select(p => p.Name)
            .Should().NotContain("ErrorMessage");
        typeof(IctJobCatalogItemView).GetProperties().Select(p => p.Name)
            .Should().NotContain("ErrorMessage");
    }
}

/// <summary>
/// Uso de ejemplo:
/// var result = await new GetIctJobRunsHandler(repo).HandleAsync("orchestrator", 10);
/// </summary>
public sealed class GetIctJobRunsHandlerTests
{
    [Fact]
    public async Task Job_desconocido_no_existe()
    {
        var repo = Substitute.For<IIctJobRunRepository>();
        var result = await new GetIctJobRunsHandler(repo)
            .HandleAsync("no-existe", 10, TestContext.Current.CancellationToken);

        result.Exists.Should().BeFalse();
        await repo.DidNotReceive().ListRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retention_devuelve_lista_vacia_sin_consultar_job_runs()
    {
        var repo = Substitute.For<IIctJobRunRepository>();
        var result = await new GetIctJobRunsHandler(repo)
            .HandleAsync("retention", 10, TestContext.Current.CancellationToken);

        result.Exists.Should().BeTrue();
        result.Items.Should().BeEmpty();
        await repo.DidNotReceive().ListRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Take_se_acota_y_el_listado_no_expone_error_message()
    {
        var repo = Substitute.For<IIctJobRunRepository>();
        repo.ListRecentAsync("orchestrator", 50, Arg.Any<CancellationToken>())
            .Returns([new IctJobRunSummary("orchestrator", DateTimeOffset.UtcNow, 10, "error")]);

        var result = await new GetIctJobRunsHandler(repo)
            .HandleAsync("orchestrator", 999, TestContext.Current.CancellationToken);

        result.Exists.Should().BeTrue();
        result.Items.Should().HaveCount(1);
        result.Items[0].Outcome.Should().Be("error");
        typeof(IctJobRunListItemView).GetProperties().Select(p => p.Name)
            .Should().NotContain("ErrorMessage");
        await repo.Received(1).ListRecentAsync("orchestrator", 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Take_nulo_usa_default_20()
    {
        var repo = Substitute.For<IIctJobRunRepository>();
        repo.ListRecentAsync("orchestrator", 20, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new GetIctJobRunsHandler(repo)
            .HandleAsync("orchestrator", null, TestContext.Current.CancellationToken);

        result.Exists.Should().BeTrue();
        await repo.Received(1).ListRecentAsync("orchestrator", 20, Arg.Any<CancellationToken>());
    }
}
