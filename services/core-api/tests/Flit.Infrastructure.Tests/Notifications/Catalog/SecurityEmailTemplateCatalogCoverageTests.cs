using System.Reflection;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Modules.Security.Application.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Catalog;

/// <summary>
/// Guardarraíl contra la regresión descrita en el AC3 de
/// <see cref="Flit.Infrastructure.Notifications.Routing.TenantChannelEmailRouter"/>: los correos de
/// cuenta (invitación, recuperación, reset administrativo, bienvenida) SIEMPRE deben salir por el
/// SMTP de FLIT, sin importar el canal del tenant. Esa excepción se decide únicamente por
/// <c>descriptor.Module == NotificationModule.Security</c> — si mañana alguien agrega un correo de
/// cuenta nuevo y olvida darlo de alta en <see cref="NotificationTemplateCatalog"/>, el bypass no se
/// activa: el correo cae al camino normal y puede salir por la API de Renting (un tercero) en la
/// ruta de acceso a la plataforma. Hoy no hay defecto — las 4 plantillas de Seguridad están
/// catalogadas; esta prueba existe para que el próximo desarrollador no tenga que acordarse.
/// </summary>
/// <remarks>
/// <para>
/// <b>Criterio de descubrimiento (por reflexión, no lista escrita a mano).</b> Se escanea el
/// ensamblado de <c>Flit.Modules.Security.Application</c> (el mismo que <see cref="ComposedEmail"/>)
/// buscando clases <c>static</c> (en IL: <c>abstract sealed</c>) cuyo nombre termina en
/// <c>"EmailTemplate"</c> y que exponen un método público <c>static</c> llamado <c>Compose</c> cuyo
/// tipo de retorno es <see cref="ComposedEmail"/>. Es la firma estructural que comparten HOY las
/// cuatro plantillas de cuenta (<see cref="InvitationEmailTemplate"/>,
/// <see cref="Flit.Modules.Security.Application.Auth.ForgotPassword.ForgotPasswordEmailTemplate"/>,
/// <see cref="Flit.Modules.Security.Application.Auth.AdminResetPassword.AdminResetPasswordEmailTemplate"/>,
/// <see cref="WelcomeRegistrationEmailTemplate"/>) y NINGUNA otra clase del ensamblado.
/// </para>
/// <para>
/// <b>Por qué este criterio y no otro.</b> Se descartó filtrar por <c>NotificationTrigger</c> (el
/// enum no distingue "de cuenta" de otro tipo) y por convención de <em>namespace</em> (las cuatro
/// plantillas viven en tres namespaces distintos bajo <c>Auth</c>, no uno común). El sufijo
/// <c>EmailTemplate</c> + método <c>Compose(...): ComposedEmail</c> es la única característica
/// estructural 100% compartida y verificable sin acoplarse a IDs de catálogo (que son
/// deliberadamente literales, ver <see cref="NotificationTemplateDescriptor"/>).
/// </para>
/// <para>
/// <b>Qué NO detecta este test (documentado a propósito, no es un hueco silencioso):</b>
/// </para>
/// <list type="bullet">
/// <item>Una plantilla de cuenta compuesta INLINE dentro de un handler, sin una clase
/// <c>*EmailTemplate</c> separada (ninguna lo hace hoy — HU #11351 las extrajo todas).</item>
/// <item>Una plantilla de cuenta ubicada en OTRO ensamblado distinto de
/// <c>Flit.Modules.Security.Application</c> (hoy todas viven ahí).</item>
/// <item>Una plantilla cuyo método de composición no se llame <c>Compose</c> o no devuelva
/// <see cref="ComposedEmail"/> (rompería la convención que las cuatro comparten hoy).</item>
/// <item>Que el catálogo asigne <see cref="NotificationModule.Security"/> a una plantilla que en
/// realidad NO es de cuenta (falso positivo del lado del catálogo, no cubierto aquí).</item>
/// </list>
/// <para>
/// Uso de ejemplo (cómo se vería una plantilla nueva bien registrada):
/// <code>
/// public static class MfaChallengeEmailTemplate
/// {
///     public static ComposedEmail Compose(string code) => new("...", "...");
/// }
/// // NotificationTemplateCatalog.cs:
/// //   TemplateIds.MfaChallenge = "security.mfa-challenge";
/// //   new NotificationTemplateDescriptor(TemplateIds.MfaChallenge, "...", NotificationModule.Security, [...])
/// </code>
/// </para>
/// </remarks>
public class SecurityEmailTemplateCatalogCoverageTests
{
    [Fact]
    public void CadaPlantillaDeCuentaDescubiertaPorReflexion_EstaRegistradaEnElCatalogoComoSecurity()
    {
        var discovered = DiscoverAccountEmailTemplateTypes();

        // Ver los 4 tipos que este ensamblado expone hoy (comentario, no assert — el assert real
        // vive por-tipo abajo, para que el mensaje de fallo señale la clase huérfana exacta).
        discovered.Should().NotBeEmpty(
            "el descubrimiento por reflexión no debería quedar vacío mientras existan plantillas "
                + "*EmailTemplate con Compose(...): ComposedEmail en Flit.Modules.Security.Application "
                + "— si esto falla, el criterio de descubrimiento dejó de encontrar las plantillas "
                + "reales y hay que revisarlo, no solo el catálogo");

        foreach (var templateType in discovered)
        {
            var expectedId = ToExpectedTemplateId(templateType.Name);
            var isRegisteredAsSecurity = NotificationTemplateCatalog.TryResolve(expectedId, out var descriptor)
                && descriptor.Module == NotificationModule.Security;

            isRegisteredAsSecurity.Should().BeTrue(
                $"la plantilla de correo de CUENTA '{templateType.FullName}' (id esperado por "
                    + $"convención: '{expectedId}') no está registrada en "
                    + $"NotificationTemplateCatalog.All con NotificationModule.Security. "
                    + "Consecuencia: TenantChannelEmailRouter.IsAccountEmail devolvería FALSE para "
                    + "este correo, así que NO se enrutaría por el SMTP de FLIT (AC3) sino por el "
                    + "canal configurado del tenant — pudiendo salir por la API de Renting, exactamente "
                    + "el punto único de fallo de un tercero que la decisión de producto quiso evitar "
                    + "en la ruta de acceso a la plataforma. Corrección: agrega una entrada en "
                    + "services/core-api/src/Flit.Infrastructure/Notifications/Catalog/"
                    + "NotificationTemplateCatalog.cs (nuevo literal en TemplateIds + nueva entrada en "
                    + $"All) con Id = \"{expectedId}\" y Module = NotificationModule.Security.");
        }
    }

    [Fact]
    public void ElNumeroDePlantillasDeCuentaDescubiertas_CoincideConLasEntradasSecurityDelCatalogo()
    {
        // Chequeo de redundancia deliberada: además de que cada plantilla individual resuelva
        // (Fact anterior), el CONTEO debe cuadrar — así una entrada del catálogo marcada Security
        // "de más" (sin clase *EmailTemplate real detrás) también se nota, aunque no sea el foco
        // principal de este guardarraíl (ver limitación documentada en el remarks de la clase).
        var discoveredCount = DiscoverAccountEmailTemplateTypes().Count;
        var catalogSecurityCount = NotificationTemplateCatalog.All.Count(t => t.Module == NotificationModule.Security);

        catalogSecurityCount.Should().Be(
            discoveredCount,
            "el número de plantillas *EmailTemplate descubiertas por reflexión en "
                + "Flit.Modules.Security.Application debe coincidir con el número de entradas "
                + "Module=Security en NotificationTemplateCatalog.All; una diferencia indica una "
                + "plantilla de cuenta sin catalogar (o una entrada de catálogo sin plantilla real)");
    }

    /// <summary>
    /// Descubrimiento estructural: clases <c>static</c> (IL: <c>abstract sealed</c>) cuyo nombre
    /// termina en <c>EmailTemplate</c> y exponen <c>public static ComposedEmail Compose(...)</c>,
    /// dentro del ensamblado dueño de las plantillas de Seguridad.
    /// </summary>
    private static List<Type> DiscoverAccountEmailTemplateTypes()
    {
        var securityApplicationAssembly = typeof(ComposedEmail).Assembly;

        return [.. securityApplicationAssembly.GetTypes()
            .Where(t => t.IsClass
                && t.IsAbstract
                && t.IsSealed // "static class" en C#
                && t.IsPublic
                && t.Name.EndsWith("EmailTemplate", StringComparison.Ordinal)
                && t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Any(m => m.Name == "Compose" && m.ReturnType == typeof(ComposedEmail)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Convención verificada hoy contra las 4 plantillas reales: <c>{Nombre}EmailTemplate</c> →
    /// <c>security.{nombre-en-kebab-case}</c>. Ej.: <c>AdminResetPasswordEmailTemplate</c> →
    /// <c>security.admin-reset-password</c>.
    /// </summary>
    private static string ToExpectedTemplateId(string typeName)
    {
        const string suffix = "EmailTemplate";
        var baseName = typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName[..^suffix.Length]
            : typeName;

        var kebab = Regex.Replace(baseName, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
        return $"security.{kebab}";
    }
}
