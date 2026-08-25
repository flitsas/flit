using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureTypes;

/// <summary>
/// ADR-0050 — la identidad de un tipo PUBLICADO se puede corregir desde el configurador.
///
/// <para>El nombre no es cosmético: es el rótulo legal del mandato y de la portada del expediente.
/// La familia gobierna clasificación, filtros, causales de rechazo y el bloqueo por compañía, así
/// que un tipo mal clasificado —una cancelación de matrícula con recorrido de OTROS— solo se puede
/// arreglar reclasificándolo.</para>
/// </summary>
public sealed class CorregirTipoPublicadoTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();

    private ProcedureType Tipo(string estado = PublicationStatus.Published)
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "CANCELACION_MATRICULA",
            Name = "Cancelación de matrícula",
            Family = "MATRICULAS",
            PublicationStatus = estado,
            IsActive = true,
            Version = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdAsync(tipo.Id, Arg.Any<CancellationToken>()).Returns(tipo);
        return tipo;
    }

    [Fact]
    public async Task SeReclasificaLaFamiliaYSubeLaVersion()
    {
        var tipo = Tipo();
        var sut = new UpdateProcedureTypeHandler(_repo);

        var (result, error) = await sut.HandleAsync(
            tipo.Id,
            new UpdateProcedureTypeRequest("Cancelación de matrícula", null, true, Family: "OTROS"),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Family.Should().Be("OTROS");
        tipo.Version.Should().Be(4);
    }

    [Fact]
    public async Task UnaFamiliaFueraDelDominioSeRechaza()
    {
        // Fuera del dominio el CHECK del DDL lo rechazaría con un error mucho menos legible.
        var tipo = Tipo();
        var sut = new UpdateProcedureTypeHandler(_repo);

        var (_, error) = await sut.HandleAsync(
            tipo.Id,
            new UpdateProcedureTypeRequest("X", null, true, Family: "VEHICULAR"),
            TestContext.Current.CancellationToken);

        error.Should().Be("invalid_family");
        tipo.Family.Should().Be("MATRICULAS", "no se persiste un valor inválido");
    }

    [Fact]
    public async Task SinFamiliaEnLaPeticion_LaConservaYNoRompeAClientesAnteriores()
    {
        var tipo = Tipo();
        var sut = new UpdateProcedureTypeHandler(_repo);

        var (result, error) = await sut.HandleAsync(
            tipo.Id,
            new UpdateProcedureTypeRequest("Nombre corregido", "desc", true),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Family.Should().Be("MATRICULAS");
        result.Name.Should().Be("Nombre corregido");
    }

    [Fact]
    public async Task UnTipoArchivadoNoSeCorrige()
    {
        var tipo = Tipo(PublicationStatus.Archived);
        var sut = new UpdateProcedureTypeHandler(_repo);

        var (_, error) = await sut.HandleAsync(
            tipo.Id,
            new UpdateProcedureTypeRequest("X", null, true),
            TestContext.Current.CancellationToken);

        error.Should().Be("conflict");
    }

    [Fact]
    public async Task UnBorradorNoSubeLaVersion()
    {
        var tipo = Tipo(PublicationStatus.Draft);
        var sut = new UpdateProcedureTypeHandler(_repo);

        var (_, error) = await sut.HandleAsync(
            tipo.Id,
            new UpdateProcedureTypeRequest("X", null, true),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        tipo.Version.Should().Be(3);
    }
}
