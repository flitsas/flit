using Flit.Tramites.Application.BulkTramites.Processing;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>
/// Resolución del organismo de tránsito de la fila (HU #12520/#12523). El Excel lleva el NOMBRE
/// —nadie escribe un GUID— y aquí se traduce al id que exige el paso 1 del wizard, contra los
/// organismos HABILITADOS de esa empresa.
///
/// <para>Existe porque la primera versión mandaba <c>TransitOfficeId: null</c> siempre y toda fila
/// de matrícula moría en <c>TRANSIT_OFFICE_REQUIRED</c>: el wizard no consulta el VIN sin
/// secretaría (HU #11199). Solo apareció al probar el flujo completo contra la API.</para>
/// </summary>
public sealed class BulkTramitesWizardGatewayTests
{
    private readonly ITransitOfficeResolver _resolver = Substitute.For<ITransitOfficeResolver>();

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OficinaId = Guid.NewGuid();

    private static BulkTramitesRowContext Context(string? organismo) => new(
        Tenant,
        Guid.NewGuid(),
        ProcedureFamilyCodes.Matriculas,
        "MATRICULA_NUEVA",
        Vin: "9BWZZZ377VT004251",
        Plate: null,
        OwnerDocumentType: null,
        OwnerDocumentNumber: null,
        Actors: [],
        TransitOfficeName: organismo);

    /// <summary>
    /// El gateway real arrastra tres handlers del wizard con constructores enormes; para probar
    /// SOLO la resolución del organismo se ejercita a través del camino público, comprobando que
    /// un nombre que no resuelve corta la fila ANTES de tocar el wizard.
    /// </summary>
    private BulkTramitesWizardGateway Gateway() =>
        new(null!, null!, null!, _resolver);

    [Fact]
    public async Task NombreQueNoEstaEntreLosHabilitados_CortaLaFila_SinLlegarAlWizard()
    {
        _resolver.ResolveEnabledByNameAsync(Tenant, "Secretaría Inventada", Arg.Any<CancellationToken>())
            .Returns((ResolvedTransitOffice?)null);

        var (token, error) = await Gateway()
            .PreviewVehicleAsync(Context("Secretaría Inventada"), TestContext.Current.CancellationToken);

        token.Should().BeNull();
        error.Should().Be(BulkTramitesWizardGateway.OrganismoNoHabilitado);
    }

    [Fact]
    public async Task CrearConOrganismoNoHabilitado_TampocoCreaElTramite()
    {
        _resolver.ResolveEnabledByNameAsync(Tenant, "Secretaría Inventada", Arg.Any<CancellationToken>())
            .Returns((ResolvedTransitOffice?)null);

        var (instanceId, error) = await Gateway()
            .CreateTramiteAsync(Context("Secretaría Inventada"), "token", TestContext.Current.CancellationToken);

        instanceId.Should().BeNull();
        error.Should().Be(BulkTramitesWizardGateway.OrganismoNoHabilitado);
    }

    [Fact]
    public async Task SinNombreDeOrganismo_NoSeResuelveNada()
    {
        // Traspaso y «Otros» no traen la columna: el organismo lo impone el RUNT. Que no se
        // resuelva nada es lo que evita inventar un id que el wizard rechazaría.
        var gateway = Gateway();

        // Sin organismo el gateway sigue hacia el wizard (que aquí es null) — la NullReference
        // confirma que pasó de largo la resolución en vez de cortar con un error propio.
        var acto = async () => await gateway.PreviewVehicleAsync(
            Context(organismo: null), TestContext.Current.CancellationToken);

        await acto.Should().ThrowAsync<NullReferenceException>();
        await _resolver.DidNotReceive().ResolveEnabledByNameAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
