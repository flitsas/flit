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
    public async Task HandleAsync_NotDraft_ReturnsNotDraft(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId, status));

        var request = new PatchFieldValuesRequest([]);
        var (result, error) = await _sut.HandleAsync(id, tenantId, request, ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
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
        await _repo.Received(1).UpdateAsync(instance, ct);
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
    }
}
