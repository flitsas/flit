using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// ADR-0092 (diseño impuesto-real §1.4) — borrar la API key del impuesto de una empresa: idempotente (borrar lo que no
/// existe no falla) y solo para la combinación de la lista blanca.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <c>await new DeleteTenantProductCredentialHandler(repo).HandleAsync(tenant, "diagnostico", "impuesto_api_key", adminId, ct)</c>.
/// </remarks>
public sealed class DeleteTenantProductCredentialHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly ITenantProductCredentialRepository _repository = Substitute.For<ITenantProductCredentialRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConFila_Borra_YDevuelveTrue()
    {
        _repository.DeleteAsync(TenantId, "diagnostico", "impuesto_api_key", AdminId, Arg.Any<CancellationToken>()).Returns(true);

        var borrada = await new DeleteTenantProductCredentialHandler(_repository).HandleAsync(TenantId, " Diagnostico ", "IMPUESTO_API_KEY", AdminId, Ct);

        borrada.Should().BeTrue();
        await _repository.Received(1).DeleteAsync(TenantId, "diagnostico", "impuesto_api_key", AdminId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinFila_EsIdempotente_YNoFalla()
    {
        _repository.DeleteAsync(TenantId, "diagnostico", "impuesto_api_key", AdminId, Arg.Any<CancellationToken>()).Returns(false);

        var borrada = await new DeleteTenantProductCredentialHandler(_repository).HandleAsync(TenantId, "diagnostico", "impuesto_api_key", AdminId, Ct);

        borrada.Should().BeFalse();
    }

    [Theory]
    [InlineData("tramites", "impuesto_api_key")]
    [InlineData("diagnostico", "otra_llave")]
    // HU #14711: los secretos del webhook solo los escribe el alta del proveedor; el PUT/DELETE manual no los acepta.
    [InlineData("diagnostico", "impuesto_webhook_secret")]
    [InlineData("diagnostico", "impuesto_webhook_secret_anterior")]
    public async Task CombinacionFueraDeLaListaBlanca_NoSoportada_YNoTocaElRepositorio(string producto, string credencial)
    {
        var act = () => new DeleteTenantProductCredentialHandler(_repository).HandleAsync(TenantId, producto, credencial, AdminId, Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.CredentialNotSupported);
        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default!, default, Ct);
    }
}
