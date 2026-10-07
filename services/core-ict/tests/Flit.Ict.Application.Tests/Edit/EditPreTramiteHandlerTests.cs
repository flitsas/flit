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
    [InlineData("", "core_api_unavailable")]
    [InlineData("not_draft", "not_draft")]
    [InlineData("invalid_valor_venta", "invalid_selling_price")]
    [InlineData("not_found", "core_api_not_found")]
    [InlineData("persist_failed", "core_api_error")]
    [InlineData("tenant_mismatch", "core_api_error")]
    public async Task Materializado_errores_de_core_api_pasan_por_lista_blanca(string? coreApiError, string esperado)
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

    // ===== Review PR #536: precio aceptado por core-api y conflicto al guardar en ICT =====

    [Fact]
    public async Task Materializado_conflicto_al_guardar_recarga_y_reintenta_una_vez()
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var master = Materialized(id, instanceId);
        var recargado = Materialized(id, instanceId, rowVersion: 4);
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master, recargado);
        _draftClient.UpdateCommercialAsync(_tenantId, instanceId, id, 18500000m, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));
        var intentos = 0;
        _repository.SaveAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(_ =>
            ++intentos == 1 ? throw new IctConcurrencyException() : Task.CompletedTask);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m), Ct);

        error.Should().BeNull();
        result!.ValidationReset.Should().BeFalse();
        recargado.SellingPrice.Should().Be(18500000m);
        recargado.UpdatedBy.Should().Be(_tenant.IntegrationClientId);
        await _repository.Received(2).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
        await _repository.Received(2).GetAsync(id, _tenantId, Arg.Any<CancellationToken>());
        await _draftClient.Received(1).UpdateCommercialAsync(_tenantId, instanceId, id, 18500000m, Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordTimelineEventAsync(
            id, _tenantId, "editado", "ok", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Materializado_conflicto_dos_veces_devuelve_stale_y_registra_desincronizado_sin_valores()
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Materialized(id, instanceId), Materialized(id, instanceId, rowVersion: 4));
        _draftClient.UpdateCommercialAsync(_tenantId, instanceId, id, 18500000m, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));
        _repository.SaveAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IctConcurrencyException());

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 18500000m), Ct);

        result.Should().BeNull();
        error.Should().Be("stale");
        await _repository.Received(2).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordTimelineEventAsync(
            id, _tenantId, "editado", "desincronizado",
            Arg.Is<string>(d => d.Contains("\"changed_fields\":[\"selling_price\"]") && !d.Contains("18500000")),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordTimelineEventAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), "ok", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Materializado_precio_de_16_enteros_se_acepta()
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        const decimal max = 9999999999999999.99m;
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Materialized(id, instanceId));
        _draftClient.UpdateCommercialAsync(_tenantId, instanceId, id, max, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));

        var (_, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: max), Ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Materializado_precio_de_17_enteros_se_rechaza_sin_llamar_a_core_api()
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Materialized(id, Guid.NewGuid()));

        var (_, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, SellingPrice: 10000000000000000m), Ct);

        error.Should().Be(EditPreTramiteHandler.InvalidSellingPrice);
        await _draftClient.DidNotReceiveWithAnyArgs().UpdateCommercialAsync(default, default, default, default, Ct);
    }
}
