using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — Ver/Crear por familia en los casos de uso: crear un trámite de una familia sin su
/// «Crear» → <c>family_permission_denied</c> (AC2); ver el detalle de una familia sin su «Ver» → <c>family_forbidden</c> (AC3).
/// <para>Uso de ejemplo: <c>Gate(crear: false, ver: true)</c> arma un gate que deniega toda creación.</para>
/// </summary>
public sealed class FamilyPermissionGateTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();

    private static IProcedureFamilyPermissionGate Gate(bool crear, bool ver)
    {
        var gate = Substitute.For<IProcedureFamilyPermissionGate>();
        gate.CanCreate(Arg.Any<ProcedureFamily>()).Returns(crear);
        gate.CanRead(Arg.Any<ProcedureFamily>()).Returns(ver);
        return gate;
    }

    private static ProcedureType PublishedType(string family) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T",
        Name = "T",
        Family = family,
        PublicationStatus = PublicationStatus.Published,
        WizardEnabled = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // AC2: Ver sin Crear → no se crea nada.
    [Theory]
    [InlineData("MATRICULAS")]
    [InlineData("TRASPASO")]
    [InlineData("OTROS")]
    public async Task AC2_SinCrearDeLaFamilia_NoCreaNiPersiste(string familia)
    {
        var ct = TestContext.Current.CancellationToken;
        var tipo = PublishedType(familia);
        _typeRepo.GetByIdAsync(tipo.Id, ct).Returns(tipo);
        var sut = new CreateProcedureInstanceHandler(_repo, _typeRepo, familyPermissions: Gate(crear: false, ver: true));

        var (result, error) = await sut.HandleAsync(new CreateProcedureInstanceRequest(Guid.NewGuid(), tipo.Id, Guid.NewGuid(), null), ct);

        error.Should().Be("family_permission_denied");
        result.Should().BeNull();
        await _repo.DidNotReceiveWithAnyArgs().AddWithUniqueReferenceAsync(default!, ct);
    }

    [Fact]
    public async Task AC2_SoloSeEvaluaLaFamiliaDelTipo()
    {
        var ct = TestContext.Current.CancellationToken;
        var tipo = PublishedType("TRASPASO");
        _typeRepo.GetByIdAsync(tipo.Id, ct).Returns(tipo);
        var gate = Substitute.For<IProcedureFamilyPermissionGate>();
        gate.CanCreate(ProcedureFamily.Matriculas).Returns(true);
        gate.CanCreate(ProcedureFamily.Traspaso).Returns(false);
        var sut = new CreateProcedureInstanceHandler(_repo, _typeRepo, familyPermissions: gate);

        var (_, error) = await sut.HandleAsync(new CreateProcedureInstanceRequest(Guid.NewGuid(), tipo.Id, Guid.NewGuid(), null), ct);

        error.Should().Be("family_permission_denied");
        gate.Received(1).CanCreate(ProcedureFamily.Traspaso);
    }

    [Fact]
    public async Task SinGate_ComportamientoPrevio_NoDeniega()
    {
        var ct = TestContext.Current.CancellationToken;
        var tipo = PublishedType("MATRICULAS");
        _typeRepo.GetByIdAsync(tipo.Id, ct).Returns(tipo);
        var sut = new CreateProcedureInstanceHandler(_repo, _typeRepo);

        var (_, error) = await sut.HandleAsync(new CreateProcedureInstanceRequest(Guid.NewGuid(), tipo.Id, Guid.NewGuid(), null), ct);

        error.Should().NotBe("family_permission_denied");
    }

    private ProcedureInstance Instancia(string familia)
    {
        var tipo = PublishedType(familia);
        var tenant = Guid.NewGuid();
        var instancia = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureTypeId = tipo.Id,
            ProcedureType = tipo,
            ReferenceNumber = "1",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(instancia.Id, tenant, Arg.Any<CancellationToken>()).Returns(instancia);
        return instancia;
    }

    // AC3: sin el «Ver» de la familia el detalle responde family_forbidden (el endpoint lo traduce a 403).
    [Fact]
    public async Task AC3_Detalle_SinVerDeLaFamilia_Forbidden()
    {
        var instancia = Instancia("OTROS");
        var sut = new GetProcedureInstanceHandler(_repo, familyPermissions: Gate(crear: true, ver: false));

        var (result, error) = await sut.HandleAsync(instancia.Id, instancia.TenantId, TestContext.Current.CancellationToken);

        error.Should().Be("family_forbidden");
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC3_Detalle_ConVerDeLaFamilia_Devuelve()
    {
        var instancia = Instancia("OTROS");
        var sut = new GetProcedureInstanceHandler(_repo, familyPermissions: Gate(crear: false, ver: true));

        var (result, error) = await sut.HandleAsync(instancia.Id, instancia.TenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }
}
