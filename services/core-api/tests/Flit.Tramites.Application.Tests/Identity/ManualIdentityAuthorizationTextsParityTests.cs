using System.Text.RegularExpressions;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// HU #14301 — el texto que la carta de FLIT imprime como «aceptado» es el que la persona vio en la captura manual. No hay una fuente
/// única compartida entre el front (<c>frontend/lib/captura-manual/consent.ts</c>) y el servidor; esta prueba falla si divergen, o si
/// cambia la versión vigente sin agregar su entrada a <see cref="ManualIdentityAuthorizationTexts"/>.
/// </summary>
public sealed class ManualIdentityAuthorizationTextsParityTests
{
    private static string? FindConsentFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "frontend", "lib", "captura-manual", "consent.ts");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string Const(string source, string name)
    {
        var m = Regex.Match(
            source, $@"export const {name}\s*=\s*""((?:[^""\\]|\\.)*)""", RegexOptions.None, TimeSpan.FromSeconds(5));
        m.Success.Should().BeTrue($"consent.ts debe exportar {name} como literal");
        return m.Groups[1].Value;
    }

    [Fact]
    public void La_version_vigente_del_servidor_tiene_texto_conocido()
    {
        ManualIdentityAuthorizationTexts.For(ManualCaptureConsent.TextVersion).Should().NotBeNull(
            "al subir la versión del consentimiento hay que agregar su texto para la carta");
    }

    [Fact]
    public void Una_version_desconocida_no_se_sustituye_por_otro_texto()
    {
        ManualIdentityAuthorizationTexts.For("manual-ley1581-v999").Should().BeNull();
        ManualIdentityAuthorizationTexts.For(null).Should().BeNull();
    }

    [Fact]
    public void Los_textos_del_servidor_son_literalmente_los_del_front()
    {
        var path = FindConsentFile();
        if (path is null)
        {
            Assert.Skip("No hay checkout del front junto al backend; la paridad se verifica en el CI del monorepo.");
            return;
        }

        var source = File.ReadAllText(path);
        var version = Const(source, "RENDERED_CONSENT_TEXT_VERSION");
        var texts = ManualIdentityAuthorizationTexts.For(version);

        texts.Should().NotBeNull($"el front renderiza la versión {version} y el servidor no tiene su texto");
        version.Should().Be(ManualCaptureConsent.TextVersion);
        texts!.ConsentBody.Should().Be(Const(source, "CONSENT_TEXT_BODY"));
        texts.PrivacyLabel.Should().Be(Const(source, "CONSENT_PRIVACY_LABEL"));
        texts.AuthorizationTitle.Should().Be(Const(source, "AUTORIZACION_FIRMA_TITLE"));
        texts.AuthorizationText.Should().Be(Const(source, "AUTORIZACION_FIRMA_TEXT"));
    }
}
