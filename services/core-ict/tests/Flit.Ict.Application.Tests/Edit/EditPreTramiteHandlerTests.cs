using Flit.Ict.Application.Edit;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Edit;

public sealed class EditPreTramiteHandlerTests
{
    private readonly IPreTramiteRepository _repository = Substitute.For<IPreTramiteRepository>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public EditPreTramiteHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _tenant.IntegrationClientId.Returns(Guid.NewGuid());
    }

    private readonly IProcedureDraftClient _draftClient = Substitute.For<IProcedureDraftClient>();

    private EditPreTramiteHandler CreateHandler() => new(_repository, _tenant, _draftClient);

    private ExternalIntegrationMaster Editable(Guid id, long rowVersion = 3) => new()
    {
        Id = id,
        TenantId = _tenantId,
        RowVersion = rowVersion,
        BusinessValidation = 2,
        ExternalValidation = 0,
        ProcessStatusId = 2,
        TrafficSecretaryCode = "5001000",
    };

    [Fact]
    public async Task Validation_affecting_change_resets_business_validation()
    {
        var id = Guid.NewGuid();
        var master = Editable(id, rowVersion: 3);
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, TrafficSecretaryCode: "11001000"), Ct);

        error.Should().BeNull();
        result!.ValidationReset.Should().BeTrue();
        master.BusinessValidation.Should().Be(0);
        master.ProcessStatusId.Should().Be(1);
        await _repository.Received(1).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stale_row_version_returns_stale()
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Editable(id, rowVersion: 4));

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, DeliveryAddress: "Calle 1"), Ct);

        result.Should().BeNull();
        error.Should().Be("stale");
    }

    [Fact]
    public async Task Materialized_pretramite_cannot_be_edited()
    {
        var id = Guid.NewGuid();
        var master = Editable(id);
        master.ProcedureInstanceId = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, DeliveryAddress: "Calle 1"), Ct);

        result.Should().BeNull();
        error.Should().Be("already_materialized");
    }

    [Fact]
    public async Task Missing_pretramite_returns_not_found()
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns((ExternalIntegrationMaster?)null);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 1), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    // ===== Bug #13304: precio de venta editable mientras el trámite siga en borrador =====

    private ExternalIntegrationMaster Materialized(Guid id, Guid instanceId, long rowVersion = 3)
    {
        var master = Editable(id, rowVersion);
        master.ProcedureInstanceId = instanceId;
        master.ProcessStatusId = 5;
        master.BusinessValidation = 2;
        master.ExternalValidation = 2;
        master.SellingPrice = 17001000m;
        return master;
    }

    [Fact]
    public async Task Materializado_solo_precio_en_borrador_propaga_a_core_api_y_no_resetea_validaciones()
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var master = Materialized(id, instanceId);
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        _draftClient.UpdateCommercialAsync(_tenantId, instanceId, id, 18500000m, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m), Ct);

        error.Should().BeNull();
        result!.ValidationReset.Should().BeFalse();
        master.SellingPrice.Should().Be(18500000m);
        master.BusinessValidation.Should().Be(2);
        master.ExternalValidation.Should().Be(2);
        master.ProcessStatusId.Should().Be(5);
        await _draftClient.Received(1).UpdateCommercialAsync(_tenantId, instanceId, id, 18500000m, Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordTimelineEventAsync(
            id, _tenantId, "editado", "ok",
            Arg.Is<string>(d => d.Contains("\"selling_price\"") && d.Contains("\"validation_affecting\":false")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Materializado_mismo_precio_igual_se_envia_para_reponer_el_comercial()
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Materialized(id, instanceId));
        _draftClient.UpdateCommercialAsync(_tenantId, instanceId, id, 17001000m, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));

        var (_, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 17001000m), Ct);

        error.Should().BeNull();
        await _draftClient.Received(1).UpdateCommercialAsync(_tenantId, instanceId, id, 17001000m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Materializado_fuera_de_borrador_devuelve_not_draft_sin_tocar_el_master()
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var master = Materialized(id, instanceId);
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master);
        _draftClient.UpdateCommercialAsync(_tenantId, instanceId, id, 18500000m, Arg.Any<CancellationToken>())
            .Returns((false, "not_draft"));

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m), Ct);

        result.Should().BeNull();
        error.Should().Be(EditPreTramiteHandler.NotDraft);
        master.SellingPrice.Should().Be(17001000m);
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("grpc_unavailable", "core_api_unavailable")]
    [InlineData(null, "core_api_unavailable")]
    [InlineData("invalid_valor_venta", "invalid_valor_venta")]
    public async Task Materializado_otros_errores_de_core_api_se_propagan(string? coreApiError, string esperado)
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Materialized(id, instanceId));
        _draftClient.UpdateCommercialAsync(_tenantId, instanceId, id, 18500000m, Arg.Any<CancellationToken>())
            .Returns((false, coreApiError));

        var (_, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m), Ct);

        error.Should().Be(esperado);
    }

    [Fact]
    public async Task Materializado_con_otro_campo_ademas_del_precio_sigue_already_materialized()
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Materialized(id, Guid.NewGuid()));

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m, SellingDate: "01-01-2025"), Ct);

        result.Should().BeNull();
        error.Should().Be("already_materialized");
        await _draftClient.DidNotReceiveWithAnyArgs().UpdateCommercialAsync(default, default, default, default, Ct);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.123)]
    public async Task Materializado_precio_invalido_se_rechaza_sin_llamar_a_core_api(double price)
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Materialized(id, Guid.NewGuid()));

        var (_, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: (decimal)price), Ct);

        error.Should().Be(EditPreTramiteHandler.InvalidSellingPrice);
        await _draftClient.DidNotReceiveWithAnyArgs().UpdateCommercialAsync(default, default, default, default, Ct);
    }

    [Fact]
    public async Task Materializado_row_version_vieja_devuelve_stale_sin_llamar_a_core_api()
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Materialized(id, Guid.NewGuid(), rowVersion: 4));

        var (_, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m), Ct);

        error.Should().Be("stale");
        await _draftClient.DidNotReceiveWithAnyArgs().UpdateCommercialAsync(default, default, default, default, Ct);
    }

    [Fact]
    public async Task Antes_de_materializar_el_precio_sigue_reseteando_validaciones_sin_llamar_a_core_api()
    {
        var id = Guid.NewGuid();
        var master = Editable(id);
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m), Ct);

        error.Should().BeNull();
        result!.ValidationReset.Should().BeTrue();
        master.BusinessValidation.Should().Be(0);
        await _draftClient.DidNotReceiveWithAnyArgs().UpdateCommercialAsync(default, default, default, default, Ct);
    }
}
