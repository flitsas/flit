using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerSignatureImage;
using Flit.Admin.Application.Companies.SignatureVault;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.SignatureVault;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

public sealed class GetMandateSignerSignatureImageHandlerTests
{
    private static readonly Guid Ot = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002");
    private static readonly Guid SignerId = Guid.Parse("262651b9-95f9-4a5e-9455-fb6dfad71e51");
    private static readonly Guid VaultId = Guid.Parse("663fc44a-b97c-4988-a36b-9bf66f0829f2");

    [Fact]
    public async Task DevuelveElPng_AunqueElBaulDeLaCompaniaEsteApagado()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var signers = Substitute.For<IMandateSignerReader>();
        signers.GetByIdAsync(SignerId, Arg.Any<CancellationToken>())
            .Returns(new MandateSignerItem
            {
                Id = SignerId,
                TransitOfficeId = Ot,
                TransitOfficeIds = [Ot],
                SignatureVaultId = VaultId,
                FullName = "JUAN COPETE",
            });

        var vault = Substitute.For<ISignatureVaultReader>();
        vault.GetByIdAnyTenantAsync(VaultId, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultItem
            {
                Id = VaultId,
                StoragePath = "firmas/juan.png",
            });

        var artifacts = Substitute.For<ISignatureVaultArtifactStorage>();
        artifacts.OpenReadAsync("firmas/juan.png", Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(png));

        var handler = new GetMandateSignerSignatureImageHandler(signers, vault, artifacts);
        var result = await handler.HandleAsync(Ot, SignerId, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetMandateSignerSignatureImageOutcome.Ok);
        result.Content.Should().Equal(png);
    }

    [Fact]
    public async Task SinImagenVinculada_NoSignature()
    {
        var signers = Substitute.For<IMandateSignerReader>();
        signers.GetByIdAsync(SignerId, Arg.Any<CancellationToken>())
            .Returns(new MandateSignerItem
            {
                Id = SignerId,
                TransitOfficeId = Ot,
                TransitOfficeIds = [Ot],
                SignatureVaultId = null,
            });

        var handler = new GetMandateSignerSignatureImageHandler(
            signers, Substitute.For<ISignatureVaultReader>(), Substitute.For<ISignatureVaultArtifactStorage>());
        var result = await handler.HandleAsync(Ot, SignerId, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetMandateSignerSignatureImageOutcome.NoSignature);
        result.Content.Should().BeNull();
    }
}
