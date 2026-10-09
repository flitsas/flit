using Flit.Identity.Api.Grpc;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Identity.Tests.Grpc;

/// <summary>HU #13334 (Epic #13316) — token de página opaco de <c>ListarUsuariosEmpresa</c>.</summary>
public sealed class IdentidadPageTokenTests
{
    private static StatusCode CodeOf(Action act) => act.Should().Throw<RpcException>().Which.StatusCode;

    [Fact]
    public void PageToken_IdaYVuelta_YUnoInventadoEsInvalidArgument()
    {
        IdentidadGrpcService.PageToken.Decode(IdentidadGrpcService.PageToken.Encode(150)).Should().Be(150);
        IdentidadGrpcService.PageToken.Decode(string.Empty).Should().Be(0);
        CodeOf(() => IdentidadGrpcService.PageToken.Decode("basura")).Should().Be(StatusCode.InvalidArgument);
        CodeOf(() => IdentidadGrpcService.PageToken.Decode(Convert.ToBase64String("v1:-3"u8.ToArray()))).Should().Be(StatusCode.InvalidArgument);
    }
}
