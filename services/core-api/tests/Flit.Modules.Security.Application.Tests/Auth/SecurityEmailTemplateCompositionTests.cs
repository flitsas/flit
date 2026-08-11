using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #11351 — cubre la composición PURA de las 3 plantillas de correo de Seguridad: misma
/// entrada produce siempre la misma salida (AC1), y la del reset administrativo jamás genera
/// la contraseña temporal —la recibe como argumento— (AC5). El asunto/cuerpo carácter a
/// carácter ya está protegido por los golden de <see cref="SecurityEmailGoldenTests"/> (HU
/// #11350, AC2); estas pruebas no duplican ese contrato, solo la propiedad de pureza.
///
/// Uso de ejemplo:
///   InvitationEmailTemplate.Compose(fullName, link) → ComposedEmail(Subject, HtmlBody)
///   ForgotPasswordEmailTemplate.Compose(displayName, link, lifetimeMinutes) → ComposedEmail
///   AdminResetPasswordEmailTemplate.Compose(displayName, temporaryPassword) → ComposedEmail
/// </summary>
public sealed class SecurityEmailTemplateCompositionTests
{
    // --- AC1: InvitationEmailTemplate -----------------------------------------------------

    [Fact]
    public void Invitation_Compose_es_pura_misma_entrada_misma_salida()
    {
        var first = InvitationEmailTemplate.Compose("Nombre De Prueba", "https://app.flit.test/invite/activate?token=abc");
        var second = InvitationEmailTemplate.Compose("Nombre De Prueba", "https://app.flit.test/invite/activate?token=abc");

        first.Should().Be(second);
    }

    [Fact]
    public void Invitation_Compose_conserva_el_asunto_actual()
    {
        var composed = InvitationEmailTemplate.Compose("Nombre De Prueba", "https://app.flit.test/invite/activate?token=abc");

        composed.Subject.Should().Be(InvitationEmailTemplate.Subject);
    }

    [Fact]
    public void Invitation_Compose_incluye_nombre_y_enlace_en_el_cuerpo()
    {
        const string link = "https://app.flit.test/invite/activate?token=xyz";

        var composed = InvitationEmailTemplate.Compose("Ana Perez", link);

        composed.HtmlBody.Should().Contain("Ana Perez").And.Contain(link);
    }

    // --- AC1: ForgotPasswordEmailTemplate -------------------------------------------------

    [Fact]
    public void ForgotPassword_Compose_es_pura_misma_entrada_misma_salida()
    {
        var first = ForgotPasswordEmailTemplate.Compose("Nombre De Prueba", "https://app.flit.test/password/reset?token=abc", 30);
        var second = ForgotPasswordEmailTemplate.Compose("Nombre De Prueba", "https://app.flit.test/password/reset?token=abc", 30);

        first.Should().Be(second);
    }

    [Fact]
    public void ForgotPassword_Compose_conserva_el_asunto_actual()
    {
        var composed = ForgotPasswordEmailTemplate.Compose("Nombre De Prueba", "https://app.flit.test/password/reset?token=abc", 30);

        composed.Subject.Should().Be("Recuperación de contraseña — FLIT");
    }

    [Fact]
    public void ForgotPassword_Compose_incluye_enlace_y_TTL_en_el_cuerpo()
    {
        const string link = "https://app.flit.test/password/reset?token=xyz";

        var composed = ForgotPasswordEmailTemplate.Compose("Nombre De Prueba", link, 45);

        composed.HtmlBody.Should().Contain(link).And.Contain("45 minutos");
    }

    [Fact]
    public void ForgotPassword_BuildResetLink_es_pura_misma_entrada_misma_salida()
    {
        var first = ForgotPasswordEmailTemplate.BuildResetLink("https://app.flit.test/password/reset", "GOLDEN-TOKEN-0000");
        var second = ForgotPasswordEmailTemplate.BuildResetLink("https://app.flit.test/password/reset", "GOLDEN-TOKEN-0000");

        first.Should().Be(second);
    }

    // --- AC1 + AC5: AdminResetPasswordEmailTemplate ---------------------------------------

    [Fact]
    public void AdminResetPassword_Compose_es_pura_misma_entrada_misma_salida()
    {
        var first = AdminResetPasswordEmailTemplate.Compose("Nombre De Prueba", "GoldenTemp0000!");
        var second = AdminResetPasswordEmailTemplate.Compose("Nombre De Prueba", "GoldenTemp0000!");

        first.Should().Be(second);
    }

    [Fact]
    public void AdminResetPassword_Compose_conserva_el_asunto_actual()
    {
        var composed = AdminResetPasswordEmailTemplate.Compose("Nombre De Prueba", "GoldenTemp0000!");

        composed.Subject.Should().Be("Tu contraseña fue restablecida — FLIT");
    }

    [Fact]
    public void AdminResetPassword_Compose_devuelve_la_contrasena_recibida_sin_generar_una_nueva()
    {
        // AC5 — invocada dos veces con LA MISMA contraseña temporal (ya generada por el
        // llamante), debe devolver exactamente el mismo cuerpo: la función nunca llama a
        // ITemporaryPasswordGenerator ni produce una contraseña distinta por su cuenta.
        const string temporaryPassword = "GoldenTemp0000!";

        var first = AdminResetPasswordEmailTemplate.Compose("Nombre De Prueba", temporaryPassword);
        var second = AdminResetPasswordEmailTemplate.Compose("Nombre De Prueba", temporaryPassword);

        first.HtmlBody.Should().Be(second.HtmlBody);
        first.HtmlBody.Should().Contain(temporaryPassword);
    }

    [Fact]
    public void AdminResetPassword_Compose_no_altera_la_contrasena_temporal_recibida()
    {
        const string temporaryPassword = "OtraTemp9999!";

        var composed = AdminResetPasswordEmailTemplate.Compose("Nombre De Prueba", temporaryPassword);

        // La contraseña que aparece en el cuerpo es EXACTAMENTE la recibida como argumento,
        // no una generada internamente.
        composed.HtmlBody.Should().Contain(temporaryPassword);
    }
}
