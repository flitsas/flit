using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Infrastructure.Notifications.Tramites;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications;

/// <summary>
/// Bug #13194 punto 5 — en QA/PDN ningún correo puede salir con URLs de <c>dev.flitsas.online</c>:
/// los composers toman la base de assets de <see cref="NotificationEmailAssetsOptions"/>
/// (<c>Notifications:EmailAssets:BaseUrl</c>) y, sin configuración, caen a un respaldo local
/// documentado — nunca a DEV.
/// <para>
/// Uso de ejemplo:
/// <code>
/// var url = TramiteCambioEstadoEmailComposer.ResolveLogoUrl(options.BaseUrl);
/// // https://qa.flitsas.online/email-assets/flit-logo.png
/// </code>
/// </para>
/// </summary>
public sealed class Bug13194EmailUrlsSinDevTests
{
    private const string Dev = "dev.flitsas.online";
    private const string LocalFallback = "http://localhost:3000/email-assets";
    private static readonly NotificationEmailAssetsOptions QaOptions = new() { BaseUrl = "https://qa.flitsas.online/email-assets" };

    [Fact]
    public void TramiteCambioEstado_usa_la_base_de_las_options_y_sin_base_no_cae_a_dev()
    {
        TramiteCambioEstadoEmailComposer.ResolveLogoUrl(QaOptions.BaseUrl)
            .Should().Be("https://qa.flitsas.online/email-assets/flit-logo.png");
        TramiteCambioEstadoEmailComposer.ResolveHeaderUrl(QaOptions.BaseUrl)
            .Should().StartWith("https://qa.flitsas.online/email-assets/");

        TramiteCambioEstadoEmailComposer.ResolveLogoUrl("").Should().NotContain(Dev).And.StartWith(LocalFallback);
        TramiteCambioEstadoEmailComposer.ResolveRentingFooterUrl("  ").Should().NotContain(Dev).And.StartWith(LocalFallback);
    }

    [Fact]
    public void AsignacionPlaca_usa_la_base_de_las_options_y_sin_base_no_cae_a_dev()
    {
        AsignacionPlacaEmailComposer.ResolveFlitLogoUrl(QaOptions.BaseUrl)
            .Should().StartWith("https://qa.flitsas.online/email-assets/");
        AsignacionPlacaEmailComposer.ResolveFlitHeaderUrl(QaOptions.BaseUrl)
            .Should().StartWith("https://qa.flitsas.online/email-assets/");

        AsignacionPlacaEmailComposer.ResolveFlitLogoUrl("").Should().NotContain(Dev).And.StartWith(LocalFallback);
        AsignacionPlacaEmailComposer.ResolveRentingHeaderUrl("").Should().NotContain(Dev).And.StartWith(LocalFallback);
    }

    [Fact]
    public void Defaults_de_options_y_muestras_no_apuntan_a_dev()
    {
        new NotificationEmailAssetsOptions().BaseUrl.Should().NotContain(Dev);
        new EmailThemePublicBrandingOptions().PublicBaseUrl.Should().NotContain(Dev);
        TramiteEmailPreviewSample.DefaultAssetsBaseUrl.Should().NotContain(Dev);
        AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl.Should().NotContain(Dev);
        RevocationRequestEmailPreviewSample.DefaultAssetsBaseUrl.Should().NotContain(Dev);
    }

    [Fact]
    public void Ningun_cs_de_src_contiene_el_literal_de_dev()
    {
        var src = FindCoreApiSrc();
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(Dev, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();

        offenders.Should().BeEmpty("las URLs por ambiente salen de configuración (Bug #13194)");
    }

    private static string FindCoreApiSrc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Infrastructure");
            if (Directory.Exists(candidate))
                return Path.Combine(dir.FullName, "src");
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró services/core-api/src desde el directorio de tests.");
    }
}
