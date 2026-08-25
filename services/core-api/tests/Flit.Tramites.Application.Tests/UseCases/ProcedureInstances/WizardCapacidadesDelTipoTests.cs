using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0050 — el estado del asistente publica las CAPACIDADES del tipo, además de sus pasos.
/// <para>Sin ellas el frontend solo tenía la familia y seguía decidiendo con
/// <c>modalidad === 'traspaso'</c>: qué partes capturar, si mostrar datos comerciales, si la prenda
/// bloquea y por qué identificador entra el vehículo. Con dos modalidades esas dos ramas agotaban el
/// catálogo; con veintiún tipos, un trámite de la familia OTROS se podía elegir y dibujar pero por
/// dentro se comportaba como una matrícula.</para>
/// </summary>
public sealed class WizardCapacidadesDelTipoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeSnapshotRepository _snapshots = Substitute.For<IProcedureTypeSnapshotRepository>();

    private static ProcedureInstance Instancia(ProcedureType tipo) => new()
    {
        ProcedureType = tipo,
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = tipo.Id,
        ReferenceNumber = "TRM-2026-000123",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<WizardStateDto> EstadoDe(ProcedureType tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(tipo);
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);

        var handler = new GetWizardStateHandler(_repo, snapshotRepo: _snapshots);
        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        return result!;
    }

    [Fact]
    public async Task ElAsistenteRecibeElNombreDelTipo()
    {
        // El título del asistente era uno de dos literales: un blindaje se anunciaba al gestor como
        // «Matrícula Inicial».
        var state = await EstadoDe(ProcedureTypeFixture.Traspaso);

        state.TypeName.Should().Be("Traspaso");
    }

    [Fact]
    public async Task ElTraspasoDeclaraSusDosPartesYSuValorComercial()
    {
        var state = await EstadoDe(ProcedureTypeFixture.Traspaso);

        state.Capabilities.Should().NotBeNull();
        state.Capabilities!.RequiresSeller.Should().BeTrue();
        state.Capabilities.RequiresBuyer.Should().BeTrue();
        state.Capabilities.RequiresCommercialValue.Should().BeTrue();
        state.Capabilities.EntryMode.Should().Be("PLATE");
        state.Capabilities.BiometricActors.Should().Contain("OWNER");
    }

    [Fact]
    public async Task LaMatriculaEntraPorVinYNoTieneParteVendedora()
    {
        var state = await EstadoDe(ProcedureTypeFixture.Matricula);

        state.Capabilities!.EntryMode.Should().Be("VIN");
        state.Capabilities.RequiresSeller.Should().BeFalse();
        state.Capabilities.RequiresCommercialValue.Should().BeFalse();
        state.Capabilities.BiometricActors.Should().NotContain("OWNER");
    }

    [Fact]
    public async Task LasCapacidadesSalenDelMismoPerfilQueGobiernaLosGates()
    {
        // No es una segunda fuente: si el asistente y el servidor pudieran discrepar, el gestor vería
        // un formulario que el backend luego rechaza.
        var tipo = ProcedureTypeFixture.Traspaso;
        var perfil = Flit.Tramites.Domain.Tramites.Services.ProcedureTypeGateProfile.FromJson(tipo.GateProfile);
        var state = await EstadoDe(tipo);

        state.Capabilities!.RequiresSeller.Should().Be(perfil.RequiresSeller);
        state.Capabilities.RequiresBuyer.Should().Be(perfil.RequiresBuyer);
        state.Capabilities.RequiresCommercialValue.Should().Be(perfil.RequiresCommercialValue);
        state.Capabilities.HasPrendaGate.Should().Be(perfil.HasPrendaGate);
        state.Capabilities.EntryMode.Should().Be(perfil.EntryMode);
    }

    [Fact]
    public async Task LasCapacidadesNoExponenLasValidacionesDelServidor()
    {
        // La proyección es parcial a propósito: publicar `validateOtOperability` o `simitMode`
        // invitaría al frontend a reimplementar un gate que solo el backend puede resolver.
        var state = await EstadoDe(ProcedureTypeFixture.Traspaso);

        typeof(WizardCapabilitiesDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(["ValidateOtOperability", "SimitMode", "ValidateDuplicateProcedure"]);
        state.Capabilities.Should().NotBeNull();
    }
}
