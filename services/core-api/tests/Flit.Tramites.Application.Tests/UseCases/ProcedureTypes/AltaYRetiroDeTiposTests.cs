using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureTypes;

/// <summary>
/// ADR-0050 — alta y retiro de tipos desde el configurador.
///
/// <para>El código es la llave con la que el tipo viaja a ICT, a Quipux y a los snapshots congelados
/// de cada expediente, así que su forma y su unicidad se validan antes de crear nada: un espacio o
/// un acento se convierten después en un fallo silencioso de emparejamiento.</para>
///
/// <para>«Eliminar» ARCHIVA, no borra: un tipo con trámites no se puede retirar porque quedarían
/// apuntando a un tipo archivado.</para>
/// </summary>
public sealed class AltaYRetiroDeTiposTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();

    // ── Alta ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("CAMBIO COLOR")]       // espacio
    [InlineData("CAMBIO-COLOR")]       // guion
    [InlineData("AB")]                 // demasiado corto
    [InlineData("1BLINDAJE")]          // empieza por dígito
    [InlineData("BLINDAJÉ")]           // acento
    public async Task UnCodigoMalFormadoSeRechazaAntesDeCrear(string code)
    {
        var (result, error) = await new CreateProcedureTypeHandler(_repo).HandleAsync(
            new CreateProcedureTypeRequest("OTROS", code, "Nombre", null),
            TestContext.Current.CancellationToken);

        error.Should().Be("invalid_code");
        result.Should().BeNull();
        await _repo.DidNotReceive().AddAsync(Arg.Any<ProcedureType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElCodigoSeNormalizaYSeGuardaEnMayusculas()
    {
        // Escribirlo en minúsculas no es un error: se normaliza. Lo que no se tolera es una forma
        // que el emparejamiento con las integraciones no pueda reproducir.
        _repo.CodeExistsAsync("BLINDAJE", Arg.Any<CancellationToken>()).Returns(false);

        var (result, error) = await new CreateProcedureTypeHandler(_repo).HandleAsync(
            new CreateProcedureTypeRequest(" otros ", "  blindaje  ", "  Blindaje  ", null),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Code.Should().Be("BLINDAJE");
        result.Family.Should().Be("OTROS");
        result.Name.Should().Be("Blindaje");
    }

    [Fact]
    public async Task UnCodigoYaOcupadoSeRechazaConSuPropioMotivo()
    {
        // El UNIQUE de la base lo rechazaría igual, pero con un 500 del constraint.
        _repo.CodeExistsAsync("BLINDAJE", Arg.Any<CancellationToken>()).Returns(true);

        var (_, error) = await new CreateProcedureTypeHandler(_repo).HandleAsync(
            new CreateProcedureTypeRequest("OTROS", "BLINDAJE", "Blindaje", null),
            TestContext.Current.CancellationToken);

        error.Should().Be("code_taken");
        await _repo.DidNotReceive().AddAsync(Arg.Any<ProcedureType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnaFamiliaFueraDelDominioSeRechaza()
    {
        var (_, error) = await new CreateProcedureTypeHandler(_repo).HandleAsync(
            new CreateProcedureTypeRequest("VEHICULAR", "BLINDAJE", "Blindaje", null),
            TestContext.Current.CancellationToken);

        error.Should().Be("invalid_family");
    }

    [Fact]
    public async Task NaceEnBorradorYConLaBarreraApagada()
    {
        // Un tipo recién creado no tiene recorrido: ofrecerlo al gestor sería prometer un asistente
        // vacío.
        ProcedureType? guardado = null;
        await _repo.AddAsync(Arg.Do<ProcedureType>(t => guardado = t), Arg.Any<CancellationToken>());
        _repo.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var (result, error) = await new CreateProcedureTypeHandler(_repo).HandleAsync(
            new CreateProcedureTypeRequest("OTROS", "NUEVO_TRAMITE", "Nuevo trámite", null),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.PublicationStatus.Should().Be(PublicationStatus.Draft);
        result.WizardEnabled.Should().BeFalse();
        guardado!.WizardEnabled.Should().BeFalse();
    }

    // ── Retiro ──────────────────────────────────────────────────────────────

    private ProcedureType Existente(bool conTramites)
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "BLINDAJE",
            Name = "Blindaje",
            Family = "OTROS",
            PublicationStatus = PublicationStatus.Published,
            IsActive = true,
            WizardEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdAsync(tipo.Id, Arg.Any<CancellationToken>()).Returns(tipo);
        _repo.HasInstancesAsync(tipo.Id, Arg.Any<CancellationToken>()).Returns(conTramites);
        return tipo;
    }

    [Fact]
    public async Task UnTipoConTramitesNoSePuedeRetirar()
    {
        var tipo = Existente(conTramites: true);

        var error = await new DeleteProcedureTypeHandler(_repo)
            .HandleAsync(tipo.Id, TestContext.Current.CancellationToken);

        error.Should().Be("conflict");
        tipo.PublicationStatus.Should().Be(PublicationStatus.Published, "no se toca nada");
        tipo.WizardEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task RetirarArchivaYApagaLaBarrera()
    {
        // Dejar la barrera encendida en un archivado es una incoherencia latente: si alguien lo
        // republicara volvería a ofrecerse sin que nadie lo hubiera decidido.
        var tipo = Existente(conTramites: false);

        var error = await new DeleteProcedureTypeHandler(_repo)
            .HandleAsync(tipo.Id, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        tipo.PublicationStatus.Should().Be(PublicationStatus.Archived);
        tipo.WizardEnabled.Should().BeFalse();
    }
}
