using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// FEATURE-08 / HU-BE-01 (CFD-01 / AC#5) — captura del snapshot inmutable del tipo al crear la
/// instancia. Cubre BE-01-AC-03 (snapshot capturado con gateProfile/conformationRules/stepSectionTypes,
/// sin form_fields) y BE-01-AC-04 (el snapshot es una copia por valor: no cambia si el tipo live se edita).
/// </summary>
public sealed class CaptureTypeSnapshotTests
{
    private static ProcedureType BuildType(
        int version = 1,
        string gateProfile = "{\"entryMode\":\"VIN\",\"requiresBuyer\":true}") => new()
    {
        Id = Guid.NewGuid(),
        Code = "MATRICULA_NUEVA",
        Name = "Matrícula Inicial",
        Family = "matriculas",
        Version = version,
        GateProfile = gateProfile,
        PublicationStatus = PublicationStatus.Published,
        WizardEnabled = true,
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
        Steps =
        [
            new ProcedureStep
            {
                Code = "consulta",
                Title = "Consulta del vehículo",
                SortOrder = 1,
                Sections =
                [
                    new ProcedureSection { Code = "VEHICULO", SectionType = "vehicle_query", SortOrder = 1 },
                    new ProcedureSection { Code = "CHECKLIST", SectionType = "document_checklist", SortOrder = 2 }
                ]
            }
        ]
    };

    // ── Builder (puro) ───────────────────────────────────────────────────────

    [Fact]
    public void Build_CapturesGateProfileConformationRulesAndStepSectionTypes()
    {
        var type = BuildType();

        var json = ProcedureTypeSnapshotBuilder.Build(type);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("code").GetString().Should().Be("MATRICULA_NUEVA");
        root.GetProperty("family").GetString().Should().Be("matriculas");
        root.GetProperty("version").GetInt32().Should().Be(1);
        root.GetProperty("gateProfile").GetProperty("entryMode").GetString().Should().Be("VIN");

        var rules = root.GetProperty("conformationRules");
        rules.GetArrayLength().Should().Be(1);
        rules[0].GetProperty("entityCode").GetString().Should().Be("BUYER");
        rules[0].GetProperty("validationProfile").GetProperty("requiresRunt").GetBoolean().Should().BeTrue();

        var steps = root.GetProperty("stepSectionTypes");
        steps.GetArrayLength().Should().Be(1);
        steps[0].GetProperty("stepCode").GetString().Should().Be("consulta");
        steps[0].GetProperty("sectionTypes").EnumerateArray()
            .Select(e => e.GetString())
            .Should().ContainInOrder("vehicle_query", "document_checklist");

        // `WizardStateQuery.FromSnapshot` LEE estas dos llaves, y nadie las escribía. Sin `stepTitle`
        // el paso caía al respaldo genérico («Actores» en vez de «Vendedor»/«Comprador»/«Locatario»);
        // sin `sectionCodes`, `SectionCoversSeller(null)` devuelve true y el paso de actores exigía
        // la parte vendedora en un tipo cuyo recorrido no la tiene.
        steps[0].GetProperty("stepTitle").GetString().Should().Be("Consulta del vehículo");
        steps[0].GetProperty("sectionCodes").EnumerateArray()
            .Select(e => e.GetString())
            .Should().ContainInOrder("VEHICULO", "CHECKLIST");
    }

    [Fact]
    public void Build_DoesNotIncludeFormFields()
    {
        var json = ProcedureTypeSnapshotBuilder.Build(BuildType());

        // BE-01-AC-03: el snapshot es liviano — nunca embebe los form_fields del tipo.
        json.Should().NotContain("formField");
        json.Should().NotContain("form_fields");
    }

    [Fact]
    public void Build_ToleratesCorruptGateProfile_DegradesToEmptyObject()
    {
        var type = BuildType(gateProfile: "not-json");

        var json = ProcedureTypeSnapshotBuilder.Build(type);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("gateProfile").ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.GetProperty("gateProfile").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Build_IsAValueCopy_UnaffectedByLaterEditsToTheLiveType()
    {
        var type = BuildType(version: 1, gateProfile: "{\"entryMode\":\"VIN\"}");
        var snapshotAtV1 = ProcedureTypeSnapshotBuilder.Build(type);

        // El SuperAdmin re-publica el tipo con otra versión y otra configuración.
        type.Version = 2;
        type.GateProfile = "{\"entryMode\":\"PLATE\"}";
        var snapshotAtV2 = ProcedureTypeSnapshotBuilder.Build(type);

        // BE-01-AC-04: el snapshot capturado en v1 conserva su estado; la instancia en curso lo
        // lee, no la versión live.
        snapshotAtV1.Should().Contain("\"version\":1");
        snapshotAtV1.Should().Contain("VIN");
        snapshotAtV1.Should().NotContain("PLATE");
        snapshotAtV1.Should().NotBe(snapshotAtV2);
    }

    // ── Handler ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TypeNotFound_ReturnsErrorAndDoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var typeRepo = Substitute.For<IProcedureTypeRepository>();
        var snapRepo = Substitute.For<IProcedureTypeSnapshotRepository>();
        typeRepo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), ct).Returns((ProcedureType?)null);
        var sut = new CaptureTypeSnapshotHandler(typeRepo, snapRepo);

        var (captured, error) = await sut.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ct);

        captured.Should().BeFalse();
        error.Should().Be("not_found");
        await snapRepo.DidNotReceive().AddAsync(Arg.Any<ProcedureTypeSnapshotRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersistsSnapshotRecordWithTypeVersionAndTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = BuildType(version: 1);
        var typeRepo = Substitute.For<IProcedureTypeRepository>();
        var snapRepo = Substitute.For<IProcedureTypeSnapshotRepository>();
        typeRepo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        var sut = new CaptureTypeSnapshotHandler(typeRepo, snapRepo);

        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var (captured, error) = await sut.HandleAsync(instanceId, type.Id, tenantId, userId, ct);

        captured.Should().BeTrue();
        error.Should().BeNull();
        await snapRepo.Received(1).AddAsync(
            Arg.Is<ProcedureTypeSnapshotRecord>(r =>
                r.ProcedureInstanceId == instanceId &&
                r.TenantId == tenantId &&
                r.ProcedureTypeId == type.Id &&
                r.TypeVersion == 1 &&
                r.CreatedBy == userId &&
                r.Snapshot.Contains("\"version\":1")),
            ct);
        await snapRepo.Received(1).SaveChangesAsync(ct);
    }

    // ── Integración: CreateProcedureInstance cablea la captura ────────────────

    [Fact]
    public async Task CreateInstance_WiresSnapshotCaptureAfterPersisting()
    {
        var ct = TestContext.Current.CancellationToken;
        var type = BuildType(version: 1);
        var instanceRepo = Substitute.For<IProcedureInstanceRepository>();
        var typeRepo = Substitute.For<IProcedureTypeRepository>();
        var snapRepo = Substitute.For<IProcedureTypeSnapshotRepository>();

        typeRepo.GetByIdAsync(type.Id, ct).Returns(type);
        typeRepo.GetByIdWithDetailsAsync(type.Id, ct).Returns(type);
        instanceRepo.AddWithUniqueReferenceAsync(
                Arg.Any<ProcedureInstance>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<ProcedureInstance>().ReferenceNumber = "TRM-2026-000001";
                return Task.FromResult(AddProcedureInstanceOutcome.Created);
            });

        var capture = new CaptureTypeSnapshotHandler(typeRepo, snapRepo);
        var sut = new CreateProcedureInstanceHandler(instanceRepo, typeRepo, capture);

        var tenantId = Guid.NewGuid();
        var request = new CreateProcedureInstanceRequest(tenantId, type.Id, Guid.NewGuid(), null);

        var (result, error) = await sut.HandleAsync(request, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        await snapRepo.Received(1).AddAsync(
            Arg.Is<ProcedureTypeSnapshotRecord>(r =>
                r.ProcedureTypeId == type.Id && r.TenantId == tenantId && r.TypeVersion == 1),
            ct);
    }
}
