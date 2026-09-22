using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12774 — resolución del tipo de adjunto del certificado de Cámara de Comercio por rol, y su
/// registro como tipo cargable. Lo que se protege es que el certificado de una parte no pueda
/// satisfacer el requisito de otra.
/// </summary>
public sealed class CamaraComercioAttachmentTipoTests
{
    /// <summary>AC2 — el adjunto se guarda bajo el código de su rol.</summary>
    [Theory]
    [InlineData("vendedor", "camara_comercio_vendedor")]
    [InlineData("comprador", "camara_comercio_comprador")]
    [InlineData("locatario", "camara_comercio_locatario")]
    public void For_DevuelveElTipoDelRol(string rol, string esperado) =>
        CamaraComercioAttachmentTipo.For(rol).Should().Be(esperado);

    /// <summary>El rol llega del cliente: no puede importar el casing ni los espacios.</summary>
    [Theory]
    [InlineData("VENDEDOR")]
    [InlineData("  vendedor  ")]
    [InlineData("Vendedor")]
    public void For_NormalizaElRol(string rol) =>
        CamaraComercioAttachmentTipo.For(rol).Should().Be(CamaraComercioAttachmentTipo.Vendedor);

    /// <summary>
    /// AC3 — un rol desconocido devuelve null en vez de caer a un código por defecto. Adivinar
    /// equivaldría a dejar que el certificado de una parte satisficiera el de otra, que es
    /// justamente lo que estos códigos existen para impedir. «propietario» entra aquí a propósito:
    /// no es un rol del modelo, se deriva de la modalidad.
    /// </summary>
    [Theory]
    [InlineData("propietario")]
    [InlineData("arrendador")]
    [InlineData("")]
    [InlineData(null)]
    public void For_RolDesconocidoNoCaeAUnCodigoPorDefecto(string? rol) =>
        CamaraComercioAttachmentTipo.For(rol).Should().BeNull();

    /// <summary>AC3 — dos actores jurídicos nunca comparten código.</summary>
    [Fact]
    public void LosTresCodigosSonDistintosEntreSi() =>
        CamaraComercioAttachmentTipo.Todos.Should().OnlyHaveUniqueItems()
            .And.HaveCount(3);

    [Theory]
    [InlineData("camara_comercio_vendedor", "vendedor")]
    [InlineData("camara_comercio_comprador", "comprador")]
    [InlineData("camara_comercio_locatario", "locatario")]
    public void RoleOf_EsLaInversaDeFor(string tipo, string rol) =>
        CamaraComercioAttachmentTipo.RoleOf(tipo).Should().Be(rol);

    /// <summary>
    /// El código histórico <c>camara_comercio</c> NO es uno de los nuestros: es el del catálogo de
    /// paridad FLIT 1.0 y arrastra los datos migrados de V1.
    /// </summary>
    [Theory]
    [InlineData("camara_comercio")]
    [InlineData("escritura_representante")]
    [InlineData("otro")]
    public void Is_NoReconoceCodigosAjenos(string tipo) =>
        CamaraComercioAttachmentTipo.Is(tipo).Should().BeFalse();

    [Fact]
    public void Is_ReconoceLosTresCodigosDelRol() =>
        CamaraComercioAttachmentTipo.Todos.Should()
            .OnlyContain(t => CamaraComercioAttachmentTipo.Is(t));

    /// <summary>
    /// AC2 — sin esto la carga responde 400 «tipo inválido»: la validación estática de la vía
    /// presigned solo mira este set.
    /// </summary>
    [Fact]
    public void LosTresTiposSonCargables() =>
        CamaraComercioAttachmentTipo.Todos.Should()
            .OnlyContain(t => AttachmentRules.ValidTipos.Contains(t));

    /// <summary>
    /// Subir un certificado nuevo debe RETIRAR el anterior del mismo rol: la casilla es una, no una
    /// bolsa. Si acumulara, el expediente se quedaría con el certificado viejo y el nuevo a la vez.
    /// </summary>
    [Fact]
    public void SubirUnCertificadoReemplazaAlAnteriorDelMismoRol() =>
        CamaraComercioAttachmentTipo.Todos.Should()
            .OnlyContain(t => AttachmentRules.ReemplazaAlSubir(t));

    /// <summary>AC5 — el expediente identifica de qué parte es cada certificado.</summary>
    [Theory]
    [InlineData("camara_comercio_vendedor", "Cámara de Comercio (vendedor)")]
    [InlineData("camara_comercio_comprador", "Cámara de Comercio (comprador)")]
    [InlineData("camara_comercio_locatario", "Cámara de Comercio (locatario)")]
    public void Expediente_MuestraElRolEnLaEtiqueta(string tipo, string esperado) =>
        DocumentLabels.Display(tipo).Should().Be(esperado);
}
