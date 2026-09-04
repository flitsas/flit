using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class IdentityCertificateAttachmentTipoTests
{
    [Theory]
    [InlineData("comprador", 1, "certificado_identidad")]
    [InlineData("comprador", 2, "certificado_identidad_2")]
    [InlineData("vendedor", 1, "certificado_identidad_vendedor")]
    [InlineData("vendedor", 3, "certificado_identidad_vendedor_3")]
    public void For_EmiteTipoHistoricoOConSufijo(string role, int ordinal, string esperado) =>
        IdentityCertificateAttachmentTipo.For(role, ordinal).Should().Be(esperado);

    [Theory]
    [InlineData("certificado_identidad", "certificado_identidad")]
    [InlineData("certificado_identidad_2", "certificado_identidad")]
    [InlineData("certificado_identidad_vendedor", "certificado_identidad_vendedor")]
    [InlineData("certificado_identidad_vendedor_2", "certificado_identidad_vendedor")]
    public void RankKey_AgrupaFamilia(string tipo, string familia) =>
        IdentityCertificateAttachmentTipo.RankKey(tipo).Should().Be(familia);

    [Theory]
    [InlineData("certificado_identidad", 1)]
    [InlineData("certificado_identidad_2", 2)]
    [InlineData("certificado_identidad_vendedor", 1)]
    [InlineData("certificado_identidad_vendedor_4", 4)]
    public void OrdinalFromTipo_LeeSufijoNumerico(string tipo, int ordinal) =>
        IdentityCertificateAttachmentTipo.OrdinalFromTipo(tipo).Should().Be(ordinal);

    [Theory]
    [InlineData("certificado_identidad_2", "Certificado de validación de identidad (2)")]
    [InlineData("certificado_identidad_vendedor_3", "Certificado de identidad (vendedor) (3)")]
    public void DocumentLabels_SufijoOrdinal(string tipo, string esperado) =>
        DocumentLabels.Display(tipo).Should().Be(esperado);
}
