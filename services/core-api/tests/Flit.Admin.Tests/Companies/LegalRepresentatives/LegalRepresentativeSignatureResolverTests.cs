using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using FluentAssertions;
using NSubstitute;
using Xunit;
using SignatureVaultAggregate = Flit.Admin.Domain.Companies.SignatureVault.SignatureVault;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests del resolutor de firma/identidad al guardar un representante (HU #10900, ADR-0033 §5.1, D2).
/// Cubren la precedencia baúl &gt; identidad: (1) firma del baúl vigente por NIT+documento,
/// (2) validación de identidad vigente por documento, (3) ninguna, y la guarda de documento (una
/// firma del baúl de OTRO documento de la misma compañía NO se vincula).
/// </summary>
public sealed class LegalRepresentativeSignatureResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000aa01");
    private const string Nit = "900000000-1";
    private const string TipoDoc = "CC";
    private const string DocRep = "123456789";
    private static readonly DateOnly Today = new(2026, 7, 23);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ISignatureVaultReader _signatureReader = Substitute.For<ISignatureVaultReader>();
    private readonly IRepresentativeIdentityLookup _identityLookup = Substitute.For<IRepresentativeIdentityLookup>();

    private LegalRepresentativeSignatureResolver NewResolver() => new(_signatureReader, _identityLookup);

    [Fact]
    public async Task Resolve_BaulVigente_LinksSignature()
    {
        var firma = SignatureVaultAggregate.Create(
            Tenant, TipoDoc, DocRep, Nit, "Apoderada S.A.S.", "sha", "path", "sha256",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        _signatureReader.FindActiveByNitAsync(Tenant, Nit, Arg.Any<CancellationToken>()).Returns(firma);

        var result = await NewResolver().ResolveAsync(Tenant, Nit, TipoDoc, DocRep, Today, Ct);

        result.HasSignatureOrIdentity.Should().BeTrue();
        result.SignatureVaultId.Should().Be(firma.Id);
        result.IdentityValidationRef.Should().BeNull();
        // Precedencia baúl > identidad: no se consulta la identidad si ya hay firma.
        await _identityLookup.DidNotReceiveWithAnyArgs()
            .FindVigenteIdentityRefAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Resolve_IdentidadVigente_WhenNoBaul_LinksIdentity()
    {
        _signatureReader.FindActiveByNitAsync(Tenant, Nit, Arg.Any<CancellationToken>())
            .Returns((SignatureVaultAggregate?)null);
        var validationId = Guid.NewGuid();
        _identityLookup.FindVigenteIdentityRefAsync(
                Tenant, TipoDoc, DocRep, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(validationId);

        var result = await NewResolver().ResolveAsync(Tenant, Nit, TipoDoc, DocRep, Today, Ct);

        result.HasSignatureOrIdentity.Should().BeTrue();
        result.IdentityValidationRef.Should().Be(validationId);
        result.SignatureVaultId.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_None_WhenNeitherVigente()
    {
        _signatureReader.FindActiveByNitAsync(Tenant, Nit, Arg.Any<CancellationToken>())
            .Returns((SignatureVaultAggregate?)null);
        _identityLookup.FindVigenteIdentityRefAsync(
                Tenant, TipoDoc, DocRep, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var result = await NewResolver().ResolveAsync(Tenant, Nit, TipoDoc, DocRep, Today, Ct);

        result.HasSignatureOrIdentity.Should().BeFalse();
        result.SignatureVaultId.Should().BeNull();
        result.IdentityValidationRef.Should().BeNull();
        result.Should().Be(LegalRepresentativeSignatureResolution.None);
    }

    [Fact]
    public async Task Resolve_BaulExpirada_FallsBackToIdentity()
    {
        // Firma del baúl del documento correcto pero FUERA de vigencia en 'today'.
        var firma = SignatureVaultAggregate.Create(
            Tenant, TipoDoc, DocRep, Nit, "Apoderada S.A.S.", "sha", "path", "sha256",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        _signatureReader.FindActiveByNitAsync(Tenant, Nit, Arg.Any<CancellationToken>()).Returns(firma);
        var validationId = Guid.NewGuid();
        _identityLookup.FindVigenteIdentityRefAsync(
                Tenant, TipoDoc, DocRep, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(validationId);

        var result = await NewResolver().ResolveAsync(Tenant, Nit, TipoDoc, DocRep, Today, Ct);

        result.SignatureVaultId.Should().BeNull();
        result.IdentityValidationRef.Should().Be(validationId);
    }

    [Fact]
    public async Task Resolve_BaulOtroDocumento_NoLinks_FallsBackToIdentity()
    {
        // La firma del baúl del NIT pertenece a OTRO representante (documento distinto): no se vincula.
        var firma = SignatureVaultAggregate.Create(
            Tenant, TipoDoc, "999999999", Nit, "Otro Apoderado", "sha", "path", "sha256",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        _signatureReader.FindActiveByNitAsync(Tenant, Nit, Arg.Any<CancellationToken>()).Returns(firma);
        _identityLookup.FindVigenteIdentityRefAsync(
                Tenant, TipoDoc, DocRep, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var result = await NewResolver().ResolveAsync(Tenant, Nit, TipoDoc, DocRep, Today, Ct);

        result.HasSignatureOrIdentity.Should().BeFalse();
    }
}
