using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureTypes;

/// <summary>
/// ADR-0050 — <c>section_type</c> en el configurador de pasos.
/// <para>Antes, <c>ProcedureSectionInput</c> no exponía el campo y <c>ReplaceStepsAsync</c> borra y
/// recrea: el primer <c>PUT /steps</c> desde la UI degradaba a <c>generic_form</c> las secciones
/// tipadas del seed F08 (PRENDA_INSCRIPCION, CAMBIO_LOCATARIO), dejándolas sin renderer ni gate.
/// Estos tests fijan la precedencia entrante &gt; almacenado &gt; default.</para>
/// </summary>
public sealed class UpsertProcedureStepsSectionTypeTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();

    private static readonly Guid TypeId = Guid.NewGuid();

    private static ProcedureType Tipo() => new()
    {
        Id = TypeId,
        Code = "PRENDA_INSCRIPCION",
        Name = "Inscripción de prenda",
        Family = ProcedureFamilyCodes.Otros,
        PublicationStatus = PublicationStatus.Draft,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Estado almacenado: un paso con una sección tipada <c>prenda_decision</c>.</summary>
    private static List<ProcedureStep> ExistentesTipados() =>
    [
        new()
        {
            Id = Guid.NewGuid(),
            ProcedureTypeId = TypeId,
            Code = "PRENDA",
            Title = "Prenda",
            SortOrder = 1,
            IsActive = true,
            Sections =
            [
                new ProcedureSection
                {
                    Id = Guid.NewGuid(),
                    Code = "DECISION",
                    Title = "Decisión de prenda",
                    SortOrder = 1,
                    SectionType = ProcedureSectionTypes.PrendaDecision,
                    FormFields = [],
                },
            ],
        },
    ];

    private static List<ProcedureStepInput> Entrada(string? sectionType) =>
    [
        new("PRENDA", "Prenda", 1, true,
        [
            new ProcedureSectionInput("DECISION", "Decisión de prenda", 1, "single", [], sectionType),
        ]),
    ];

    private async Task<List<ProcedureStep>> EjecutarAsync(List<ProcedureStepInput> entrada)
    {
        _repo.GetByIdAsync(TypeId, Arg.Any<CancellationToken>()).Returns(Tipo());
        _repo.GetStepsWithDetailsAsync(TypeId, Arg.Any<CancellationToken>()).Returns(ExistentesTipados());

        List<ProcedureStep>? guardados = null;
        await _repo.ReplaceStepsAsync(TypeId, Arg.Do<List<ProcedureStep>>(x => guardados = x), Arg.Any<CancellationToken>());

        var handler = new UpsertProcedureStepsHandler(_repo);
        var (result, error, _) = await handler.HandleAsync(TypeId, entrada, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        guardados.Should().NotBeNull();
        return guardados!;
    }

    [Fact]
    public async Task SinSectionTypeEnLaEntrada_ConservaElAlmacenado()
    {
        // El caso real: el cliente actual no envía el campo. Antes esto lo degradaba a generic_form.
        var guardados = await EjecutarAsync(Entrada(sectionType: null));

        guardados[0].Sections.Single().SectionType.Should().Be(ProcedureSectionTypes.PrendaDecision);
    }

    [Fact]
    public async Task ConSectionTypeValido_LoAplica()
    {
        var guardados = await EjecutarAsync(Entrada(ProcedureSectionTypes.DocumentChecklist));

        guardados[0].Sections.Single().SectionType.Should().Be(ProcedureSectionTypes.DocumentChecklist);
    }

    [Fact]
    public async Task ConSectionTypeFueraDelCatalogo_LoIgnoraYConservaElAlmacenado()
    {
        // El CHECK del DDL lo rechazaría; descartarlo preserva la sección en vez de romper el guardado.
        var guardados = await EjecutarAsync(Entrada("no_existe"));

        guardados[0].Sections.Single().SectionType.Should().Be(ProcedureSectionTypes.PrendaDecision);
    }

    [Fact]
    public async Task SeccionNueva_SinSectionType_CaeAGenericForm()
    {
        var entrada = new List<ProcedureStepInput>
        {
            new("PRENDA", "Prenda", 1, true,
            [
                new ProcedureSectionInput("OTRA", "Sección nueva", 2, "single", []),
            ]),
        };

        var guardados = await EjecutarAsync(entrada);

        guardados[0].Sections.Single().SectionType.Should().Be(ProcedureSectionTypes.GenericForm);
    }

    [Fact]
    public async Task ElDtoDeSalidaExponeElSectionType()
    {
        _repo.GetByIdAsync(TypeId, Arg.Any<CancellationToken>()).Returns(Tipo());
        _repo.GetStepsWithDetailsAsync(TypeId, Arg.Any<CancellationToken>()).Returns(ExistentesTipados());

        var handler = new UpsertProcedureStepsHandler(_repo);
        var (result, _, _) = await handler.HandleAsync(TypeId, Entrada(sectionType: null), TestContext.Current.CancellationToken);

        result!.Single().Sections.Single().SectionType.Should().Be(ProcedureSectionTypes.PrendaDecision);
    }
}
