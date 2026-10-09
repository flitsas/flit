using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

/// <summary>
/// El mandato estampa la rúbrica de Kyverum de la validación propia del mandatario, igual que la del mandante.
/// Antes el recuadro MANDATARIO solo mostraba el sello de texto «Validación de identidad / Firma …». El baúl conserva
/// la prioridad, y si la rúbrica no se puede leer queda el sello de texto.
/// </summary>
public sealed class MandatarioFirmaResolverRubricaKyverumTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string RubricaPath = "identity/mandatario/rubrica.png";
    private static readonly byte[] Rubrica = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private static MandateSignerCandidate Candidato(bool identityVigente = true, string? rubricaPath = RubricaPath,
        bool firmaValida = true) =>
        new(
            Guid.NewGuid(), "Ana Restrepo", "1020304050", UserId: null, IdentityVigente: identityVigente,
            SignatureVaultId: null, TipoDocumento: "CC", CertificadoIdentidad: "hash-abc",
            FirmaValida: firmaValida, MotivoSinFirma: firmaValida ? null : "mandatario_fuera_de_vigencia",
            RubricaIdentidadPath: rubricaPath);

    private static IAttachmentStorage StorageCon(string path, byte[] contenido)
    {
        var storage = Substitute.For<IAttachmentStorage>();
        storage.OpenReadAsync(path, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream(contenido)));
        return storage;
    }

    [Fact]
    public async Task ConValidacionPropiaAprobada_DevuelveLaRubricaYElSello()
    {
        var ct = TestContext.Current.CancellationToken;

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, StorageCon(RubricaPath, Rubrica), TenantId, Candidato(),
            cancellationToken: ct);

        resultado.FirmaIdentidad.Should().Equal(Rubrica);
        resultado.Sello.Should().Be("Validación de identidad\nFirma hash-abc");
        resultado.Firma.Should().BeNull();
    }

    [Fact]
    public async Task ConSelloCompleto_UsaLaMismaLeyendaQueLasPartes()
    {
        var ct = TestContext.Current.CancellationToken;
        const string sello = "Validación biométrica CC 1020304050\nUUID u-1\nFirma hash-abc\nAprob 2026/10/08";

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, StorageCon(RubricaPath, Rubrica), TenantId,
            Candidato() with { SelloIdentidad = sello }, cancellationToken: ct);

        resultado.Sello.Should().Be(sello);
        resultado.FirmaIdentidad.Should().Equal(Rubrica);
    }

    [Fact]
    public async Task SinRutaDeRubrica_QuedaSoloElSello()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = Substitute.For<IAttachmentStorage>();

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, storage, TenantId, Candidato(rubricaPath: null), cancellationToken: ct);

        resultado.FirmaIdentidad.Should().BeNull();
        resultado.Sello.Should().NotBeNull();
        await storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiLaRubricaNoSePuedeLeer_QuedaElSello_YSeAvisa()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = Substitute.For<IAttachmentStorage>();
        storage.OpenReadAsync(RubricaPath, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("caído"));
        Exception? aviso = null;

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, storage, TenantId, Candidato(), ex => aviso = ex, ct);

        resultado.FirmaIdentidad.Should().BeNull();
        resultado.Sello.Should().Be("Validación de identidad\nFirma hash-abc");
        aviso.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task SinIdentidadVigente_NoLeeLaRubrica()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = Substitute.For<IAttachmentStorage>();

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, storage, TenantId, Candidato(identityVigente: false),
            cancellationToken: ct);

        resultado.FirmaIdentidad.Should().BeNull();
        resultado.Sello.Should().BeNull();
        await storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MandatarioSinFirmaValida_NoEstampaRubricaNiSello()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = StorageCon(RubricaPath, Rubrica);

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, storage, TenantId, Candidato(firmaValida: false), cancellationToken: ct);

        resultado.FirmaIdentidad.Should().BeNull();
        resultado.Sello.Should().BeNull();
        resultado.MotivoSinFirma.Should().Be("mandatario_fuera_de_vigencia");
    }
}
