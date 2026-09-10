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
/// HU #10522 (RF17/RF22 — matriz viva) — el gestor manda también en los GATES (no solo en el
/// display): completitud documental resuelta desde la matriz (<see cref="ChecklistMatrixCompleteness"/>)
/// e inyectada como override en <see cref="SubmitGate"/> / <see cref="FinalizeDraftGate"/>. Con el
/// flag OFF / sin matriz el override es <c>null</c> ⇒ gate actual (sin regresión).
/// </summary>
public sealed class ChecklistMatrixGateTests
{
    private readonly IChecklistCompanyParamsProvider _companyParams = Substitute.For<IChecklistCompanyParamsProvider>();
    private readonly IResolvedChecklistMatrixProvider _matrix = Substitute.For<IResolvedChecklistMatrixProvider>();

    public ChecklistMatrixGateTests()
    {
        _companyParams.GetForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CompanyDocumentParam>>(new List<CompanyDocumentParam>()));
    }

    private static ProcedureInstance Matricula() => new()
    {
        ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-1",
        Status = TramiteEstado.Borrador,
        ChecklistEstado = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private void SetMatrix(params ResolvedChecklistDoc[] docs) =>
        _matrix.GetForAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ResolvedChecklistDoc>>(docs.ToList()));

    // ── Servicio compartido ──────────────────────────────────────────────────

    [Fact]
    public async Task Servicio_SinProveedorMatriz_DevuelveNull()
    {
        // Sin proveedor inyectado ⇒ no aplica ⇒ el llamador usa su gate del catálogo.
        var svc = new ChecklistMatrixCompleteness(_companyParams);

        var result = await svc.TryComputeCompletoAsync(Matricula(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Servicio_SinMatriz_CompletoPorqueNoHayDocumentosAsociados()
    {
        SetMatrix();
        var svc = new ChecklistMatrixCompleteness(_companyParams, _matrix);

        var result = await svc.TryComputeCompletoAsync(Matricula(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Servicio_ConMatriz_ComputaCompletitud()
    {
        // factura obligatorio y sin subir ⇒ incompleto.
        SetMatrix(new ResolvedChecklistDoc("factura", "Factura", true, 10));
        var svc = new ChecklistMatrixCompleteness(_companyParams, _matrix);

        var result = await svc.TryComputeCompletoAsync(Matricula(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Servicio_DocumentoGeneradoObligatorio_NoIncompleta()
    {
        SetMatrix(new ResolvedChecklistDoc("soat", "SOAT", true, 10, EsGeneradoSistema: true));
        var svc = new ChecklistMatrixCompleteness(_companyParams, _matrix);

        var result = await svc.TryComputeCompletoAsync(Matricula(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    // ── Override en los gates ────────────────────────────────────────────────

    [Fact]
    public void SubmitGate_SinOverride_ExigeDocumentosDelCatalogo()
    {
        // Matrícula sin adjuntos: el gate actual (catálogo) exige factura/aduana ⇒ documentos_incompletos.
        var errors = SubmitGate.Evaluate(Matricula(), new HashSet<string>());

        errors.Should().Contain(SubmitGate.DocumentosIncompletos);
    }

    [Fact]
    public void SubmitGate_OverrideTrue_ElGestorLibera_NoBloqueaDocumentos()
    {
        // El gestor resolvió "documentos completos" (override=true) ⇒ el gate NO bloquea por documentos.
        var errors = SubmitGate.Evaluate(Matricula(), new HashSet<string>(), documentosCompletosOverride: true);

        errors.Should().NotContain(SubmitGate.DocumentosIncompletos);
    }

    [Fact]
    public void SubmitGate_OverrideFalse_ElGestorExige_BloqueaDocumentos()
    {
        // El gestor marcó un documento obligatorio no cargado (override=false) ⇒ bloquea aunque el catálogo no lo pidiera.
        var errors = SubmitGate.Evaluate(Matricula(), new HashSet<string>(), documentosCompletosOverride: false);

        errors.Should().Contain(SubmitGate.DocumentosIncompletos);
    }

    [Fact]
    public void FinalizeDraftGate_OverrideTrue_NoBloqueaDocumentos()
    {
        var conOverride = FinalizeDraftGate.Evaluate(Matricula(), documentosCompletosOverride: true);
        var sinOverride = FinalizeDraftGate.Evaluate(Matricula());

        sinOverride.Should().Contain(FinalizeDraftGate.DocumentosIncompletos);
        conOverride.Should().NotContain(FinalizeDraftGate.DocumentosIncompletos);
    }
}
