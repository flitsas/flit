using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Migración V1→V2 — un trámite MIGRADO (is_migrated) en estado terminal es una FOTO de solo
/// lectura: el wizard reporta todos los pasos como <c>complete</c> sin someterlo al gating vivo
/// (que exige comercial/biométrica/FUR inexistentes en la foto). El bypass exige AMBAS condiciones
/// (migrado + terminal): un migrado en borrador, o un nativo aprobado, siguen el gating normal.
/// </summary>
public sealed class WizardMigradoReadonlyTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetWizardStateHandler _handler;

    public WizardMigradoReadonlyTests()
    {
        _handler = new GetWizardStateHandler(_repo);
    }

    // Instancia "pelada": sin actores, comercial, biométrica ni FUR. Un trámite vivo así tendría los
    // pasos posteriores a la consulta en incomplete/locked; el bypass de foto los fuerza a complete.
    private static ProcedureInstance Traspaso(string status, bool isMigrated) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "MIG-TR-24860",
            Status = status,
            IsMigrated = isMigrated,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void Setup(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);

    [Fact]
    public async Task MigradoTerminal_SinDatos_TodosLosPasosComplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(TramiteEstado.Aprobado, isMigrated: true);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result.Should().NotBeNull();
        result!.TotalSteps.Should().Be(6);
        result.Steps.Should().OnlyContain(s => s.Status == "complete");
        result.Steps.Should().OnlyContain(s => s.Reasons.Count == 0);
        // Terminal ⇒ sin acciones: no se puede radicar y no hay blockers pendientes.
        result.CanSubmit.Should().BeFalse();
        result.Blockers.Should().BeEmpty();
        // El estado de negocio y la ausencia de transiciones se preservan (aprobado es final).
        result.Status.Should().Be(TramiteEstado.Aprobado);
        result.AllowedTransitions.Should().BeEmpty();
    }

    [Fact]
    public async Task MigradoAnulado_TambienEsFotoCompleta()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(TramiteEstado.Anulado, isMigrated: true);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Should().OnlyContain(s => s.Status == "complete");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task MigradoEnBorrador_NoSeSaltaElGating()
    {
        var ct = TestContext.Current.CancellationToken;
        // Borrador NO es terminal ⇒ el bypass no aplica; sin datos, los pasos posteriores no están completos.
        var instance = Traspaso(TramiteEstado.Borrador, isMigrated: true);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Should().NotBeEmpty();
        result.Steps.Should().Contain(s => s.Status != "complete");
    }

    [Fact]
    public async Task ElOrigenMigradoViajaAlWizard_EnCualquierEstado()
    {
        var ct = TestContext.Current.CancellationToken;
        // El frontend lo necesita en BORRADOR —no solo en la foto terminal— para explicar en el
        // pre-vuelo por qué las consultas de RUNT/SIMIT llegan sin hacer: no se migran porque caducan.
        Setup(Traspaso(TramiteEstado.Borrador, isMigrated: true));
        var (migrado, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        Setup(Traspaso(TramiteEstado.Borrador, isMigrated: false));
        var (nativo, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        migrado!.EsMigrado.Should().BeTrue();
        nativo!.EsMigrado.Should().BeFalse();
    }

    [Fact]
    public async Task NativoAprobado_SinDatos_NoSeFuerzaCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        // Aprobado pero NO migrado: el bypass exige is_migrated; sin datos, no todos los pasos están completos.
        var instance = Traspaso(TramiteEstado.Aprobado, isMigrated: false);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Should().Contain(s => s.Status != "complete");
    }
}
