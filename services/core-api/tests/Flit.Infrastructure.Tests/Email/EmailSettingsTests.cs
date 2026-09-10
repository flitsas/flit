using Flit.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Infrastructure.Tests.Email;

/// <summary>
/// Cubre <see cref="EmailSettings.CheckCertificateRevocation"/>: el defecto debe seguir siendo
/// <c>true</c> tanto al instanciar la clase directamente como al enlazarla desde configuración
/// (un despliegue que no defina la clave no debe quedar sin comprobación de revocación).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var configuration = new ConfigurationBuilder()
///     .AddInMemoryCollection(new Dictionary&lt;string, string?&gt; { ["Smtp:Host"] = "smtp.test" })
///     .Build();
/// var settings = configuration.GetSection(EmailSettings.SectionName).Get&lt;EmailSettings&gt;();
/// </code>
/// </remarks>
public sealed class EmailSettingsTests
{
    [Fact]
    public void CheckCertificateRevocation_PorDefecto_EsTrue()
    {
        var settings = new EmailSettings();

        settings.CheckCertificateRevocation.Should().BeTrue();
    }

    [Fact]
    public void Enlazado_ConSeccionQueDefineFalse_ProduceFalse()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.office365.com",
            ["Smtp:CheckCertificateRevocation"] = "false",
        });

        var settings = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>();

        settings.Should().NotBeNull();
        settings!.CheckCertificateRevocation.Should().BeFalse();
    }

    [Fact]
    public void Enlazado_ConSeccionQueNoTraeLaClave_ProduceTrue()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.office365.com",
            ["Smtp:Port"] = "587",
        });

        var settings = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>();

        settings.Should().NotBeNull();
        settings!.CheckCertificateRevocation.Should().BeTrue();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
