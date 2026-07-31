using Flit.Ict.Application.Attachments;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Attachments;

public sealed class CloseDocumentHandlerTests
{
    private readonly IPreTramiteRepository _repository = Substitute.For<IPreTramiteRepository>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CloseDocumentHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
    }

    private CloseDocumentHandler CreateHandler() => new(_repository, _tenant);

    private ExternalIntegrationMaster Master(bool closed = false, Guid? procedureInstanceId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        ProcessStatusId = 2,
        ClosedDocument = closed,
        ProcedureInstanceId = procedureInstanceId,
        ManagerUser = "gestor",
        ManagerMail = "gestor@demo.co",
        CompanyManagerDocument = "900123456-1",
    };

    [Fact]
    public async Task Sin_tenant_devuelve_unauthenticated_y_no_persiste()
    {
        _tenant.TenantId.Returns((Guid?)null);

        var (ok, error) = await CreateHandler().HandleAsync("TX1", Ct);

        ok.Should().BeFalse();
        error.Should().Be("unauthenticated");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Id_en_blanco_devuelve_invalid_request()
    {
        var (ok, error) = await CreateHandler().HandleAsync("   ", Ct);

        ok.Should().BeFalse();
        error.Should().Be("invalid_request");
    }

    [Fact]
    public async Task Pretramite_inexistente_devuelve_not_found()
    {
        _repository.FindByManagerIdTransactionAsync("TX1", _tenantId, Arg.Any<CancellationToken>())
            .Returns((ExternalIntegrationMaster?)null);

        var (ok, error) = await CreateHandler().HandleAsync("TX1", Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_found");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ya_materializado_devuelve_already_materialized()
    {
        var master = Master(procedureInstanceId: Guid.NewGuid());
        _repository.FindByManagerIdTransactionAsync("TX1", _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (ok, error) = await CreateHandler().HandleAsync("TX1", Ct);

        ok.Should().BeFalse();
        error.Should().Be("already_materialized");
        master.ClosedDocument.Should().BeFalse();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cierre_de_documento_abierto_marca_closed_persiste_y_registra_evento()
    {
        var master = Master(closed: false);
        _repository.FindByManagerIdTransactionAsync("TX1", _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (ok, error) = await CreateHandler().HandleAsync("TX1", Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        master.ClosedDocument.Should().BeTrue();
        await _repository.Received(1).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordTimelineEventAsync(
            master.Id, _tenantId, "documento_cerrado", "ok", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cerrar_dos_veces_es_idempotente_no_reescribe_ni_reemite_evento()
    {
        var master = Master(closed: true);
        _repository.FindByManagerIdTransactionAsync("TX1", _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (ok, error) = await CreateHandler().HandleAsync("TX1", Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordTimelineEventAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
