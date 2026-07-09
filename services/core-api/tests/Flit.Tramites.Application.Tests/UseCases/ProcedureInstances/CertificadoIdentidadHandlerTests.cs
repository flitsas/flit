using System.Text;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class CertificadoIdentidadHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeCertClient _certClient = new();
    private readonly DescargarCertificadoIdentidadHandler _handler;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _validation = Guid.NewGuid();

    public CertificadoIdentidadHandlerTests()
    {
        _handler = new DescargarCertificadoIdentidadHandler(_repo, _certClient);
    }

    private sealed class FakeCertClient : IKyverumCertificateClient
    {
        public bool ReturnNull { get; set; }
        public Exception? Throw { get; set; }

        public Task<KyverumCertificate?> DownloadCertificateAsync(string verificationId, CancellationToken ct = default)
        {
            if (Throw is not null)
                throw Throw;
            if (ReturnNull)
                return Task.FromResult<KyverumCertificate?>(null);
            return Task.FromResult<KyverumCertificate?>(
                new KyverumCertificate(Encoding.UTF8.GetBytes("%PDF"), "application/pdf", $"cert_{verificationId}.pdf"));
        }
    }

    private ProcedureInstanceBiometricValidation Bio(
        string provider = "kyverum", string? verificationId = "kyv-1", Guid? tenant = null, Guid? instance = null) =>
        new()
        {
            Id = _validation,
            TenantId = tenant ?? _tenant,
            ProcedureInstanceId = instance ?? _instance,
            Provider = provider,
            KyverumVerificationId = verificationId,
            Status = BiometricEstados.Aprobado,
        };

    [Fact]
    public async Task Download_Kyverum_ReturnsCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(Bio());

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.ContentType.Should().Be("application/pdf");
        result.FileName.Should().Be("cert_kyv-1.pdf");
    }

    [Fact]
    public async Task Download_NotFound_WhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns((ProcedureInstanceBiometricValidation?)null);

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Download_NotFound_WhenTenantMismatch()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(Bio(tenant: Guid.NewGuid()));

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Download_SinCertificado_WhenMockProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(Bio(provider: "mock", verificationId: null));

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("sin_certificado");
    }

    [Fact]
    public async Task Download_SinCertificado_WhenKyverumHasNone()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(Bio());
        _certClient.ReturnNull = true;

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("sin_certificado");
    }

    [Fact]
    public async Task Download_ProveedorError_OnDefinitiveFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(Bio());
        _certClient.Throw = new KyverumCertificateException("rechazado", transient: false);

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("proveedor_error");
    }

    [Fact]
    public async Task Download_ProveedorNoDisponible_OnTransientFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(Bio());
        _certClient.Throw = new KyverumCertificateException("timeout", transient: true);

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("proveedor_no_disponible");
    }
}
