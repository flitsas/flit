using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #14603 (punto 3) — tras aprobar una validación MANUAL, el dashboard pintaba «Sin firma» mientras el detalle mostraba la
/// firma. Se ejercitan las DOS rutas del listado (sin filtros y filtrada/paginada) con único y múltiple propietario.
/// </summary>
public sealed class Bug14603FirmaManualListadoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    public static TheoryData<int> Propietarios => new() { 1, 2 };

    [Theory]
    [MemberData(nameof(Propietarios))]
    public async Task ListadoSinFiltros_AprobacionManual_DiceFirmado(int vendedores)
    {
        var instance = Traspaso(vendedores);
        Preparar(instance);

        var fila = (await new ListProcedureInstancesHandler(_repo)
            .HandleAsync(instance.TenantId, TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;

        AssertFirmado(fila);
    }

    [Theory]
    [MemberData(nameof(Propietarios))]
    public async Task ListadoFiltrado_AprobacionManual_DiceFirmado(int vendedores)
    {
        var instance = Traspaso(vendedores);
        Preparar(instance);

        var (items, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = instance.TenantId }, TestContext.Current.CancellationToken);

        AssertFirmado(items.Should().ContainSingle().Subject);
    }

    /// <summary>
    /// Misma regresión con aprobación AUTOMÁTICA (proveedor, sin <c>SellarAprobacionManual</c>): el desplegable de
    /// copropietarios también debe decir «Firmado» en la ruta filtrada.
    /// </summary>
    [Theory]
    [MemberData(nameof(Propietarios))]
    public async Task ListadoFiltrado_AprobacionAutomatica_DiceFirmado(int vendedores)
    {
        var instance = Traspaso(vendedores, manual: false);
        Preparar(instance);

        var (items, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = instance.TenantId }, TestContext.Current.CancellationToken);

        AssertFirmado(items.Should().ContainSingle().Subject);
    }

    private static void AssertFirmado(InstanceSummaryDto fila)
    {
        fila.FirmaVendedorEstado.Should().Be(FirmaParteEstados.Firmado, "columna del vendedor");
        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado, "columna del comprador");
        fila.VendedoresActores!.Select(a => a.FirmaEstado).Should().AllBe(FirmaParteEstados.Firmado, "desplegable de vendedores");
        fila.CompradoresActores!.Select(a => a.FirmaEstado).Should().AllBe(FirmaParteEstados.Firmado, "desplegable de compradores");
    }

    private void Preparar(ProcedureInstance instance)
    {
        var now = DateTimeOffset.UtcNow;
        // Lo que devolvería el repositorio real: las llaves de las filas aprobadas y vigentes del tenant.
        var keys = instance.BiometricValidations
            .Where(v => BiometricRules.EsAprobadaVigente(v, now))
            .Select(v => BiometricRules.IdentidadKey(v.TenantId, v.DocumentType, v.DocumentNumber))
            .ToHashSet(StringComparer.Ordinal);

        _repo.ListWithSummaryGraphAsync(instance.TenantId, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([instance]);
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstance>)[instance], 1));
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(keys);
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>());
    }

    private static ProcedureInstance Traspaso(int vendedores, bool manual = true)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("traspaso"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT1-0000522",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        for (var i = 1; i <= vendedores; i++)
            AgregarParte(instance, "vendedor", i, $"9853247{i}", manual);
        AgregarParte(instance, "comprador", 1, "1018000001", manual);
        return instance;
    }

    /// <summary>Actor + su validación aprobada por el flujo MANUAL (misma secuencia que AprobarValidacionManualHandler).</summary>
    private static void AgregarParte(ProcedureInstance instance, string rol, int ordinal, string documento, bool manual)
    {
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = $"{rol} {ordinal}",
            Ordinal = ordinal,
        });

        var now = DateTimeOffset.UtcNow;
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = rol,
            DocumentType = "CC",
            DocumentNumber = documento,
            Name = $"{rol} {ordinal}",
            Provider = BiometricProviders.Manual,
            Status = BiometricEstados.PendienteRevisionManual,
            CreatedAt = now.AddHours(-1),
        };
        v.Approve(now);
        if (manual)
            v.SellarAprobacionManual(Guid.NewGuid(), now);
        instance.BiometricValidations.Add(v);
    }
}
