using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class RuntPersonLookupHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly RuntPersonLookupHandler _sut;

    public RuntPersonLookupHandlerTests()
    {
        _sut = new RuntPersonLookupHandler(_repo, _registry);
    }

    private sealed class FakeProvider(ConsultationResult result) : IConsultationProvider
    {
        public string Key => "verifik_conductor";
        public ConsultationContext? CapturedContext { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            CapturedContext = ctx;
            return Task.FromResult(result);
        }
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = ProcedureInstanceStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ConsultationResult FoundResult() =>
        new("verifik_conductor", "green",
            [new ConsultationCheck("conductor", "Persona en RUNT", "ok", "verifik_conductor", null)],
            [
                new HydratedField("person_full_name", "JUAN CARLOS PEREZ GOMEZ", null),
                new HydratedField("person_first_name", "JUAN CARLOS", null),
                new HydratedField("person_last_name", "PEREZ GOMEZ", null),
                new HydratedField("person_license_status", "ACTIVO", null),
            ]);

    private static ConsultationResult NotFoundResult() =>
        new("verifik_conductor", "yellow",
            [new ConsultationCheck("conductor", "Persona en RUNT", "unknown", "verifik_conductor", "Persona no encontrada en RUNT")],
            []);

    [Fact]
    public async Task HandleAsync_InvalidRequest_WhenBlankDocument()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "CC", "   ", ct);

        error.Should().Be("invalid_request");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_InstanceNotFound_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "CC", "123456789", ct);

        error.Should().Be("instance_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ProviderNotFound_WhenRegistryReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns((IConsultationProvider?)null);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().Be("provider_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Found_ReturnsDtoWithName()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        var provider = new FakeProvider(FoundResult());
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.FullName.Should().Be("JUAN CARLOS PEREZ GOMEZ");
        result.FirstName.Should().Be("JUAN CARLOS");
        result.LastName.Should().Be("PEREZ GOMEZ");
        result.LicenseStatus.Should().Be("ACTIVO");
        result.DocumentType.Should().Be("CC");
        result.DocumentNumber.Should().Be("123456789");
        result.Source.Should().Be("RUNT");

        // El lookup NO persiste y NO lee los field_values de la instancia: solo arma un
        // contexto en memoria con el documento consultado.
        provider.CapturedContext.Should().NotBeNull();
        provider.CapturedContext!.FieldValues.Should().ContainKey("document_type");
        provider.CapturedContext.FieldValues["document_number"].Should().Be("123456789");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsDtoFoundFalse_Still200Shape()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns(new FakeProvider(NotFoundResult()));

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "999999999", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeFalse();
        result.FullName.Should().BeNull();
        result.FirstName.Should().BeNull();
        result.LastName.Should().BeNull();
        result.LicenseStatus.Should().BeNull();
        result.DocumentNumber.Should().Be("999999999");
    }
}
