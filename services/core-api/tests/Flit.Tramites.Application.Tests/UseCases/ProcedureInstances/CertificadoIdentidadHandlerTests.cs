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

    // ── HU #11014 — identidad APALANDADA: la validación vigente de la persona vive en OTRO trámite ──

    /// <summary>Instancia con un actor cuyo documento coincide con la validación apalancada.</summary>
    private ProcedureInstance InstanceConActor(string tipoDoc = "CC", string documento = "1020304050") =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = _instance,
            TenantId = _tenant,
            Actors = [new ProcedureInstanceActor
            {
                ActorType = "vendedor",
                DocumentType = tipoDoc,
                DocumentNumber = documento,
                FullName = "Vendedor Apalancado",
            }],
        };

    private ProcedureInstanceBiometricValidation BioApalancada(
        string tipoDoc = "CC", string documento = "1020304050", Guid? otraInstancia = null) =>
        new()
        {
            Id = _validation,
            TenantId = _tenant,
            // Vive en otro trámite (o es una prevalidación standalone si va null).
            ProcedureInstanceId = otraInstancia,
            Provider = "kyverum",
            KyverumVerificationId = "kyv-apalancada",
            Status = BiometricEstados.Aprobado,
            DocumentType = tipoDoc,
            DocumentNumber = documento,
            ValidatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ValidUntil = DateTimeOffset.UtcNow.AddDays(28),
        };

    [Fact]
    public async Task Download_IdentidadApalancadaDeOtroTramite_DevuelveCertificado()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(BioApalancada(otraInstancia: Guid.NewGuid()));
        _repo.GetByIdWithBiometricsAndActorsAsync(_instance, _tenant, ct).Returns(InstanceConActor());

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        // Antes devolvía "not_found" y la UI pintaba "Validación de identidad no encontrada".
        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Download_PrevalidacionStandalone_DevuelveCertificado()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(BioApalancada(otraInstancia: null));
        _repo.GetByIdWithBiometricsAndActorsAsync(_instance, _tenant, ct).Returns(InstanceConActor());

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Download_ValidacionDeOtraPersona_NoSeExpone()
    {
        var ct = TestContext.Current.CancellationToken;
        // La validación es de otro documento: no es la identidad efectiva de ninguna parte del trámite.
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(BioApalancada(documento: "999999999", otraInstancia: Guid.NewGuid()));
        _repo.GetByIdWithBiometricsAndActorsAsync(_instance, _tenant, ct).Returns(InstanceConActor());

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Download_ValidacionApalancadaVencida_NoSeExpone()
    {
        var ct = TestContext.Current.CancellationToken;
        var vencida = BioApalancada(otraInstancia: Guid.NewGuid());
        vencida.ValidatedAt = DateTimeOffset.UtcNow.AddDays(-90);
        vencida.ValidUntil = DateTimeOffset.UtcNow.AddDays(-60);
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(vencida);
        _repo.GetByIdWithBiometricsAndActorsAsync(_instance, _tenant, ct).Returns(InstanceConActor());

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Download_OtroTenant_SigueSinExponerse()
    {
        var ct = TestContext.Current.CancellationToken;
        var deOtroTenant = BioApalancada(otraInstancia: Guid.NewGuid());
        deOtroTenant.TenantId = Guid.NewGuid();
        _repo.GetBiometricByIdAsync(_validation, ct).Returns(deOtroTenant);

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }
}
