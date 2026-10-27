using System.Text.Json;
using Flit.Api.Authorization;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Épica #12753 — decisión del usuario (2026-10-21): los controles de permisos de módulos y OT vienen encendidos por
/// defecto; apagarlos es el freno de emergencia. Las pruebas los apagan en <c>TestEnvironment</c>, así que esta verifica
/// el valor real que se despliega (appsettings.json) y el de las clases de opciones cuando la clave falta.
/// </summary>
public sealed class PermissionEnforcementDefaultsTests
{
    [Fact]
    public void Las_opciones_vienen_encendidas_si_la_configuracion_no_dice_nada()
    {
        new ModulePermissionEnforcementOptions().Enabled.Should().BeTrue();
        new OtPermissionEnforcementOptions().Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("ModulePermissionEnforcement")]
    [InlineData("OtPermissionEnforcement")]
    public void El_appsettings_que_se_despliega_las_trae_encendidas(string seccion)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(AppSettingsPath()));
        json.RootElement.GetProperty("Security").GetProperty(seccion).GetProperty("Enabled").GetBoolean().Should().BeTrue();
    }

    private static string AppSettingsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Api", "appsettings.json");
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException("No se encontró src/Flit.Api/appsettings.json desde la carpeta de pruebas.");
    }
}
