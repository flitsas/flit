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
/// FEATURE-08 / HU-BE-04 (CFD-06, CFD-07) — documentos requeridos por tipo y flags comercial/
/// identidad/firma en gate_profile. Cubre BE-04-AC-02 (persistencia docs), AC-05/06/07 (persistencia
/// de flags en gate_profile). El bloqueo de gates (DynamicGateEvaluator) es HU-BE-06.
/// </summary>
public sealed class ConformationDocumentsTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();
    private readonly ICatalogRepository _catalog = Substitute.For<ICatalogRepository>();
    private readonly IProcedureTypeSourceRepository _sources = Substitute.For<IProcedureTypeSourceRepository>();
    private readonly IProcedureTypeDocumentRepository _docs = Substitute.For<IProcedureTypeDocumentRepository>();

    private static ProcedureType Draft(string gateProfile = "{}") => new()
    {
        Id = Guid.NewGuid(),
        Code = "MATRICULA_NUEVA",
        Name = "Matrícula Inicial",
        Family = "matriculas",
        Version = 1,
        GateProfile = gateProfile,
        PublicationStatus = PublicationStatus.Draft,
        CreatedAt = DateTimeOffset.UtcNow,
        ConformationRules = [],
        Steps = []
    };

    private UpdateConformationProfileHandler UpdateSut() =>
        new(_repo, _catalog, _sources, _docs);

    [Fact]
    public async Task Put_PersistsDocumentRequirements_ResolvingCodes()
    {
        // BE-04-AC-02
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        var cedulaId = Guid.NewGuid();
        var promesaId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _docs.ResolveDocumentTypeIdAsync("CEDULA", ct).Returns(cedulaId);
        _docs.ResolveDocumentTypeIdAsync("PROMESA", ct).Returns(promesaId);
        _docs.ListByTypeAsync(type.Id, ct).Returns(new List<ProcedureDocumentRequirementRecord>
        {
            new("CEDULA", true, false, null, 1),
            new("PROMESA", false, true, null, 2),
        });

        var input = new UpdateConformationProfileInput(
            GateProfile: null,
            DocumentRequirements:
            [
                new ConformationDocumentRequirementInput("CEDULA", IsRequired: true, SortOrder: 1),
                new ConformationDocumentRequirementInput("PROMESA", IsDummy: true, SortOrder: 2),
            ]);

        var (result, error) = await UpdateSut().HandleAsync(type.Id, input, ct);

        error.Should().BeNull();
        await _docs.Received(1).ReplaceRequirementsAsync(
            type.Id,
            Arg.Is<IReadOnlyList<ProcedureDocumentRequirementUpsert>>(u =>
                u.Count == 2 &&
                u[0].DocumentTypeId == cedulaId && u[0].IsRequired &&
                u[1].DocumentTypeId == promesaId && u[1].IsDummy),
            ct);
        result!.DocumentRequirements.Should().HaveCount(2);
        result.DocumentRequirements.Should().Contain(d => d.DocumentTypeCode == "CEDULA" && d.IsRequired);
        result.DocumentRequirements.Should().Contain(d => d.DocumentTypeCode == "PROMESA" && d.IsDummy);
    }

    [Fact]
    public async Task Put_UnknownDocumentCode_ReturnsDocumentTypeNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _docs.ResolveDocumentTypeIdAsync("GHOST", ct).Returns((Guid?)null);

        var input = new UpdateConformationProfileInput(
            GateProfile: null,
            DocumentRequirements: [new ConformationDocumentRequirementInput("GHOST", IsRequired: true)]);

        var (result, error) = await UpdateSut().HandleAsync(type.Id, input, ct);

        error.Should().Be("document_type_not_found:GHOST");
        result.Should().BeNull();
        await _docs.DidNotReceive().ReplaceRequirementsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ProcedureDocumentRequirementUpsert>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_GateProfileWithCommercialBiometricSignatureFlags_Persists()
    {
        // BE-04-AC-05/06/07 (persistencia en gate_profile).
        var ct = TestContext.Current.CancellationToken;
        var type = Draft("{}");
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);

        var input = new UpdateConformationProfileInput(new JsonObject
        {
            ["requiresCommercialValue"] = true,
            ["commercialValueSource"] = "FASECOLDA",
            ["requiresBiometrics"] = true,
            ["biometricActors"] = new JsonArray("BUYER", "OWNER"),
            ["requiresSignature"] = true,
        });

        var (result, error) = await UpdateSut().HandleAsync(type.Id, input, ct);

        error.Should().BeNull();
        type.GateProfile.Should().Contain("FASECOLDA");
        result!.GateProfile["requiresCommercialValue"]!.GetValue<bool>().Should().BeTrue();
        result.GateProfile["commercialValueSource"]!.GetValue<string>().Should().Be("FASECOLDA");
        result.GateProfile["requiresBiometrics"]!.GetValue<bool>().Should().BeTrue();
        result.GateProfile["requiresSignature"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Get_IncludesDocumentRequirements()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = Draft();
        _repo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        _docs.ListByTypeAsync(type.Id, ct).Returns(new List<ProcedureDocumentRequirementRecord>
        {
            new("CEDULA", true, false, null, 1),
        });

        var get = new GetConformationProfileHandler(_repo, _sources, _docs);
        var (result, error) = await get.HandleAsync(type.Id, ct);

        error.Should().BeNull();
        result!.DocumentRequirements.Should().ContainSingle(d => d.DocumentTypeCode == "CEDULA" && d.IsRequired);
    }
}
