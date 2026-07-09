using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class PatchFieldValuesTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly PatchFieldValuesHandler _sut;

    public PatchFieldValuesTests()
    {
        _sut = new PatchFieldValuesHandler(_repo);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var request = new PatchFieldValuesRequest([]);
        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), request, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("submitted")]
    [InlineData("completed")]
    public async Task HandleAsync_NotDraft_NonTransitField_ReturnsNotDraft(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId, status));

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(null, "plate", "ABC123", null)]);
        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Submitted_TransitOfficeKeys_Allowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Entregado);
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _repo.GetFormFieldIdByKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), ct).Returns((Guid?)null);

        var request = new PatchFieldValuesRequest(
        [
            new FieldValueInput(null, "transit_office_code", "11001000", null),
            new FieldValueInput(null, "transit_office_name", "SDM", null),
            new FieldValueInput(null, "transit_office_city", "Bogotá", null),
        ]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().HaveCount(3);
        result.Should().NotBeNull();
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_Draft_NewField_IsAdded()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(Guid.NewGuid(), "plate", "ABC123", null)]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().ContainSingle(f => f.FieldKey == "plate" && f.ValueText == "ABC123");
        result!.FieldValues.Should().ContainSingle(f => f.FieldKey == "plate");
        // El hijo NUEVO se marca Added explícito → INSERT (no inferencia de estado por PK store-generated).
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceFieldValue>(f => f.FieldKey == "plate"));
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_Draft_NewField_NullFormFieldId_ResolvesByFieldKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        var resolvedFieldId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _repo.GetFormFieldIdByKeyAsync(instance.ProcedureTypeId, "plate", ct).Returns(resolvedFieldId);

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(null, "plate", "ABC123", null)]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "plate" && f.ValueText == "ABC123" && f.FormFieldId == resolvedFieldId);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_Draft_NewField_NonFormKey_PersistsAsLooseValue()
    {
        // Claves de sistema/consulta (p.ej. transit_office_code del organismo M5) no son
        // form_fields: deben persistir como valor "loose" con FormFieldId = null, NO rechazarse.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _repo.GetFormFieldIdByKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), ct).Returns((Guid?)null);

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(null, "transit_office_code", "11001000", null)]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "transit_office_code"
            && f.ValueText == "11001000"
            && f.FormFieldId == null);
        result!.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "transit_office_code" && f.FormFieldId == null);
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceFieldValue>(f =>
            f.FieldKey == "transit_office_code" && f.FormFieldId == null));
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_Draft_ExistingField_IsUpdated()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        var existing = new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FormFieldId = Guid.NewGuid(),
            FieldKey = "plate",
            ValueText = "OLD",
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow
        };
        instance.FieldValues.Add(existing);
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(existing.FormFieldId, "plate", "NEW", null)]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().HaveCount(1);
        existing.ValueText.Should().Be("NEW");
        existing.UpdatedAt.Should().NotBeNull();
        result!.FieldValues.Should().ContainSingle(f => f.FieldKey == "plate" && f.ValueText == "NEW");
        // Actualizar un value EXISTENTE (ya trackeado) NO debe marcar Added: queda como UPDATE.
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceFieldValue>());
    }

    // ── B11 (HU #10659): OT inmutable en traspaso ─────────────────────────────

    private static ProcedureInstance TraspasoInstance(Guid id, Guid tenantId, string status)
    {
        var instance = Instance(id, tenantId, status);
        instance.ModalidadEntrada = "traspaso";
        instance.TipologiaCodigo = "traspaso_standard";
        return instance;
    }

    [Theory]
    [InlineData("transit_office_id")]
    [InlineData("transit_office_code")]
    [InlineData("transit_office_name")]
    [InlineData("transit_office_city")]
    public async Task HandleAsync_Traspaso_TransitOfficeChange_Rejected(string fieldKey)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(TraspasoInstance(id, tenantId, TramiteEstado.Borrador));

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(null, fieldKey, "cualquier valor", null)]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().Be("ot_traspaso_no_modificable");
        result.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_Traspaso_Submitted_TransitOfficeChange_StillRejected()
    {
        // La excepción post-submit (IsPostSubmitTransitOfficeKey) NO aplica en traspaso.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(TraspasoInstance(id, tenantId, TramiteEstado.Entregado));

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(null, "transit_office_code", "11001000", null)]);

        var (_, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().Be("ot_traspaso_no_modificable");
        await _repo.DidNotReceive().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_Traspaso_NonTransitField_Allowed()
    {
        // El bloqueo es específico de claves transit_office_*: otros campos siguen editables en borrador.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = TraspasoInstance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(Guid.NewGuid(), "plate", "ABC123", null)]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_Matricula_TransitOfficeChange_Allowed()
    {
        // Matrícula: sin cambios — el operador elige/cambia el OT libremente.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador); // modalidad matrícula por defecto.
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _repo.GetFormFieldIdByKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), ct).Returns((Guid?)null);

        var request = new PatchFieldValuesRequest(
            [new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]);

        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().ContainSingle(f => f.FieldKey == "transit_office_id");
        await _repo.Received(1).SaveChangesAsync(ct);
    }
}
