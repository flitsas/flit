using System.Reflection;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Preview;

/// <summary>
/// HU #11354 — muestras con marcadores visibles para las 3 plantillas de correo de Seguridad.
///
/// Uso de ejemplo:
/// <code>
/// var composed = SecurityEmailPreviewSample.BuildInvitation();
/// // composed.HtmlBody contiene "[ACÁ VA EL NOMBRE DEL DESTINATARIO]", nunca un nombre real
/// </code>
/// </summary>
public sealed class SecurityEmailPreviewSampleTests
{
    // Los mismos valores fijos que usa el golden de producción (SecurityEmailGoldenTests,
    // HU #11350) — así el body "de producción" que calculamos aquí es exactamente el que el
    // golden congela, sin duplicar HTML a mano ni depender de leer el .golden.txt de otro
    // proyecto de pruebas.
    private const string ActivateUrlBase = "https://app.flit.test/invite/activate";
    private const string ResetUrlBase = "https://app.flit.test/password/reset";
    private const string GoldenName = "Nombre De Prueba";
    private const string GoldenRawToken = "GOLDEN-TOKEN-0000";
    private const string GoldenTemporaryPassword = "GoldenTemp0000!";

    // Los composers de producción pasan nombre/contraseña por HtmlEncode antes de embeberlos —
    // los marcadores llevan tildes y "Ñ", así que en el HTML final aparecen como entidades
    // (p. ej. "&#193;"), no como el literal con tilde.
    private static readonly string EncodedPhNombreDestinatario =
        System.Net.WebUtility.HtmlEncode(SecurityEmailPreviewSample.PhNombreDestinatario);
    private static readonly string EncodedPhContrasenaTemporal =
        System.Net.WebUtility.HtmlEncode(SecurityEmailPreviewSample.PhContrasenaTemporal);

    // --- AC1: marcadores, no identidades inventadas ---------------------------------------

    [Fact]
    public void Invitation_no_contiene_nombre_link_ni_token_reales()
    {
        var composed = SecurityEmailPreviewSample.BuildInvitation();

        composed.HtmlBody.Should().Contain(EncodedPhNombreDestinatario);
        composed.HtmlBody.Should().NotContain(GoldenName);
        composed.HtmlBody.Should().NotContain(GoldenRawToken);
    }

    [Fact]
    public void ForgotPassword_no_contiene_nombre_link_ni_token_reales()
    {
        var composed = SecurityEmailPreviewSample.BuildForgotPassword();

        composed.HtmlBody.Should().Contain(EncodedPhNombreDestinatario);
        composed.HtmlBody.Should().NotContain(GoldenName);
        composed.HtmlBody.Should().NotContain(GoldenRawToken);
    }

    [Fact]
    public void AdminResetPassword_no_contiene_nombre_ni_contrasena_reales()
    {
        var composed = SecurityEmailPreviewSample.BuildAdminResetPassword();

        composed.HtmlBody.Should().Contain(EncodedPhNombreDestinatario);
        composed.HtmlBody.Should().Contain(EncodedPhContrasenaTemporal);
        composed.HtmlBody.Should().NotContain(GoldenName);
        composed.HtmlBody.Should().NotContain(GoldenTemporaryPassword);
    }

    /// <summary>
    /// AC1 — recorre TODOS los marcadores públicos (`Ph*`) por reflexión y falla si alguno no
    /// respeta la convención "[ACÁ VA ...]" en mayúsculas o si tiene pinta de dato personal
    /// (nombre propio con may/min mixtas, correo, o secuencia de dígitos como un documento).
    /// </summary>
    [Fact]
    public void Todos_los_marcadores_respetan_la_convencion_y_no_parecen_datos_personales()
    {
        var marcadores = typeof(SecurityEmailPreviewSample)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.Name.StartsWith("Ph", StringComparison.Ordinal) && f.FieldType == typeof(string))
            .Select(f => (Nombre: f.Name, Valor: (string)f.GetValue(null)!))
            .ToList();

        marcadores.Should().NotBeEmpty();

        foreach (var (nombre, valor) in marcadores)
        {
            valor.Should().MatchRegex(@"^\[ACÁ VA .+\]$", $"el marcador {nombre} debe seguir la convención de MandatoPreviewSample");
            valor.Should().Be(valor.ToUpperInvariant(), $"el marcador {nombre} debe estar íntegramente en mayúsculas");
            valor.Should().NotMatchRegex(@"[a-zñáéíóú]{2,}", $"el marcador {nombre} no debe contener texto en minúsculas (pinta de nombre propio)");
            valor.Should().NotMatchRegex(@"\d{5,}", $"el marcador {nombre} no debe contener una secuencia numérica larga (pinta de documento/teléfono)");
            valor.Should().NotContain("@", $"el marcador {nombre} no debe parecer un correo electrónico");
        }
    }

    // --- AC2: la contraseña nunca se genera para una muestra -------------------------------

    [Fact]
    public void AdminResetPassword_no_conoce_ITemporaryPasswordGenerator()
    {
        // El tipo de muestra no debe siquiera referenciar la interfaz de generación de
        // contraseñas: se verifica que ninguno de sus miembros la usa como parámetro/campo.
        var tipo = typeof(SecurityEmailPreviewSample);

        var referenciaGenerador = tipo
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(m => m.GetParameters())
            .Any(p => p.ParameterType.Name.Contains("TemporaryPasswordGenerator", StringComparison.Ordinal));

        referenciaGenerador.Should().BeFalse("BuildAdminResetPassword no debe recibir ni invocar ITemporaryPasswordGenerator");
    }

    [Fact]
    public void AdminResetPassword_marcador_de_contrasena_es_estable_entre_llamadas()
    {
        // Si internamente se generara una contraseña "temporal" para la muestra, dos llamadas
        // sucesivas producirían cuerpos distintos. Al ser un marcador fijo, son idénticas.
        var primera = SecurityEmailPreviewSample.BuildAdminResetPassword();
        var segunda = SecurityEmailPreviewSample.BuildAdminResetPassword();

        primera.Should().Be(segunda);
    }

    // --- AC3: misma función Compose que producción ------------------------------------------

    [Fact]
    public void Invitation_muestra_y_golden_de_produccion_difieren_solo_en_los_valores_sustituidos()
    {
        // "Golden de producción": el mismo Compose de producción, con los mismos valores fijos
        // que congela SecurityEmailGoldenTests (HU #11350) para esta plantilla.
        var linkProduccion = InvitationEmailTemplate.BuildActivateLink(ActivateUrlBase, GoldenRawToken);
        var produccion = InvitationEmailTemplate.Compose(GoldenName, linkProduccion);

        var muestra = SecurityEmailPreviewSample.BuildInvitation();
        var linkMuestra = InvitationEmailTemplate.BuildActivateLink(ActivateUrlBase, SecurityEmailPreviewSample.PhToken);

        // Sustituyendo en el cuerpo de producción el nombre y el link por sus equivalentes de
        // muestra, debe obtenerse EXACTAMENTE el cuerpo de la muestra: la estructura HTML
        // invariante (saludo, párrafos, enlace <a>, despedida) es la misma en ambos.
        var esperado = produccion.HtmlBody
            .Replace(GoldenName, EncodedPhNombreDestinatario, StringComparison.Ordinal)
            .Replace(linkProduccion, linkMuestra, StringComparison.Ordinal);

        esperado.Should().Be(muestra.HtmlBody);
        muestra.Subject.Should().Be(produccion.Subject, "el asunto no depende de datos variables, debe ser idéntico");
    }

    [Fact]
    public void ForgotPassword_muestra_y_golden_de_produccion_difieren_solo_en_los_valores_sustituidos()
    {
        var linkProduccion = ForgotPasswordEmailTemplate.BuildResetLink(ResetUrlBase, GoldenRawToken);
        var produccion = ForgotPasswordEmailTemplate.Compose(GoldenName, linkProduccion, 30);

        var muestra = SecurityEmailPreviewSample.BuildForgotPassword(30);
        var linkMuestra = ForgotPasswordEmailTemplate.BuildResetLink(ResetUrlBase, SecurityEmailPreviewSample.PhToken);

        var esperado = produccion.HtmlBody
            .Replace(GoldenName, EncodedPhNombreDestinatario, StringComparison.Ordinal)
            .Replace(linkProduccion, linkMuestra, StringComparison.Ordinal);

        esperado.Should().Be(muestra.HtmlBody);
        muestra.Subject.Should().Be(produccion.Subject);
    }

    [Fact]
    public void AdminResetPassword_muestra_y_golden_de_produccion_difieren_solo_en_los_valores_sustituidos()
    {
        var produccion = AdminResetPasswordEmailTemplate.Compose(GoldenName, GoldenTemporaryPassword);

        var muestra = SecurityEmailPreviewSample.BuildAdminResetPassword();

        var esperado = produccion.HtmlBody
            .Replace(GoldenName, EncodedPhNombreDestinatario, StringComparison.Ordinal)
            .Replace(GoldenTemporaryPassword, EncodedPhContrasenaTemporal, StringComparison.Ordinal);

        esperado.Should().Be(muestra.HtmlBody);
        muestra.Subject.Should().Be(produccion.Subject);
    }

    [Fact]
    public void Las_tres_muestras_usan_el_Subject_publico_de_su_composer_de_produccion()
    {
        // Refuerza AC3 desde otro ángulo: el asunto de la muestra es literalmente la constante
        // pública del composer de producción, no un texto reescrito en la clase de muestra.
        SecurityEmailPreviewSample.BuildInvitation().Subject.Should().Be(InvitationEmailTemplate.Subject);
        SecurityEmailPreviewSample.BuildForgotPassword().Subject.Should().Be(ForgotPasswordEmailTemplate.Subject);
        SecurityEmailPreviewSample.BuildAdminResetPassword().Subject.Should().Be(AdminResetPasswordEmailTemplate.Subject);
        SecurityEmailPreviewSample.BuildWelcomeRegistration().Subject.Should().Be(WelcomeRegistrationEmailTemplate.Subject);
    }

    [Fact]
    public void WelcomeRegistration_muestra_enlaza_login_principal_y_usa_shell_flit()
    {
        var muestra = SecurityEmailPreviewSample.BuildWelcomeRegistration();

        muestra.HtmlBody.Should().Contain(WelcomeRegistrationEmailTemplate.DefaultLoginUrl);
        muestra.HtmlBody.Should().Contain("tramite-cambio-estado-header.png");
        muestra.HtmlBody.Should().Contain("flit-logo.png");
        muestra.HtmlBody.Should().Contain("GRACIAS POR REGISTRARTE");
    }
}
