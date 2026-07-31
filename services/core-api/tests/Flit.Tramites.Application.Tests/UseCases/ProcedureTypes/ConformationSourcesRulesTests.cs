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
/// FEATURE-08 / HU-BE-03 (CFD-04, CFD-05) — fuentes por tipo y actores configurables (incl. LESSEE)
/// en GET/PUT conformation-profile. Cubre BE-03-AC-02..07.
/// </summary>
public sealed class ConformationSourcesRulesTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();
    private readonly ICatalogRepository _catalog = Substitute.For<ICatalogRepository>();
    private readonly IProcedureTypeSourceRepository _sources = Substitute.For<IProcedureTypeSourceRepository>();

    private static ProcedureType Draft() => new()
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

    // ── Fuentes (CFD-04) ───────────────────────────────────────────────────────

    [Fact]
    public async Task Put_PersistsSources_ResolvingCodes_AndReturnsThem()
    {
        // BE-03-AC-02 / AC-03 / AC-04
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        var runtId = Guid.NewGuid();
        var simitId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _catalog.GetExternalDataSourceByCodeAsync("RUNT", ct)
            .Returns(new ExternalDataSource { Id = runtId, Code = "RUNT" });
        _catalog.GetExternalDataSourceByCodeAsync("SIMIT", ct)
            .Returns(new ExternalDataSource { Id = simitId, Code = "SIMIT" });
        _sources.ListByTypeAsync(type.Id, ct).Returns(new List<ProcedureTypeSourceRecord>
        {
            new(runtId, "RUNT", 1, "{}"),
            new(simitId, "SIMIT", 2, "{\"simitMode\":\"INTERNAL\"}"),
        });

        var sut = new UpdateConformationProfileHandler(_repo, _catalog, _sources);
        var input = new UpdateConformationProfileInput(
            GateProfile: null,
            Sources:
            [
                new ConformationSourceInput("RUNT", 1, null),
                new ConformationSourceInput("SIMIT", 2, new JsonObject { ["simitMode"] = "INTERNAL" }),
            ]);

        var (result, error) = await sut.HandleAsync(type.Id, input, ct);

        error.Should().BeNull();
        await _sources.Received(1).ReplaceSourcesAsync(
            type.Id,
            Arg.Is<IReadOnlyList<ProcedureTypeSourceUpsert>>(u =>
                u.Count == 2 &&
                u[0].ExternalDataSourceId == runtId && u[0].ExecutionOrder == 1 &&
                u[1].ExternalDataSourceId == simitId && u[1].ExecutionOrder == 2 &&
                u[1].Config.Contains("INTERNAL")),
            ct);

        // AC-03: retornadas ordenadas por execution_order; AC-04: simitMode en config.
        result!.Sources.Should().HaveCount(2);
        result.Sources[0].SourceCode.Should().Be("RUNT");
        result.Sources[1].SourceCode.Should().Be("SIMIT");
        result.Sources[1].Config["simitMode"]!.GetValue<string>().Should().Be("INTERNAL");
    }

    [Fact]
    public async Task Put_UnknownSourceCode_ReturnsSourceNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _catalog.GetExternalDataSourceByCodeAsync("XYZ", ct).Returns((ExternalDataSource?)null);

        var sut = new UpdateConformationProfileHandler(_repo, _catalog, _sources);
        var input = new UpdateConformationProfileInput(
            GateProfile: null, Sources: [new ConformationSourceInput("XYZ", 1, null)]);

        var (result, error) = await sut.HandleAsync(type.Id, input, ct);

        error.Should().Be("source_not_found:XYZ");
        result.Should().BeNull();
        await _sources.DidNotReceive().ReplaceSourcesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ProcedureTypeSourceUpsert>>(), Arg.Any<CancellationToken>());
    }

    // ── Actores / conformation_rules (CFD-05) ──────────────────────────────────

    [Fact]
    public async Task Put_PersistsConformationRules_IncludingLessee()
    {
        // BE-03-AC-06
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _catalog.GetProcedureEntityByCodeAsync("OWNER", ct).Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "OWNER", Name = "Propietario" });
        _catalog.GetProcedureEntityByCodeAsync("BUYER", ct).Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "BUYER", Name = "Comprador" });
        _catalog.GetProcedureEntityByCodeAsync("LESSEE", ct).Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "LESSEE", Name = "Locatario" });

        var sut = new UpdateConformationProfileHandler(_repo, _catalog, _sources);
        var input = new UpdateConformationProfileInput(
            GateProfile: null,
            ConformationRules:
            [
                new ConformationRuleUpsertInput("OWNER", new JsonObject { ["requiresRunt"] = true }, true, 1),
                new ConformationRuleUpsertInput("BUYER", new JsonObject { ["allowsMultiple"] = true }, true, 2),
                new ConformationRuleUpsertInput("LESSEE", new JsonObject { ["allowsJuridicalPerson"] = true, ["requiresRunt"] = true }, true, 3),
            ]);

        var (result, error) = await sut.HandleAsync(type.Id, input, ct);

        error.Should().BeNull();
        await _repo.Received(1).ReplaceConformationRulesAsync(
            type.Id,
            Arg.Is<List<ConformationRule>>(l =>
                l.Count == 3 && l.Any(r => r.ProcedureEntity!.Code == "LESSEE")),
            ct);
        result!.ConformationRules.Should().HaveCount(3);
        result.ConformationRules.Should().Contain(r => r.EntityCode == "LESSEE");
    }

    [Fact]
    public async Task Put_UnknownEntityCode_ReturnsEntityNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _catalog.GetProcedureEntityByCodeAsync("GHOST", ct).Returns((ProcedureEntity?)null);

        var sut = new UpdateConformationProfileHandler(_repo, _catalog, _sources);
        var input = new UpdateConformationProfileInput(
            GateProfile: null,
            ConformationRules: [new ConformationRuleUpsertInput("GHOST", null)]);

        var (_, error) = await sut.HandleAsync(type.Id, input, ct);

        error.Should().Be("entity_not_found:GHOST");
    }

    // ── GET incluye sources + rules juntos (CFD-04/05) ─────────────────────────

    [Fact]
    public async Task Get_IncludesSourcesAndConformationRules()
    {
        // BE-03-AC-07
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        type.ConformationRules =
        [
            new ConformationRule { SortOrder = 1, ValidationProfile = "{\"requiresRunt\":true}", ProcedureEntity = new ProcedureEntity { Code = "OWNER", Name = "Propietario" } },
        ];
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _sources.ListByTypeAsync(type.Id, ct).Returns(new List<ProcedureTypeSourceRecord>
        {
            new(Guid.NewGuid(), "RUNT", 1, "{}"),
            new(Guid.NewGuid(), "SIMIT", 2, "{}"),
        });

        var sut = new GetConformationProfileHandler(_repo, _sources);
        var (result, error) = await sut.HandleAsync(type.Id, ct);

        error.Should().BeNull();
        result!.Sources.Should().HaveCount(2);
        result.ConformationRules.Should().ContainSingle(r => r.EntityCode == "OWNER");
    }
}
