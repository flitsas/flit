using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class GetProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetProcedureInstanceHandler _sut;

    public GetProcedureInstanceTests()
    {
        _sut = new GetProcedureInstanceHandler(_repo);
    }

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Exists_ReturnsMappedDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            FieldValues =
            {
                new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    FormFieldId = Guid.NewGuid(),
                    FieldKey = "plate",
                    ValueText = "ABC123",
                    Source = "user"
                }
            },
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ToStatus = TramiteEstado.Borrador,
                    ChangedAt = DateTimeOffset.UtcNow
                }
            }
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.ReferenceNumber.Should().Be("TRM-2026-000001");
        result.FieldValues.Should().ContainSingle(f => f.FieldKey == "plate");
        result.StatusHistory.Should().ContainSingle(h => h.ToStatus == TramiteEstado.Borrador);
    }
}
