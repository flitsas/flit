using FluentAssertions;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Motivo de rechazo para reportes: vocabulario cerrado en español, sin delatar cómo se validó.
/// </summary>
public sealed class IdentityRejectionReasonTextTests
{
    private static ProcedureInstanceBiometricValidation Rechazada(
        string? detail = null, string? payload = null, string? codigo = null) =>
        new()
        {
            Status = BiometricEstados.Rechazado,
            Detail = detail,
            ProviderPayload = payload,
            RejectionReasonCode = codigo,
        };

    [Theory]
    [InlineData(BiometricEstados.Aprobado)]
    [InlineData(BiometricEstados.EnProceso)]
    [InlineData(BiometricEstados.Enviado)]
    public void Sin_rechazo_no_hay_motivo(string estado) =>
        IdentityRejectionReasonText.For(new() { Status = estado }).Should().BeNull();

    [Fact]
    public void Codigo_de_la_lista_cerrada_sale_con_su_etiqueta()
    {
        IdentityRejectionReasonText.For(Rechazada(codigo: ManualRejectionReasons.ImagenBorrosa))
            .Should().Be("Imagen borrosa.");
    }

    [Theory]
    [InlineData("{\"motivo\":\"Coincidencia facial insuficiente\"}", null, "El rostro no coincide con el documento.")]
    [InlineData(null, "{\"subject_motivo\":\"Imagen borrosa del documento\"}", "Imagen borrosa.")]
    [InlineData(null, "{\"coincidencias\":{\"documento\":false}}", "La verificación del documento no fue exitosa.")]
    [InlineData(null, "{\"coincidencias\":{\"nombre\":false}}", "Los datos personales no coinciden con el documento.")]
    public void Texto_de_origen_se_traduce_al_vocabulario(string? detail, string? payload, string esperado) =>
        IdentityRejectionReasonText.For(Rechazada(detail, payload)).Should().Be(esperado);

    [Theory]
    [InlineData("{\"motivo\":\"Rechazado por Kyverum: score 12\"}")]
    [InlineData("{\"motivo\":\"revisión manual del proveedor\"}")]
    [InlineData("not json")]
    [InlineData(null)]
    public void Origen_desconocido_o_con_marcas_cae_al_generico_sin_palabras_prohibidas(string? detail)
    {
        var motivo = IdentityRejectionReasonText.For(Rechazada(detail));

        motivo.Should().Be(IdentityRejectionReasonText.Generico);
        IdentityRejectionReasonText.Prohibidas.Should().OnlyContain(p =>
            !motivo!.Contains(p, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ninguna_etiqueta_de_la_lista_cerrada_ni_el_texto_traducido_contiene_palabras_prohibidas()
    {
        foreach (var r in ManualRejectionReasons.Todos)
        {
            var t = IdentityRejectionReasonText.For(Rechazada(codigo: r.Code))!;
            IdentityRejectionReasonText.Limpio(t).Should().BeTrue($"el código {r.Code} no debe delatar el mecanismo");
        }
    }
}
