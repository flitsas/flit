using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

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
        var instance = Instance(id, tenantId, ProcedureInstanceStatus.Submitted);
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
        var instance = Instance(id, tenantId, ProcedureInstanceStatus.Draft);
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
        var instance = Instance(id, tenantId, ProcedureInstanceStatus.Draft);
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
        var instance = Instance(id, tenantId, ProcedureInstanceStatus.Draft);
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
        var instance = Instance(id, tenantId, ProcedureInstanceStatus.Draft);
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
}
