using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11015 — el sello de firma de los documentos estampa la serie del certificado
/// (<c>firmaSerie</c>). Cuando el webhook se pierde y aprueba la reconciliación por GET —que no
/// siempre expone <c>firmaSerie</c>— la validación quedaba aprobada SIN certificado y, por la guarda
/// de idempotencia, ningún resultado posterior podía rellenarlo: el sello salía sin valor para
/// siempre. Ahora una aprobada sin certificado SÍ se enriquece, sin cambiar estado ni re-emitir eventos.
/// </summary>
public sealed class IdentityCertificateBackfillTests
{
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();

    private IdentityValidationResultApplier Applier() => new(_events);

    private static ProcedureInstanceBiometricValidation Aprobada(string? certificado) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureInstanceId = Guid.NewGuid(),
        PartyRole = "comprador",
        Status = BiometricEstados.Aprobado,
        Provider = BiometricProviders.Kyverum,
        CertificateHash = certificado,
        ValidatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
    };

    private static IdentityValidationTerminalResult Resultado(string? certificado) =>
        new(Approved: true, ProviderStatus: "approved", SanitizedPayload: "{}", Score: 95, CertificateHash: certificado);

    [Fact]
    public async Task AprobadaSinCertificado_SeEnriqueceConLaSerieQueLlegaDespues()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Aprobada(certificado: null);
        var now = DateTimeOffset.UtcNow;

        var transiciono = await Applier().ApplyAsync(v, Resultado("firma-serie-123"), now, ct);

        // No hay transición (ya estaba aprobada) pero el hueco queda relleno.
        transiciono.Should().BeFalse();
        v.CertificateHash.Should().Be("firma-serie-123");
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.UpdatedAt.Should().Be(now);
        // La idempotencia se mantiene: no se re-emite el evento de identidad aprobada.
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, ct);
    }

    [Fact]
    public async Task AprobadaConCertificado_NoSeSobrescribe()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Aprobada(certificado: "firma-original");

        await Applier().ApplyAsync(v, Resultado("firma-nueva"), DateTimeOffset.UtcNow, ct);

        v.CertificateHash.Should().Be("firma-original");
    }

    [Fact]
    public async Task AprobadaSinCertificado_YResultadoSinSerie_SigueSinCertificado()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Aprobada(certificado: null);

        await Applier().ApplyAsync(v, Resultado(null), DateTimeOffset.UtcNow, ct);

        v.CertificateHash.Should().BeNull();
    }

    [Fact]
    public async Task Rechazada_NoSeEnriquece()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Aprobada(certificado: null);
        v.Status = BiometricEstados.Rechazado;

        await Applier().ApplyAsync(v, Resultado("firma-serie-123"), DateTimeOffset.UtcNow, ct);

        v.CertificateHash.Should().BeNull();
    }
}
