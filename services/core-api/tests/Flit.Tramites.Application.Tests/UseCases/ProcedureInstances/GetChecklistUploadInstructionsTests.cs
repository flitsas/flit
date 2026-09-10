using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12066 (Feature #12064) — la instrucción de cargue del catálogo viaja hasta el ítem del
/// checklist, que es lo que el gestor lee en la tarjeta del paso Requisitos. Viaja por el carril
/// que ya existía para los límites por tipo (<see cref="DocumentTypeRule"/>), así que un tipo sin
/// texto configurado deja el ítem exactamente como estaba antes del cambio.
/// </summary>
public sealed class GetChecklistUploadInstructionsTests
{
    private const string InstruccionPazSalvo =
        "Sube el Paz y Salvo de impuestos vehiculares expedido por la Secretaría de Hacienda.";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IChecklistCompanyParamsProvider _companyParams = Substitute.For<IChecklistCompanyParamsProvider>();

    public GetChecklistUploadInstructionsTests() =>
        _companyParams.GetForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CompanyDocumentParam>>([]));

    /// <summary>Catálogo en memoria: solo los tipos que el test declara tienen regla.</summary>
    private sealed class FakeCatalog(params DocumentTypeRule[] rules) : IDocumentTypeCatalog
    {
        private readonly Dictionary<string, DocumentTypeRule> _rules =
            rules.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);

        public Task<DocumentTypeRule?> GetRuleAsync(string tipo, CancellationToken ct = default) =>
            Task.FromResult(_rules.GetValueOrDefault(tipo));

        public Task<IReadOnlySet<string>> ListSystemGeneratedCodesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static ProcedureInstance Traspaso(Guid id, Guid tenant) => new()
    {
        ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso"),
        Id = id,
        TenantId = tenant,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000042",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<ChecklistResponse> RunAsync(IDocumentTypeCatalog? catalog)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithChecklistGraphAsync(id, tenant, ct).Returns(Traspaso(id, tenant));

        var handler = new GetChecklistHandler(_repo, _companyParams, documentTypes: catalog);
        var (result, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        return result!;
    }

    /// <summary>AC1 — el ítem llega con el texto que el administrador configuró para su tipo.</summary>
    [Fact]
    public async Task Checklist_ConInstruccionEnCatalogo_LaEntregaEnElItem()
    {
        var result = await RunAsync(new FakeCatalog(
            new DocumentTypeRule("paz_salvo", [], 0, InstruccionPazSalvo)));

        result.Items.Should().Contain(i => i.DocTipo == "paz_salvo")
            .Which.InstruccionCargue.Should().Be(InstruccionPazSalvo);
    }

    /// <summary>
    /// AC2 — un tipo sin texto configurado llega sin instrucción. Es el caso de la mayoría del
    /// catálogo el día del despliegue, y la tarjeta debe verse como hasta ahora.
    /// </summary>
    [Fact]
    public async Task Checklist_TipoSinInstruccionConfigurada_DejaElItemSinInstruccion()
    {
        // El tipo existe en el catálogo (tiene límites) pero nadie le escribió el texto todavía.
        var result = await RunAsync(new FakeCatalog(
            new DocumentTypeRule("paz_salvo", ["application/pdf"], 20L * 1024 * 1024)));

        result.Items.Should().Contain(i => i.DocTipo == "paz_salvo")
            .Which.InstruccionCargue.Should().BeNull();
    }

    /// <summary>
    /// AC5 — sin catálogo inyectado el checklist es idéntico al de antes del cambio: mismos
    /// documentos, misma obligatoriedad y ninguna instrucción inventada.
    /// </summary>
    [Fact]
    public async Task Checklist_SinCatalogo_NoInventaInstrucciones()
    {
        var result = await RunAsync(catalog: null);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(i => i.InstruccionCargue == null);
    }

    /// <summary>
    /// La instrucción se resuelve por tipo, no se contagia entre casillas: dos documentos del
    /// mismo checklist con textos distintos conservan cada uno el suyo.
    /// </summary>
    [Fact]
    public async Task Checklist_InstruccionEsPorTipo_NoSeContagiaEntreDocumentos()
    {
        const string InstruccionCompraventa =
            "Adjunta el contrato o formato de compraventa debidamente firmado para el traspaso.";

        var result = await RunAsync(new FakeCatalog(
            new DocumentTypeRule("paz_salvo", [], 0, InstruccionPazSalvo),
            new DocumentTypeRule("compraventa", [], 0, InstruccionCompraventa)));

        result.Items.Should().Contain(i => i.DocTipo == "paz_salvo")
            .Which.InstruccionCargue.Should().Be(InstruccionPazSalvo);
        result.Items.Should().Contain(i => i.DocTipo == "compraventa")
            .Which.InstruccionCargue.Should().Be(InstruccionCompraventa);
    }

    /// <summary>
    /// Cambiar el texto en el módulo documental se refleja en la siguiente consulta: el handler
    /// lee el catálogo en cada llamada, no cachea el texto en la instancia (AC4).
    /// </summary>
    [Fact]
    public async Task Checklist_TextoNuevoEnCatalogo_SeReflejaEnLaSiguienteConsulta()
    {
        var antes = await RunAsync(new FakeCatalog(
            new DocumentTypeRule("paz_salvo", [], 0, "Texto viejo.")));
        antes.Items.Should().Contain(i => i.DocTipo == "paz_salvo")
            .Which.InstruccionCargue.Should().Be("Texto viejo.");

        var despues = await RunAsync(new FakeCatalog(
            new DocumentTypeRule("paz_salvo", [], 0, "Texto nuevo del administrador.")));
        despues.Items.Should().Contain(i => i.DocTipo == "paz_salvo")
            .Which.InstruccionCargue.Should().Be("Texto nuevo del administrador.");
    }
}
