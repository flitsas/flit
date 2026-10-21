using System.Runtime.CompilerServices;

namespace Flit.Integration.Tests;

/// <summary>
/// HU #13348 (Epic #13316): core-api llama a core-consultas siempre, así que el host exige el secreto del cliente de
/// servicio svc-tramites al arrancar. En las pruebas basta uno cualquiera: ninguna llama al servicio de verdad.
/// </summary>
internal static class ServiciosDePlataformaDePrueba
{
    [ModuleInitializer]
    public static void ServiceClientDePrueba()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Platform__ServiceClient__ClientSecret")))
            Environment.SetEnvironmentVariable("Platform__ServiceClient__ClientSecret", "secreto-de-pruebas");
    }

    /// <summary>
    /// Los controles de permisos vienen encendidos por defecto en la API. Las pruebas que no los ejercitan se
    /// escribieron con el comportamiento previo (solo la policy de rol): aquí arrancan apagados y las pruebas de exigencia
    /// los encienden explícitamente con <c>UseSetting</c>/configuración en memoria, que tiene prioridad sobre estas variables.
    /// </summary>
    [ModuleInitializer]
    public static void ControlesDePermisosApagadosSalvoQueLaPruebaLosEncienda()
    {
        Environment.SetEnvironmentVariable("Security__OtPermissionEnforcement__Enabled", "false");
        Environment.SetEnvironmentVariable("Security__ModulePermissionEnforcement__Enabled", "false");
    }
}
