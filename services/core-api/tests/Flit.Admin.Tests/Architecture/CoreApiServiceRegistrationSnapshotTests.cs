using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// Epic #13217 (HU #13231/#13232) — el registro de servicios de core-api se parte para que core-identity reuse solo lo de
/// identidad. Esta prueba congela, por tipo de servicio, la secuencia de implementaciones y su ciclo de vida: mover un
/// bloque de registro de archivo no puede cambiar qué recibe cada consumidor (el último registro gana y los
/// <c>IEnumerable</c> respetan el orden por tipo). Si un cambio es intencional, regenerar con
/// <c>FLIT_UPDATE_DI_SNAPSHOT=1</c> y revisar el diff en el PR.
/// </summary>
public sealed class CoreApiServiceRegistrationSnapshotTests
{
    private const string SnapshotFile = "core-api-services.snapshot.txt";

    [Fact]
    public void ElRegistroDeServiciosDeCoreApi_NoCambia()
    {
        IReadOnlyList<ServiceDescriptor> captured = [];
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(services => captured = [.. services]));
        _ = factory.Services; // construye el host

        var actual = Describe(captured);
        var path = Path.Combine(SourceDirectory(), SnapshotFile);
        if (Environment.GetEnvironmentVariable("FLIT_UPDATE_DI_SNAPSHOT") == "1")
        {
            File.WriteAllLines(path, actual);
            return;
        }

        File.Exists(path).Should().BeTrue($"falta {SnapshotFile}; generarlo con FLIT_UPDATE_DI_SNAPSHOT=1");
        actual.Should().Equal(File.ReadAllLines(path), "el registro de servicios de core-api cambió");
    }

    /// <summary>Una línea por registro, agrupadas por tipo de servicio (orden estable) y en el orden de registro dentro del tipo.</summary>
    private static string[] Describe(IEnumerable<ServiceDescriptor> descriptors) =>
        descriptors
            .Select((d, index) => (d, index))
            .GroupBy(x => Name(x.d.ServiceType) + (x.d.IsKeyedService ? $"[{x.d.ServiceKey}]" : string.Empty))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .SelectMany(g => g.OrderBy(x => x.index).Select(x => $"{g.Key} => {Implementation(x.d)} ({x.d.Lifetime})"))
            .ToArray();

    private static string Implementation(ServiceDescriptor d)
    {
        if (d.IsKeyedService)
        {
            return d.KeyedImplementationType is { } keyedType ? Name(keyedType) : d.KeyedImplementationInstance is { } ki ? $"instance:{Name(ki.GetType())}" : "factory";
        }

        return d.ImplementationType is { } type ? Name(type) : d.ImplementationInstance is { } instance ? $"instance:{Name(instance.GetType())}" : "factory";
    }

    /// <summary>Nombre legible y estable (sin versiones de ensamblado en los argumentos genéricos).</summary>
    private static string Name(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition();
        var baseName = (definition.FullName ?? definition.Name).Split('`')[0];
        return type.IsGenericTypeDefinition
            ? $"{baseName}<{new string(',', type.GetGenericArguments().Length - 1)}>"
            : $"{baseName}<{string.Join(",", type.GetGenericArguments().Select(Name))}>";
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
