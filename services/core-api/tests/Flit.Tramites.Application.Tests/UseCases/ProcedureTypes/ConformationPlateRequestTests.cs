using System.Text.Json.Nodes;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureTypes;

/// <summary>
/// FEATURE-08 / HU-BE-05 (CFD-08) — persistencia del flag <c>requiresPlateRequest</c> en gate_profile.
/// Cubre BE-05-AC-01. La activación condicional del paso en el wizard es HU-BE-06.
/// </summary>
public sealed class ConformationPlateRequestTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();

    [Fact]
    public async Task Put_RequiresPlateRequest_PersistsInGateProfile()
    {
        // BE-05-AC-01
        var ct = TestContext.Current.CancellationToken;
        var type = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula Inicial",
            Family = "matriculas",
            Version = 1,
            GateProfile = "{}",
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ConformationRules = [],
            Steps = []
        };
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);

        var sut = new UpdateConformationProfileHandler(_repo);
        var input = new UpdateConformationProfileInput(new JsonObject { ["requiresPlateRequest"] = true });

        var (result, error) = await sut.HandleAsync(type.Id, input, ct);

        error.Should().BeNull();
        type.GateProfile.Should().Contain("requiresPlateRequest");
        result!.GateProfile["requiresPlateRequest"]!.GetValue<bool>().Should().BeTrue();
    }
}
