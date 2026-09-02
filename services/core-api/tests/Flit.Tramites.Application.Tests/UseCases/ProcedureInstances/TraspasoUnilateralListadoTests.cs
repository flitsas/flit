using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0051 — el listado de operación deja de clasificar por FAMILIA. Con el criterio anterior,
/// <c>TRASPASO_UNILATERAL</c> heredaba las dos partes de un traspaso estándar y la fila mentía tres
/// veces sobre el mismo trámite: pedía identidad de un comprador que no comparece, lo mostraba
/// «pendiente» para siempre en la columna Firmado, y reclamaba la firma de una compraventa que ese
/// tipo no genera. Cada prueba fija una de las tres, con el traspaso estándar como control.
/// </summary>
public sealed class TraspasoUnilateralListadoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    [Fact]
    public async Task Unilateral_NoReclamaLaFirmaDeUnaCompraventaQueNoExiste()
    {
        var fila = await FilaAsync(ProcedureTypeFixture.TraspasoUnilateral);

        // El locatario ya tenía el vehículo por el contrato de leasing: no hay compraventa que firmar.
        fila.SignaturePending.Should().BeFalse();
    }

    [Fact]
    public async Task TraspasoStandard_SigueReclamandoLaFirmaDeLaCompraventa()
    {
        // Control: el tipo que sí autogenera compraventa no cambia de conducta.
        var fila = await FilaAsync(ProcedureTypeFixture.Traspaso);

        fila.SignaturePending.Should().BeTrue();
    }

    [Fact]
    public async Task Unilateral_LaColumnaFirmadoNoAcreditaAUnaParteQueNoInterviene()
    {
        var fila = await FilaAsync(ProcedureTypeFixture.TraspasoUnilateral);

        // `null` = NO APLICA. Antes salía "pendiente" a perpetuidad, porque el corte por familia solo
        // contemplaba al vendedor y daba al comprador por presente siempre.
        fila.FirmaCompradorEstado.Should().BeNull();
        fila.FirmaVendedorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task TraspasoStandard_AcreditaALasDosPartes()
    {
        var fila = await FilaAsync(ProcedureTypeFixture.Traspaso);

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
        fila.FirmaVendedorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task Unilateral_ConLaIdentidadDelPropietarioAprobada_ElChipQuedaAprobado()
    {
        // La parte requerida es SOLO el propietario. Exigirle también al comprador dejaba el chip
        // clavado aunque el único que interviene ya hubiera validado.
        var fila = await FilaAsync(ProcedureTypeFixture.TraspasoUnilateral, identidadAprobada: ["vendedor"]);

        fila.IdentityValidationStatus.Should().Be(BiometricEstados.Aprobado);
        fila.FirmaVendedorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task TraspasoStandard_ConSoloElVendedorAprobado_NoBastaParaElChip()
    {
        // Control: donde sí intervienen dos partes, una sola aprobada no cierra el chip.
        var fila = await FilaAsync(ProcedureTypeFixture.Traspaso, identidadAprobada: ["vendedor"]);

        fila.IdentityValidationStatus.Should().NotBe(BiometricEstados.Aprobado);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<InstanceSummaryDto> FilaAsync(
        ProcedureType tipo, IReadOnlyCollection<string>? identidadAprobada = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Tramite(tipo);
        Preparar(instance, identidadAprobada ?? []);

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(instance.TenantId, ct);

        return filas.Should().ContainSingle().Subject;
    }

    private void Preparar(ProcedureInstance instance, IReadOnlyCollection<string> identidadAprobada)
    {
        _repo.ListWithSummaryGraphAsync(
                instance.TenantId, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([instance]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>(StringComparer.Ordinal));

        // La identidad aprobada se resuelve por PERSONA: la llave canónica del actor de esa parte.
        var claves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parte in identidadAprobada)
        {
            var actor = instance.Actors.First(a =>
                string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
            claves.Add(BiometricRules.IdentidadKey(
                instance.TenantId, actor.DocumentType, actor.DocumentNumber));
        }

        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(claves);
    }

    /// <summary>Borrador con las dos partes persistidas y sin ninguna firma de compraventa.</summary>
    private static ProcedureInstance Tramite(ProcedureType tipo)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = tipo,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = tipo.Id,
            ReferenceNumber = "TRM-2026-000042",
            Status = TramiteEstado.Borrador,
            Plate = "ABC123",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // El vendedor del unilateral existe igual que el del estándar: lo que cambia es que llega
        // sincronizado desde RUES/RUNT, no tecleado. Para el listado es una fila de actor más.
        instance.Actors.Add(Actor("vendedor", "NIT", "900123456", "Leasing S.A."));
        instance.Actors.Add(Actor("comprador", "CC", "1020304050", "Ana Locataria"));

        return instance;
    }

    private static ProcedureInstanceActor Actor(
        string parte, string tipoDoc, string documento, string nombre) =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = parte,
            DocumentType = tipoDoc,
            DocumentNumber = documento,
            FullName = nombre,
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
