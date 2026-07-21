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
/// FEATURE-08 / HU-BE-01 (CFD-01) — GET/PUT del perfil de conformación del tipo.
/// Cubre BE-01-AC-05 (GET retorna estructura completa) y BE-01-AC-06 (PUT rechaza published con 422).
/// </summary>
public sealed class ConformationProfileTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();

    private static ProcedureType Draft(string gateProfile = "{\"entryMode\":\"VIN\"}") => new()
    {
        Id = Guid.NewGuid(),
        Code = "MATRICULA_NUEVA",
        Name = "Matrícula Inicial",
        Family = "matriculas",
        Version = 1,
        GateProfile = gateProfile,
        PublicationStatus = PublicationStatus.Draft,
        CreatedAt = DateTimeOffset.UtcNow,
        ConformationRules =
        [
            new ConformationRule
            {
                SortOrder = 1,
                ValidationProfile = "{\"requiresRunt\":true}",
                ProcedureEntity = new ProcedureEntity { Code = "BUYER", Name = "Comprador" }
            }
        ],
        Steps = []
    };

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_NotFound_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), ct).Returns((ProcedureType?)null);
        var sut = new GetConformationProfileHandler(_repo);

        var (result, error) = await sut.HandleAsync(Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Get_ReturnsCompleteShape_WithGateProfileRulesAndEmptySourcesDocs()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        var sut = new GetConformationProfileHandler(_repo);

        var (result, error) = await sut.HandleAsync(type.Id, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.ProcedureTypeId.Should().Be(type.Id);
        result.Version.Should().Be(1);
        result.PublicationStatus.Should().Be(PublicationStatus.Draft);
        result.GateProfile["entryMode"]!.GetValue<string>().Should().Be("VIN");
        result.ConformationRules.Should().ContainSingle();
        result.ConformationRules[0].EntityCode.Should().Be("BUYER");
        result.ConformationRules[0].ValidationProfile["requiresRunt"]!.GetValue<bool>().Should().BeTrue();
        // BE-01-AC-05: las 4 claves están presentes; sources/documentRequirements vacías en BE-01.
        result.Sources.Should().BeEmpty();
        result.DocumentRequirements.Should().BeEmpty();
    }

    // ── PUT ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_NotFound_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), ct).Returns((ProcedureType?)null);
        var sut = new UpdateConformationProfileHandler(_repo);

        var (result, error) = await sut.HandleAsync(
            Guid.NewGuid(), new UpdateConformationProfileInput(new JsonObject()), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_PublishedType_ReturnsNotEditable()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        type.PublicationStatus = PublicationStatus.Published;
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        var sut = new UpdateConformationProfileHandler(_repo);

        var (result, error) = await sut.HandleAsync(
            type.Id, new UpdateConformationProfileInput(new JsonObject { ["entryMode"] = "PLATE" }), ct);

        // BE-01-AC-06: tipo published → 422 (not_editable). No se persiste.
        error.Should().Be("not_editable");
        result.Should().BeNull();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<ProcedureType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_DraftType_PersistsGateProfileAndReturnsProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = Draft(gateProfile: "{}");
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        var sut = new UpdateConformationProfileHandler(_repo);

        var input = new UpdateConformationProfileInput(
            new JsonObject { ["entryMode"] = "BOTH", ["requiresBuyer"] = true });

        var (result, error) = await sut.HandleAsync(type.Id, input, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.GateProfile["entryMode"]!.GetValue<string>().Should().Be("BOTH");
        type.GateProfile.Should().Contain("BOTH");
        await _repo.Received(1).UpdateAsync(type, ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }
}
