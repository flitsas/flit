using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12203 — vista previa del Certificado RUES (CF-04): revisión antes de emitir, SIN persistir.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new PreviewRuesCompanyHandler(lookup);
/// var result = await handler.HandleAsync(tenantId, "900123456");
/// // result.Found == true, result.Campos con los campos mercantiles
/// </code>
/// </summary>
public sealed class PreviewRuesCompanyHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeStandaloneRuesCompanyLookup _lookup = new();

    private PreviewRuesCompanyHandler Sut => new(_lookup);

    [Fact]
    public async Task NitValido_DevuelveLosCamposMercantilesParaRevision()
    {
        var result = await Sut.HandleAsync(Tenant, "900123456", TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Nit.Should().Be("900123456");
        result.Error.Should().BeNull();
        result.Campos.Should().Contain(c => c.Key == "rues_razon_social" && c.Value == "EMPRESA DE PRUEBA SAS");
    }

    [Fact]
    public void ElPreviewNoTienePorDondePersistir()
    {
        // El AC exige que el preview no escriba ninguna fila en admin.standalone_documents. Es
        // estructural: el handler no recibe repositorio ni storage, así que no puede hacerlo aunque
        // alguien lo intentara desde dentro.
        var dependencias = typeof(PreviewRuesCompanyHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencias.Should().NotContain(typeof(IStandaloneDocumentRepository));
        dependencias.Should().NotContain(typeof(IStandaloneDocumentStorage));
        dependencias.Should().NotContain(typeof(IStandaloneRuesCertificateRenderer));
        dependencias.Should().ContainSingle().Which.Should().Be<IStandaloneRuesCompanyLookup>();
    }

    [Fact]
    public async Task NitSinCoincidencia_DegradaSinCampos()
    {
        _lookup.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null);

        var result = await Sut.HandleAsync(Tenant, "900999999", TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        result.Campos.Should().BeEmpty();
        result.Error.Should().BeNull("un NIT sin coincidencia no es un fallo del proveedor");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NitVacio_NoConsultaAlProveedor(string? nit)
    {
        var result = await Sut.HandleAsync(Tenant, nit, TestContext.Current.CancellationToken);

        result.Error.Should().Be("invalid_request");
        _lookup.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ProveedorCaido_DevuelveElCodigoNormalizadoYNingunCampo()
    {
        _lookup.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, "provider_unavailable");

        var result = await Sut.HandleAsync(Tenant, "900123456", TestContext.Current.CancellationToken);

        result.Error.Should().Be("provider_unavailable");
        result.Found.Should().BeFalse();
        result.Campos.Should().BeEmpty();
    }
}
