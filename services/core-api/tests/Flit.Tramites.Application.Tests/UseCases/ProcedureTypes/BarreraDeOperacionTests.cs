using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureTypes;

/// <summary>
/// ADR-0050 — <c>wizard_enabled</c> es la barrera que convierte «habilitar un trámite» en
/// configuración y no en un despliegue.
/// <para>Nació sin forma de moverse: el PUT del tipo no la llevaba y además rechaza los publicados
/// —y los 21 del catálogo lo están—, así que la barrera era un interruptor sin manija y ningún tipo
/// podía operarse. Estas pruebas fijan la palanca y sus condiciones.</para>
/// </summary>
public sealed class BarreraDeOperacionTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();
    private readonly IProcedureTypeValidator _validator = new ProcedureTypeValidator();

    private static ProcedureType Tipo(
        bool publicado = true,
        bool activo = true,
        bool conPasos = true,
        bool habilitado = false)
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "BLINDAJE",
            Name = "Blindaje",
            Family = "OTROS",
            GateProfile = """{"entryMode":"PLATE","requiresBuyer":true}""",
            PublicationStatus = publicado ? PublicationStatus.Published : PublicationStatus.Draft,
            IsActive = activo,
            WizardEnabled = habilitado,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (conPasos)
        {
            var paso = new ProcedureStep { Id = Guid.NewGuid(), Code = "consulta", ProcedureTypeId = tipo.Id };
            paso.Sections.Add(new ProcedureSection
            {
                Id = Guid.NewGuid(),
                Code = "vehiculo",
                SectionType = ProcedureSectionTypes.VehicleQuery,
            });
            tipo.Steps.Add(paso);
        }

        return tipo;
    }

    private SetWizardEnabledHandler Handler(ProcedureType tipo)
    {
        _repo.GetByIdWithDetailsAsync(tipo.Id, Arg.Any<CancellationToken>()).Returns(tipo);
        return new SetWizardEnabledHandler(_repo, _validator);
    }

    [Fact]
    public async Task UnTipoPublicadoYParametrizadoSePuedeHabilitar()
    {
        // Es lo que no se podía hacer: el PUT del tipo rechaza los publicados, y todos lo están.
        var tipo = Tipo();
        var (result, error, _) = await Handler(tipo)
            .HandleAsync(tipo.Id, enabled: true, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.WizardEnabled.Should().BeTrue();
        tipo.WizardEnabled.Should().BeTrue();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnTipoSinPasosNoSePuedeHabilitar_YSeDiceQueLeFalta()
    {
        // Habilitarlo dejaría al gestor frente a un asistente vacío y bloqueado.
        var tipo = Tipo(conPasos: false);
        var (result, error, detail) = await Handler(tipo)
            .HandleAsync(tipo.Id, enabled: true, TestContext.Current.CancellationToken);

        error.Should().Be(SetWizardEnabledHandler.NotReady);
        result.Should().BeNull();
        detail.Should().NotBeNull();
        tipo.WizardEnabled.Should().BeFalse();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnTipoSinPublicarNoSePuedeHabilitar()
    {
        var tipo = Tipo(publicado: false);
        var (_, error, _) = await Handler(tipo)
            .HandleAsync(tipo.Id, enabled: true, TestContext.Current.CancellationToken);

        error.Should().Be(SetWizardEnabledHandler.NotReady);
    }

    [Fact]
    public async Task UnTipoInactivoNoSePuedeHabilitar()
    {
        var tipo = Tipo(activo: false);
        var (_, error, _) = await Handler(tipo)
            .HandleAsync(tipo.Id, enabled: true, TestContext.Current.CancellationToken);

        error.Should().Be(SetWizardEnabledHandler.NotReady);
    }

    [Fact]
    public async Task ApagarNoExigeNada()
    {
        // La palanca de seguridad tiene que poder accionarse siempre: un tipo que resultó estar mal
        // parametrizado es justo el que hay que poder apagar, y es el que no pasaría el validador.
        var tipo = Tipo(conPasos: false, habilitado: true);
        var (result, error, _) = await Handler(tipo)
            .HandleAsync(tipo.Id, enabled: false, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.WizardEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task PedirLoQueYaEsNoEsUnError()
    {
        var tipo = Tipo(habilitado: true);
        var (result, error, _) = await Handler(tipo)
            .HandleAsync(tipo.Id, enabled: true, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.WizardEnabled.Should().BeTrue();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnTipoQueNoExisteSeReporta()
    {
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureType?)null);

        var (_, error, _) = await new SetWizardEnabledHandler(_repo, _validator)
            .HandleAsync(Guid.NewGuid(), enabled: true, TestContext.Current.CancellationToken);

        error.Should().Be("not_found");
    }
}
