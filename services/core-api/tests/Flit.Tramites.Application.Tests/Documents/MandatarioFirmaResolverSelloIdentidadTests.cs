using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

/// <summary>
/// HU #11753 (ADR-0050) — el sello de identidad del mandatario (HU #11030) en el Contrato Privado de
/// Mandato se conserva EXACTAMENTE igual, campo a campo, ante el cambio de fuente de identidad
/// (HU #11751/#11752): <see cref="MandatarioFirmaResolver"/> nunca lee ninguna tabla directamente —
/// solo consume <see cref="MandateSignerCandidate.IdentityVigente"/> y
/// <see cref="MandateSignerCandidate.CertificadoIdentidad"/>, cuyo CONTRATO no cambió. Estas pruebas
/// fijan ese contrato con los valores que <c>MandateSignerDirectory</c> produce hoy desde el módulo
/// Identidad (HU #11752), y el caso DA-4 aceptado por el PO.
///
/// <para>Uso de ejemplo:
/// <c>MandatarioFirmaResolver.ResolveAsync(NullSignatureVaultPolicy.Instance, storage, tenantId,
/// candidato, ct: ct)</c> ⇒ <c>Resultado.Sello == "Validación de identidad\nFirma {hash}"</c> cuando el
/// candidato tiene identidad vigente y sin firma en el baúl.</para>
/// </summary>
public sealed class MandatarioFirmaResolverSelloIdentidadTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static MandateSignerCandidate Candidato(
        bool identityVigente, string? certificado, DateTimeOffset? validUntil = null) =>
        new(
            Guid.NewGuid(), "Ana Restrepo", "1020304050", UserId: null, IdentityVigente: identityVigente,
            SignatureVaultId: null, TipoDocumento: "CC", CertificadoIdentidad: certificado,
            IdentityValidUntil: validUntil);

    [Fact]
    public async Task IdentidadAprobadaYVigenteEnElModuloIdentidad_ProduceElMismoSelloDeSiempre()
    {
        // Mismos campos, mismo valor que produciría (y producía) la fuente anterior: el formato del
        // sello (HU #11030) vive en MandatarioFirmaResolver, no en el origen del dato.
        var ct = TestContext.Current.CancellationToken;
        var candidato = Candidato(identityVigente: true, certificado: "hash-mandatario-abc");

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance,
            Substitute.For<IAttachmentStorage>(),
            TenantId,
            candidato,
            cancellationToken: ct);

        resultado.Firma.Should().BeNull();
        resultado.Metadatos.Should().BeNull();
        resultado.Sello.Should().Be("Validación de identidad\nFirma hash-mandatario-abc");
    }

    [Fact]
    public async Task DA4_IdentidadSoloExistiaEnLaTablaAdmin_MandatoSaleSinSelloYSinExcepcionNiBloqueo()
    {
        // Caso DA-4 (ADR-0050, aceptado por el PO el 2026-08-20): un mandatario cuya identidad SOLO
        // vivía en admin.admin_identity_validations queda, tras el cambio de fuente, con
        // IdentityVigente=false (MandateSignerDirectory ya no lee esa tabla — ver
        // MandateSignerDirectoryIdentityVigenciaTests.NingunaConsultaVaHaciaAdminIdentityValidations).
        // La consecuencia NO es un error: el mandato se emite igual, sin bloque de sello, sin lanzar.
        var ct = TestContext.Current.CancellationToken;
        var candidato = Candidato(identityVigente: false, certificado: null);

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance,
            Substitute.For<IAttachmentStorage>(),
            TenantId,
            candidato,
            cancellationToken: ct);

        resultado.Firma.Should().BeNull();
        resultado.Sello.Should().BeNull();
        resultado.Metadatos.Should().BeNull();
        // Ninguna excepción se propagó: la ausencia de aserción de "Throws" ya lo prueba, pero se deja
        // explícito el `resultado` completo para que quede como snapshot del caso aceptado.
    }

    [Fact]
    public async Task CertificadoVacioAunqueVigente_NoProduceSelloVacio()
    {
        // Defensa en profundidad del formato: IdentityVigente=true con certificado vacío/nulo (dato
        // parcial) no debe producir "Validación de identidad\nFirma " (línea a medias) — se prefiere
        // sin sello, igual que sin firma del baúl.
        var ct = TestContext.Current.CancellationToken;
        var candidato = Candidato(identityVigente: true, certificado: "   ");

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance,
            Substitute.For<IAttachmentStorage>(),
            TenantId,
            candidato,
            cancellationToken: ct);

        resultado.Sello.Should().BeNull();
    }
}
