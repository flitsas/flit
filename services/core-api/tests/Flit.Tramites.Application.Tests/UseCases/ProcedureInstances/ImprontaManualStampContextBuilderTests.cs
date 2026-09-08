using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ImprontaManualStampContextBuilderTests
{
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();

    private static ProcedureInstance BaseInstance()
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        return new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FLIT-NIT-1",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard),
        };
    }

    private static ProcedureInstanceAttachment Impronta(Guid tenant, Guid instanceId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = instanceId,
            Tipo = "impronta",
            Filename = "impronta.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = "aa",
            StoragePath = "impronta/path",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Juridica_UsaNombreYFirmaDelPresentantePorIdentidad()
    {
        var instance = BaseInstance();
        var entityId = Guid.NewGuid();
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = entityId,
            ActorType = "vendedor",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "EMPRESA SAS",
            PersonType = ActorPersonTypes.Juridical,
            Ordinal = 1,
            Metadata = """{"representanteLegal":{"tipoDocumento":"CC","numeroDocumento":"1090123456","nombreCompleto":"Ana Presentante","email":"a@e.com","mecanismoFirma":"identidad"}}""",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var sigBytes = MinimalPng();
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = "vendedor",
            DocumentType = "CC",
            DocumentNumber = "1090123456",
            Name = "Ana Presentante",
            Status = BiometricEstados.Aprobado,
            CertificateHash = "cert-hash-rl",
            SignatureImagePath = "bio/firma.png",
            CreatedAt = DateTimeOffset.UtcNow,
            ValidatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        });

        _storage.OpenReadAsync("bio/firma.png", Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(sigBytes));

        var ctx = await ImprontaManualStampContextBuilder.BuildAsync(
            instance, Impronta(instance.TenantId, instance.Id), _storage, _vault,
            ct: TestContext.Current.CancellationToken);

        ctx.Signers.Should().ContainSingle();
        ctx.Signers[0].FullName.Should().Be("Ana Presentante");
        ctx.Signers[0].SignatureImage.Should().Equal(sigBytes);
        ctx.Signers[0].ImageSidecarText.Should().Contain("Validación biométrica");
        ctx.Signers[0].ImageSidecarText.Should().Contain("1090123456");
        await _vault.DidNotReceive().ResolveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Juridica_UsaFirmaDelBaulCuandoAplica()
    {
        var instance = BaseInstance();
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = "NIT",
            DocumentNumber = "900999888",
            FullName = "OTRA EMPRESA SA",
            PersonType = ActorPersonTypes.Juridical,
            Ordinal = 1,
            Metadata = """{"representanteLegal":{"tipoDocumento":"CC","numeroDocumento":"555666","nombreCompleto":"Luis RL","email":"l@e.com"}}""",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var baulPng = MinimalPng();
        _vault.ResolveAsync(instance.TenantId, "CC", "555666", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Luis RL Baul", "sig-hash", "vault/f.png", "sha",
                DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
                DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(11)),
                "555666",
                CodigoHash: "CODIGO123"));
        _storage.OpenReadAsync("vault/f.png", Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(baulPng));

        var ctx = await ImprontaManualStampContextBuilder.BuildAsync(
            instance, Impronta(instance.TenantId, instance.Id), _storage, _vault,
            ct: TestContext.Current.CancellationToken);

        ctx.Signers.Should().ContainSingle();
        ctx.Signers[0].FullName.Should().Be("Luis RL Baul");
        ctx.Signers[0].SignatureImage.Should().Equal(baulPng);
        ctx.Signers[0].ImageSidecarText.Should().Contain("Doc. 555666");
        ctx.Signers[0].ImageSidecarText.Should().Contain("Luis RL Baul");
        ctx.Signers[0].ImageSidecarText.Should().Contain("Hash: CODIGO123");
    }

    [Fact]
    public async Task Natural_UsaDatosDelPropietario()
    {
        var instance = BaseInstance();
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = "CC",
            DocumentNumber = "10101010",
            FullName = "Pedro Natural",
            PersonType = ActorPersonTypes.Natural,
            Ordinal = 1,
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var ctx = await ImprontaManualStampContextBuilder.BuildAsync(
            instance, Impronta(instance.TenantId, instance.Id), _storage,
            ct: TestContext.Current.CancellationToken);

        ctx.Signers.Should().ContainSingle();
        ctx.Signers[0].FullName.Should().Be("Pedro Natural");
    }

    /// <summary>PNG 1×1 válido (cabecera) para pasar <see cref="IdentitySignatureImageFormat.IsSupported"/>.</summary>
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE,
        0x02, 0xFE, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];
}
