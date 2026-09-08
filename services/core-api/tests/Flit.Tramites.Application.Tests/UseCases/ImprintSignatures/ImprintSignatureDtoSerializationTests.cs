using System.Text.Json;
using Flit.Tramites.Application.UseCases.ImprintSignatures;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ImprintSignatures;

public sealed class ImprintSignatureDtoSerializationTests
{
    [Fact]
    public void ImprintSignatureDto_JsonSerialization_DoesNotIncludePrivateKey()
    {
        var dto = new ImprintSignatureDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            ProcedureInstanceId: Guid.NewGuid(),
            Placa: "POV420",
            ModuleCode: "tramites",
            AttachmentId: null,
            PublicKey: "-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----",
            DocumentHash: "abc123",
            Signature: "sig==",
            SignedAt: DateTimeOffset.UtcNow,
            WasSignedWithoutOwnerSignature: false,
            SignedStoragePath: null,
            SignedSha256: null,
            SignedSizeBytes: null,
            SignedFilename: null,
            DeletedAt: null);

        var json = JsonSerializer.Serialize(dto);

        json.Should().NotContain("private_key", because: "el DTO nunca debe exponer la clave privada");
        json.Should().NotContain("PrivateKey");
        json.Should().Contain("PublicKey");
        json.Should().Contain("DocumentHash");
    }
}
