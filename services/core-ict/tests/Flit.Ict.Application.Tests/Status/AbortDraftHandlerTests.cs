using Flit.Ict.Application.Status;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Status;

public sealed class AbortDraftHandlerTests
{
    private readonly IPreTramiteRepository _repository = Substitute.For<IPreTramiteRepository>();
    private readonly IProcedureDraftClient _draftClient = Substitute.For<IProcedureDraftClient>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AbortDraftHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
    }

    private AbortDraftHandler CreateHandler() => new(_repository, _draftClient, _tenant);

    private ExternalIntegrationMaster Master(short processStatusId = 2, Guid? procedureInstanceId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        ProcessStatusId = processStatusId,
        ProcedureInstanceId = procedureInstanceId,
        ManagerUser = "gestor",
        ManagerMail = "gestor@demo.co",
        CompanyManagerDocument = "901698038",
    };

    [Fact]
    public async Task NonMaterialized_abort_marks_and_notifies_gestor()
    {
        var master = Master(processStatusId: 2, procedureInstanceId: null);
        _repository.FindByManagerIdTransactionAsync("TX1", _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (ok, error) = await CreateHandler().HandleAsync("TX1", "duplicado en origen", Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        await _repository.Received(1).MarkAbortedAsync(
            master.Id, _tenantId, "duplicado en origen",
            master.ManagerUser, master.ManagerMail, master.CompanyManagerDocument, Arg.Any<CancellationToken>());
        // El pre-trámite no materializó: el webhook al gestor se encola aquí (no hay Plano C).
        await _repository.Received(1).EnqueueAbortWebhookAsync(
            master.Id, _tenantId, "duplicado en origen", Arg.Any<CancellationToken>());
        // No debe tocar core-api: no hay borrador que anular.
        await _draftClient.DidNotReceive().AbortDraftAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Materialized_abort_delegates_to_core_api_and_does_not_enqueue_webhook()
    {
        var pi = Guid.NewGuid();
        var master = Master(processStatusId: 5, procedureInstanceId: pi);
        _repository.FindByManagerIdTransactionAsync("TX2", _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        _draftClient.AbortDraftAsync(
                _tenantId, pi, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new DraftActionResult("anulado", null));

        var (ok, error) = await CreateHandler().HandleAsync("TX2", "solicitud del cliente", Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        await _draftClient.Received(1).AbortDraftAsync(
            _tenantId, pi, "solicitud del cliente",
            master.ManagerUser, master.ManagerMail, master.CompanyManagerDocument, Arg.Any<CancellationToken>());
        await _repository.Received(1).MarkAbortedAsync(
            master.Id, _tenantId, "solicitud del cliente",
            master.ManagerUser, master.ManagerMail, master.CompanyManagerDocument, Arg.Any<CancellationToken>());
        // El materializado se notifica por el Plano C (core-api → callback), NO aquí: evita doble webhook.
        await _repository.DidNotReceive().EnqueueAbortWebhookAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonMaterialized_already_aborted_returns_estado_final_without_side_effects()
    {
        var master = Master(processStatusId: 6, procedureInstanceId: null);
        _repository.FindByManagerIdTransactionAsync("TX3", _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (ok, error) = await CreateHandler().HandleAsync("TX3", null, Ct);

        ok.Should().BeFalse();
        error.Should().Be("estado_final");
        await _repository.DidNotReceive().MarkAbortedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().EnqueueAbortWebhookAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_pretramite_returns_not_found()
    {
        _repository.FindByManagerIdTransactionAsync("TX4", _tenantId, Arg.Any<CancellationToken>())
            .Returns((ExternalIntegrationMaster?)null);

        var (ok, error) = await CreateHandler().HandleAsync("TX4", null, Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_found");
    }
}
