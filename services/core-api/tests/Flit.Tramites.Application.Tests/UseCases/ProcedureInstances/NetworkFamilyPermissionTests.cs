using Flit.Queries.Domain;
using Flit.Queries.Domain.Tenancy;
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
/// HU #13664 (Feature #13636 F-F, Épica #12753) AC3 — «Ver por familia» en la vista de red de la cabeza de grupo: el detalle
/// responde <c>family_forbidden</c> (el endpoint lo traduce a 403) sin el «.read» de la familia, y el listado y el conteo por
/// estado reciben las familias legibles (<see cref="ProcedureInstanceListRequest.AllowedFamilies"/>) en el filtro del repositorio.
/// <para>Uso de ejemplo: <c>request with { AllowedFamilies = gate.ReadableFamilyCodes() }</c> deja fuera los trámites de OTROS.</para>
/// </summary>
public sealed class NetworkFamilyPermissionTests
{
    private static readonly Guid Cabeza = Guid.NewGuid();
    private static readonly Guid Hijo = Guid.NewGuid();
    private static readonly TenantScope Grupo = TenantScope.Group(Cabeza, [Hijo], GroupKind.Concesion);

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static IProcedureFamilyPermissionGate Gate(bool ver)
    {
        var gate = Substitute.For<IProcedureFamilyPermissionGate>();
        gate.CanRead(Arg.Any<ProcedureFamily>()).Returns(ver);
        return gate;
    }

    private ProcedureInstance InstanciaDelHijo(string familia)
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(), Code = "T", Name = "T", Family = familia,
            PublicationStatus = PublicationStatus.Published, WizardEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        var instancia = new ProcedureInstance
        {
            Id = Guid.NewGuid(), TenantId = Hijo, ProcedureTypeId = tipo.Id, ProcedureType = tipo,
            ReferenceNumber = "1", Status = TramiteEstado.Borrador, CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(instancia.Id, Grupo, Arg.Any<CancellationToken>()).Returns(instancia);
        return instancia;
    }

    [Fact]
    public async Task AC3_RedDetalle_SinVerDeLaFamilia_FamilyForbidden()
    {
        var instancia = InstanciaDelHijo("OTROS");
        var sut = new NetworkGetProcedureInstanceHandler(_repo, Gate(ver: false));

        var (result, error) = await sut.HandleAsync(instancia.Id, Grupo, TestContext.Current.CancellationToken);

        error.Should().Be("family_forbidden");
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC3_RedDetalle_ConVerDeLaFamilia_Devuelve()
    {
        var instancia = InstanciaDelHijo("OTROS");
        var sut = new NetworkGetProcedureInstanceHandler(_repo, Gate(ver: true));

        var (result, error) = await sut.HandleAsync(instancia.Id, Grupo, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task RedDetalle_SinGate_ComportamientoPrevio()
    {
        var instancia = InstanciaDelHijo("OTROS");
        var sut = new NetworkGetProcedureInstanceHandler(_repo);

        var (_, error) = await sut.HandleAsync(instancia.Id, Grupo, TestContext.Current.CancellationToken);

        error.Should().BeNull();
    }

    [Fact]
    public async Task AC3_RedListado_PasaLasFamiliasLegiblesAlFiltroDelRepositorio()
    {
        var ct = TestContext.Current.CancellationToken;
        ProcedureInstanceListFilter? filtro = null;
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Do<ProcedureInstanceListFilter>(f => filtro = f),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(([], 0));
        var sut = new NetworkListProcedureInstancesHandler(_repo);

        var (_, _, error) = await sut.HandleAsync(
            Grupo, null, new ProcedureInstanceListRequest { AllowedFamilies = ["MATRICULAS", "TRASPASO"] }, ct);

        error.Should().BeNull();
        filtro!.AllowedFamilies.Should().Equal("MATRICULAS", "TRASPASO");
    }

    [Fact]
    public async Task AC3_RedConteo_PasaLasFamiliasLegiblesAlFiltroDelRepositorio()
    {
        var ct = TestContext.Current.CancellationToken;
        ProcedureInstanceListFilter? filtro = null;
        _repo.CountByStatusFilteredAsync(Arg.Any<TenantScope>(), Arg.Do<ProcedureInstanceListFilter>(f => filtro = f), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _repo.ListTenantIdsWithMatchesAsync(Arg.Any<TenantScope>(), Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());
        var sut = new NetworkCountProcedureInstancesByStatusHandler(_repo);

        var (counts, error) = await sut.HandleAsync(Grupo, null, new ProcedureInstanceListRequest { AllowedFamilies = ["OTROS"] }, ct);

        error.Should().BeNull();
        counts.Should().NotBeNull();
        filtro!.AllowedFamilies.Should().Equal("OTROS");
    }
}
