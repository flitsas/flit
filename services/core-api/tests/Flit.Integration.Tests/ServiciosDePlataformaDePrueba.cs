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
}
