using Flit.Ict.Application.Documents;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Documents;

/// <summary>
/// HU #14668 Bloque 2 — GetDocumentsTransactionHandler: obtención de URL prefirmada del consolidado.
/// </summary>
public sealed class GetDocumentsTransactionHandlerTests
{
    private readonly IPreTramiteRepository _repo = Substitute.For<IPreTramiteRepository>();
    private readonly IIctConsolidadoClient _consolidado = Substitute.For<IIctConsolidadoClient>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private readonly Guid _tenantId = Guid.NewGuid();
    private const string Ref = "TRX-0001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetDocumentsTransactionHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
    }

    private GetDocumentsTransactionHandler CreateHandler() =>
        new(_repo, _consolidado, _tenant);

    private ExternalIntegrationMaster MasterWithInstance(Guid instanceId) =>
        new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProcedureInstanceId = instanceId };

    private ExternalIntegrationMaster MasterWithoutInstance() =>
        new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProcedureInstanceId = null };

    // ──────────────────────────────────────────────────────────────
    // 1. Sin tenant → unauthenticated
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NoTenant_ReturnsUnauthenticated()
    {
        _tenant.TenantId.Returns((Guid?)null);
        var handler = CreateHandler();

        var (url, error) = await handler.HandleAsync(Ref, Ct);

        url.Should().BeNull();
        error.Should().Be("unauthenticated");
    }

    // ──────────────────────────────────────────────────────────────
    // 2. Pre-trámite no encontrado → not_found
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_MasterNotFound_ReturnsNotFound()
    {
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns((ExternalIntegrationMaster?)null);
        var handler = CreateHandler();

        var (url, error) = await handler.HandleAsync(Ref, Ct);

        url.Should().BeNull();
        error.Should().Be("not_found");
    }

    // ──────────────────────────────────────────────────────────────
    // 3. Pre-trámite sin procedure_instance_id → not_materialized
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_MasterNotMaterialized_ReturnsNotMaterialized()
    {
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithoutInstance());
        var handler = CreateHandler();

        var (url, error) = await handler.HandleAsync(Ref, Ct);

        url.Should().BeNull();
        error.Should().Be("not_materialized");
    }

    // ──────────────────────────────────────────────────────────────
    // 4. Trámite no aprobado → not_approved
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("borrador")]
    [InlineData("en_revision")]
    [InlineData("rechazado")]
    [InlineData(null)]   // tramite_status no disponible (best-effort null)
    public async Task HandleAsync_TramiteNotAprobado_ReturnsNotApproved(string? status)
    {
        var instanceId = Guid.NewGuid();
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithInstance(instanceId));
        _repo.GetTramiteStatusAsync(instanceId, Ct).Returns(status);
        var handler = CreateHandler();

        var (url, error) = await handler.HandleAsync(Ref, Ct);

        url.Should().BeNull();
        error.Should().Be("not_approved");
    }

    // ──────────────────────────────────────────────────────────────
    // 5. Trámite aprobado pero sin consolidado → consolidado_no_disponible
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_AprobadoWithoutConsolidado_ReturnsConsolidadoNoDisponible()
    {
        var instanceId = Guid.NewGuid();
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithInstance(instanceId));
        _repo.GetTramiteStatusAsync(instanceId, Ct).Returns("aprobado");
        _consolidado.GetPresignedUrlAsync(_tenantId, instanceId, Ct)
            .Returns((null, "consolidado_no_disponible"));
        var handler = CreateHandler();

        var (url, error) = await handler.HandleAsync(Ref, Ct);

        url.Should().BeNull();
        error.Should().Be("consolidado_no_disponible");
    }

    // ──────────────────────────────────────────────────────────────
    // 6. File Manager no disponible → storage_unavailable
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_StorageUnavailable_ReturnsStorageUnavailable()
    {
        var instanceId = Guid.NewGuid();
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithInstance(instanceId));
        _repo.GetTramiteStatusAsync(instanceId, Ct).Returns("aprobado");
        _consolidado.GetPresignedUrlAsync(_tenantId, instanceId, Ct)
            .Returns((null, "storage_unavailable"));
        var handler = CreateHandler();

        var (url, error) = await handler.HandleAsync(Ref, Ct);

        url.Should().BeNull();
        error.Should().Be("storage_unavailable");
    }

    // ──────────────────────────────────────────────────────────────
    // 7. Happy path → (url, null)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_AprobadoConConsolidado_ReturnsPresignedUrl()
    {
        var instanceId = Guid.NewGuid();
        const string expectedUrl = "https://files.example.com/presigned/abc123";
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithInstance(instanceId));
        _repo.GetTramiteStatusAsync(instanceId, Ct).Returns("aprobado");
        _consolidado.GetPresignedUrlAsync(_tenantId, instanceId, Ct).Returns((expectedUrl, null));
        var handler = CreateHandler();

        var (url, error) = await handler.HandleAsync(Ref, Ct);

        error.Should().BeNull();
        url.Should().Be(expectedUrl);
    }

    // ──────────────────────────────────────────────────────────────
    // 8. Estado "aprobado" case-insensitive
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("APROBADO")]
    [InlineData("Aprobado")]
    public async Task HandleAsync_AprobadoCaseInsensitive_ContinuesToConsolidado(string status)
    {
        var instanceId = Guid.NewGuid();
        const string url = "https://files.example.com/presigned/xyz";
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithInstance(instanceId));
        _repo.GetTramiteStatusAsync(instanceId, Ct).Returns(status);
        _consolidado.GetPresignedUrlAsync(_tenantId, instanceId, Ct).Returns((url, null));
        var handler = CreateHandler();

        var (resultUrl, error) = await handler.HandleAsync(Ref, Ct);

        error.Should().BeNull();
        resultUrl.Should().Be(url);
    }

    // ──────────────────────────────────────────────────────────────
    // Versión preliminar (?preliminar=true)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandlePreliminarAsync_Borrador_EntregaSinExigirAprobadoYNoConsultaElEstado()
    {
        var instanceId = Guid.NewGuid();
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithInstance(instanceId));
        var esperado = new ConsolidadoEntregado("https://fm.example/consolidado.pdf", "borrador", Definitivo: false);
        _consolidado.GetConsolidadoAsync(_tenantId, instanceId, true, Ct).Returns((esperado, (string?)null));

        var (entrega, error) = await CreateHandler().HandlePreliminarAsync(Ref, Ct);

        error.Should().BeNull();
        entrega.Should().Be(esperado);
        await _repo.DidNotReceiveWithAnyArgs().GetTramiteStatusAsync(default, Ct);
        await _consolidado.DidNotReceiveWithAnyArgs().GetPresignedUrlAsync(default, default, Ct);
    }

    [Theory]
    [InlineData("anulado")]
    [InlineData("consolidado_no_disponible")]
    [InlineData("storage_unavailable")]
    public async Task HandlePreliminarAsync_PropagaElErrorDeLaEntrega(string codigo)
    {
        var instanceId = Guid.NewGuid();
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithInstance(instanceId));
        _consolidado.GetConsolidadoAsync(_tenantId, instanceId, true, Ct).Returns(((ConsolidadoEntregado?)null, codigo));

        var (entrega, error) = await CreateHandler().HandlePreliminarAsync(Ref, Ct);

        entrega.Should().BeNull();
        error.Should().Be(codigo);
    }

    [Fact]
    public async Task HandlePreliminarAsync_SinTenantNoEncontradoONoMaterializado()
    {
        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns(MasterWithoutInstance());
        (await CreateHandler().HandlePreliminarAsync(Ref, Ct)).Error.Should().Be("not_materialized");

        _repo.FindByManagerIdTransactionAsync(Ref, _tenantId, Ct).Returns((ExternalIntegrationMaster?)null);
        (await CreateHandler().HandlePreliminarAsync(Ref, Ct)).Error.Should().Be("not_found");

        _tenant.TenantId.Returns((Guid?)null);
        (await CreateHandler().HandlePreliminarAsync(Ref, Ct)).Error.Should().Be("unauthenticated");
        await _consolidado.DidNotReceiveWithAnyArgs().GetConsolidadoAsync(default, default, default, Ct);
    }
}
