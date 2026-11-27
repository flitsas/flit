using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Companies;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.Companies;

/// <summary>
/// HU #14694 (plan §1.3 decisión P1, D-TAXID, D-EMAIL, DA-8) — los datos que core-impuestos enviará al alta del
/// proveedor. El mismo handler atiende <c>GET …/alta-previa</c> y el rpc <c>EmpresasService.ObtenerDatosAltaProveedor</c>.
/// <para>Uso de ejemplo: <c>await handler.HandleAsync(tenantId, "impuestos")</c> → razón social, NIT base, correo, bloqueo.</para>
/// </summary>
public sealed class ObtenerDatosAltaProveedorHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IDatosAltaProveedorStore _store = Substitute.For<IDatosAltaProveedorStore>();

    private ObtenerDatosAltaProveedorHandler Handler() => new(_store);

    private void Empresa(string? taxId, string? correo, IReadOnlyList<string?>? ancestros = null, bool activa = true) =>
        _store.ObtenerAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new DatosEmpresaParaAlta("Transportes Prueba S.A.S.", taxId, activa, ancestros ?? [], correo));

    [Fact]
    public async Task NitValidoYAdminCompany_DevuelveLoQueSeEnviara_SinBloqueo()
    {
        Empresa("900.123.456-7", "admin@prueba.test");

        var datos = await Handler().HandleAsync(TenantId, "impuestos", Ct);

        datos.Should().Be(new DatosAltaProveedor("Transportes Prueba S.A.S.", "900123456", EstadoNit.Valido, "admin@prueba.test", null, true));
        datos!.CodigoBloqueo.Should().BeNull();
    }

    [Fact]
    public async Task SinAdminCompanyActivo_BloqueaConSinAdmin()
    {
        Empresa("900123456-7", null);

        var datos = await Handler().HandleAsync(TenantId, "impuestos", Ct);

        datos!.Bloqueo.Should().Be(BloqueoAlta.SinAdmin);
        datos.CodigoBloqueo.Should().Be("SIN_ADMIN");
        datos.ContactEmail.Should().BeNull();
    }

    [Theory]
    [InlineData("9001234567", EstadoNit.Ambiguo)]
    [InlineData("", EstadoNit.Ausente)]
    [InlineData(null, EstadoNit.Ausente)]
    public async Task NitNoValido_NoBloquea_YNoEnviaBase(string? taxId, EstadoNit estado)
    {
        Empresa(taxId, "admin@prueba.test");

        var datos = await Handler().HandleAsync(TenantId, "impuestos", Ct);

        datos!.EstadoNit.Should().Be(estado);
        datos.TaxIdBase.Should().BeNull();
        datos.Bloqueo.Should().BeNull("un NIT que no es VALIDO no bloquea el PUT (supuesto S-C)");
    }

    [Fact]
    public async Task HijaConElNitDeLaCabeza_EsIgualACabeza()
    {
        Empresa("900123456-7", "admin@prueba.test", ["900.123.456-7"]);

        var datos = await Handler().HandleAsync(TenantId, "impuestos", Ct);

        datos!.EstadoNit.Should().Be(EstadoNit.IgualACabeza);
        datos.TaxIdBase.Should().BeNull();
    }

    [Fact]
    public async Task EmpresaInactiva_LoInforma()
    {
        Empresa("900123456-7", "admin@prueba.test", activa: false);

        (await Handler().HandleAsync(TenantId, "impuestos", Ct))!.EmpresaActiva.Should().BeFalse();
    }

    [Fact]
    public async Task EmpresaInexistente_DevuelveNull()
    {
        _store.ObtenerAsync(TenantId, Arg.Any<CancellationToken>()).Returns((DatosEmpresaParaAlta?)null);

        (await Handler().HandleAsync(TenantId, "impuestos", Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("diagnostico")]
    [InlineData("flotas")]
    public async Task ProductoSinAprovisionamiento_SeRechaza(string code)
    {
        var act = () => Handler().HandleAsync(TenantId, code, Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductWithoutProvisioning);
        await _store.DidNotReceiveWithAnyArgs().ObtenerAsync(default, Ct);
    }
}
