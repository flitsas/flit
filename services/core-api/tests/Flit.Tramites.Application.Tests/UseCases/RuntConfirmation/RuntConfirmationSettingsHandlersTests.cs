using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.RuntConfirmation;

/// <summary>HU #12277 — lectura con defaults (AC1), validación sin efectos (AC2) y auditoría campo a campo (AC3).</summary>
public sealed class RuntConfirmationSettingsHandlersTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Get_SinFila_DevuelveLosDefaults()
    {
        var repo = new FakeRepo(null);
        var dto = await new GetRuntConfirmationSettingsHandler(repo).HandleAsync(TestContext.Current.CancellationToken);

        dto.Enabled.Should().BeFalse();
        dto.RunAtLocal.Should().Be("02:00");
        dto.ProviderKey.Should().Be("kyverum_runt");
        dto.GraceDays.Should().Be(0);
        dto.DiscrepancyAfterRuns.Should().Be(3);
        dto.MaxAttempts.Should().Be(10);
        repo.Saved.Should().BeNull("leer no persiste la fila");
    }

    [Theory]
    [InlineData(-1, 3, 10, "kyverum_runt", "graceDays")]
    [InlineData(0, 0, 10, "kyverum_runt", "discrepancyAfterRuns")]
    [InlineData(0, 5, 3, "kyverum_runt", "maxAttempts")]
    [InlineData(0, 3, 10, "otro", "providerKey")]
    public async Task Put_ConValorFueraDeRango_IdentificaElCampo_YNoCambiaNada(int grace, int discrepancy, int max, string provider, string field)
    {
        var repo = new FakeRepo(new RuntConfirmationSettings { Enabled = true });
        var audit = new FakeAudit();

        var result = await new UpdateRuntConfirmationSettingsHandler(repo, audit).HandleAsync(
            new UpdateRuntConfirmationSettingsCommand(false, "02:00", provider, grace, discrepancy, max, Actor),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(field);
        repo.Saved.Should().BeNull();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Put_CambiaDosCampos_AuditaUnaEntradaPorCampoConAntesDespuesYActor()
    {
        var repo = new FakeRepo(new RuntConfirmationSettings { Enabled = true, ProviderKey = "kyverum_runt" });
        var audit = new FakeAudit();

        var result = await new UpdateRuntConfirmationSettingsHandler(repo, audit).HandleAsync(
            new UpdateRuntConfirmationSettingsCommand(false, "02:00", "verifik", 0, 3, 10, Actor),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Settings!.Enabled.Should().BeFalse();
        result.Settings.ProviderKey.Should().Be("verifik");
        result.Settings.UpdatedBy.Should().Be(Actor);
        result.Settings.UpdatedAt.Should().NotBeNull();

        audit.Entries.Should().HaveCount(2);
        audit.Entries.Should().Contain(e => e.Change.Field == "enabled" && e.Change.OldValue == "true" && e.Change.NewValue == "false" && e.Actor == Actor);
        audit.Entries.Should().Contain(e => e.Change.Field == "providerKey" && e.Change.OldValue == "kyverum_runt" && e.Change.NewValue == "verifik" && e.Actor == Actor);
        repo.Saved!.ProviderKey.Should().Be("verifik");
    }

    [Fact]
    public async Task Put_SinCambios_NoPersisteNiAudita()
    {
        var repo = new FakeRepo(new RuntConfirmationSettings());
        var audit = new FakeAudit();

        var result = await new UpdateRuntConfirmationSettingsHandler(repo, audit).HandleAsync(
            new UpdateRuntConfirmationSettingsCommand(false, "02:00", "kyverum_runt", 0, 3, 10, Actor),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Changes.Should().BeEmpty();
        repo.Saved.Should().BeNull();
        audit.Entries.Should().BeEmpty();
    }

    private sealed class FakeRepo(RuntConfirmationSettings? row) : IRuntConfirmationSettingsRepository
    {
        public RuntConfirmationSettings? Saved { get; private set; }

        public Task<RuntConfirmationSettings> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(row ?? RuntConfirmationSettings.Defaults());

        public Task SaveAsync(RuntConfirmationSettings settings, CancellationToken ct = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudit : IRuntConfirmationAuditWriter
    {
        public List<(RuntConfirmationSettingChange Change, Guid? Actor)> Entries { get; } = [];

        public Task WriteSettingChangeAsync(RuntConfirmationSettingChange change, Guid? actorUserId, CancellationToken ct = default)
        {
            Entries.Add((change, actorUserId));
            return Task.CompletedTask;
        }
    }
}
