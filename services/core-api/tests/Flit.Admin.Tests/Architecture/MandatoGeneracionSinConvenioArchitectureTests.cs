using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #14358 AC9 — generar el contrato no depende del convenio: con la tabla ausente la generación no tiene cómo
/// fallar, porque nada del camino la consulta. La tabla se borra de verdad en #14359 (su AC5). Los nombres van como
/// texto para que estas pruebas sigan compilando cuando #14359 retire el repositorio.
/// <para>Uso de ejemplo: la dependencia de un servicio del camino hacia <c>IMandatoFirmaPolicy</c> hace fallar el grafo.</para>
/// </summary>
public sealed partial class MandatoGeneracionSinConvenioArchitectureTests
{
    private static readonly string[] TiposRetirados =
    [
        "IMandatoFirmaPolicy", "MandatoFirmaPolicy", "NullMandatoFirmaPolicy",
        "ICompanyAgreementRepository", "CompanyAgreementRepository",
    ];

    /// <summary>Únicos archivos de producción que pueden nombrar la tabla: su mapeo y el repositorio, que retira #14359.</summary>
    private static readonly string[] ArchivosPermitidos =
    [
        "Flit.Admin.Domain/Companies/TransitOffices/ICompanyAgreementRepository.cs",
        "Flit.Infrastructure/AdminInfrastructureExtensions.cs",
        "Flit.Infrastructure/Persistence/FlitDbContext.cs",
        "Flit.Infrastructure/Persistence/Repositories/CompanyAgreementRepository.cs",
    ];

    [Fact]
    public void AC9_El_grafo_de_dependencias_de_la_generacion_no_llega_al_convenio()
    {
        IReadOnlyList<ServiceDescriptor> registrados = [];
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(services => registrados = [.. services]));
        _ = factory.Services;

        var alcanzados = Cierre(registrados, typeof(GenerarFurHandler), typeof(IMandateSimulatorService));

        alcanzados.Should().Contain(typeof(IMandateRequirementPolicy), "el recorrido del grafo tiene que ser real");
        alcanzados.Select(t => t.Name).Should().NotContain(TiposRetirados);
    }

    [Fact]
    public void AC9_Ningun_codigo_de_produccion_consulta_la_tabla_de_convenios()
    {
        var src = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "..", "src"));

        var consumidores = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(src, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal)
                && !f.Contains("/obj/", StringComparison.Ordinal)
                && !f.Contains("/Migrations/", StringComparison.Ordinal))
            .Where(f => UsoDeLaTabla().IsMatch(File.ReadAllText(Path.Combine(src, f))))
            .Except(ArchivosPermitidos)
            .ToList();

        consumidores.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Flit.Tramites.Domain.Integration.IMandatoFirmaPolicy")]
    [InlineData("Flit.Tramites.Domain.Integration.NullMandatoFirmaPolicy")]
    [InlineData("Flit.Tramites.Domain.Integration.MandatoFirmaContexto")]
    public void AC9_El_puerto_de_la_politica_de_firma_ya_no_existe(string tipo) =>
        typeof(IMandateRequirementPolicy).Assembly.GetType(tipo).Should().BeNull();

    /// <summary>Tipos alcanzables por constructor desde las raíces. Los registros por fábrica no se pueden abrir.</summary>
    private static HashSet<Type> Cierre(IReadOnlyList<ServiceDescriptor> registrados, params Type[] raices)
    {
        var implementaciones = registrados
            .Where(d => !d.IsKeyedService && d.ImplementationType is not null)
            .GroupBy(d => d.ServiceType)
            .ToDictionary(g => g.Key, g => g.Last().ImplementationType!);

        var alcanzados = new HashSet<Type>();
        var pendientes = new Queue<Type>(raices);
        while (pendientes.TryDequeue(out var tipo))
        {
            if (tipo.FullName?.StartsWith("Flit.", StringComparison.Ordinal) != true || !alcanzados.Add(tipo))
                continue;

            var concreto = implementaciones.GetValueOrDefault(tipo, tipo);
            alcanzados.Add(concreto);
            var constructor = concreto.GetConstructors().MaxBy(c => c.GetParameters().Length);
            foreach (var parametro in constructor?.GetParameters() ?? [])
            {
                pendientes.Enqueue(parametro.ParameterType);
            }
        }

        return alcanzados;
    }

    [GeneratedRegex(@"\b(ICompanyAgreementRepository|CompanyTransitOfficeAgreements)\b")]
    private static partial Regex UsoDeLaTabla();

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
