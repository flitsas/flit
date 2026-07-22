using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.BulkImport;

public sealed class BulkImportProcedureInstancesHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeCodeResolver _officeResolver = Substitute.For<ITransitOfficeCodeResolver>();
    private readonly IMatriculaInicialGate _matriculaGate = Substitute.For<IMatriculaInicialGate>();
    private readonly BulkImportProcedureInstancesHandler _sut;

    public BulkImportProcedureInstancesHandlerTests()
    {
        var createHandler = new CreateProcedureInstanceHandler(_repo, _typeRepo);
        var patchHandler = new PatchFieldValuesHandler(_repo);
        _sut = new BulkImportProcedureInstancesHandler(
            createHandler, patchHandler, _officeResolver, _matriculaGate,
            NullLogger<BulkImportProcedureInstancesHandler>.Instance);
    }

    private static ProcedureType PublishedType(string code, string family) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Family = family,
        PublicationStatus = PublicationStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private void StubTraspasoTypeAndReferenceGenerator()
    {
        _typeRepo.GetByCodePublishedAsync("TRASPASO_STANDARD", Arg.Any<CancellationToken>())
            .Returns(PublishedType("TRASPASO_STANDARD", "TRASPASO"));

        var seq = 0;
        _repo.AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var instance = call.Arg<ProcedureInstance>();
                var year = call.ArgAt<int>(1);
                instance.ReferenceNumber = $"TRM-{year}-{++seq:D6}";
                return Task.FromResult(AddProcedureInstanceOutcome.Created);
            });
    }

    /// <summary>El patch de seed recarga la instancia: devolvemos un borrador con field_values vacío.</summary>
    private void StubInstanceLoadForSeed()
    {
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new ProcedureInstance
            {
                Id = call.ArgAt<Guid>(0),
                TenantId = call.ArgAt<Guid>(1),
                ProcedureTypeId = Guid.NewGuid(),
                Status = TramiteEstado.Borrador,
                ModalidadEntrada = "traspaso",
                TipologiaCodigo = "traspaso_standard",
                ReferenceNumber = "TRM-2026-000001",
                FieldValues = new List<ProcedureInstanceFieldValue>(),
                CreatedAt = DateTimeOffset.UtcNow
            });
        _repo.GetFormFieldIdByKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
    }

    private static ProcedureImportRow TraspasoRow(int n, string? placa = null, string? office = null) =>
        new(n, "traspaso", null, office, null, placa);

    [Fact]
    public async Task HandleAsync_AllRowsValid_CreatesDrafts()
    {
        var ct = TestContext.Current.CancellationToken;
        StubTraspasoTypeAndReferenceGenerator();
        StubInstanceLoadForSeed();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var rows = new[] { TraspasoRow(1, placa: "ABC123"), TraspasoRow(2) };

        var report = await _sut.HandleAsync(tenant, user, rows, ct);

        report.Total.Should().Be(2);
        report.Created.Should().Be(2);
        report.Failed.Should().Be(0);
        report.Results.Should().OnlyContain(r => r.Status == "created" && r.ReferenceNumber != null);
    }

    [Fact]
    public async Task HandleAsync_UnknownTransitOfficeCode_FailsRowWithoutCreating()
    {
        var ct = TestContext.Current.CancellationToken;
        _officeResolver.ResolveId("99999").Returns((Guid?)null);
        var rows = new[] { TraspasoRow(1, office: "99999") };

        var report = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), rows, ct);

        report.Failed.Should().Be(1);
        report.Results[0].Status.Should().Be("failed");
        report.Results[0].Error.Should().Be("oficina_no_encontrada");
        await _repo.DidNotReceive().AddWithUniqueReferenceAsync(
            Arg.Any<ProcedureInstance>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MatriculaInicialWithoutToggle_FailsRow()
    {
        var ct = TestContext.Current.CancellationToken;
        _matriculaGate.IsAllowedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var rows = new[] { new ProcedureImportRow(1, "matricula_inicial", null, null, "9BWZZZ377VT004251", null) };

        var report = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), rows, ct);

        report.Failed.Should().Be(1);
        report.Results[0].Error.Should().Be("matricula_inicial_no_habilitada");
        await _repo.DidNotReceive().AddWithUniqueReferenceAsync(
            Arg.Any<ProcedureInstance>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MissingModalidadAndTipo_FailsRowButContinuesBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        StubTraspasoTypeAndReferenceGenerator();
        StubInstanceLoadForSeed();
        var rows = new[]
        {
            TraspasoRow(1, placa: "ABC123"),
            new ProcedureImportRow(2, null, null, null, null, null), // ni modalidad ni tipo
        };

        var report = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), rows, ct);

        report.Total.Should().Be(2);
        report.Created.Should().Be(1);
        report.Failed.Should().Be(1);
        report.Results.Single(r => r.Row == 2).Error.Should().Be("invalid_request");
    }
}
