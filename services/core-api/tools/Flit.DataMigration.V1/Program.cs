using Flit.DataMigration.V1.Cli;
using Flit.DataMigration.V1.Configuration;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Orchestration;
using Flit.DataMigration.V1.Reporting;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.DataMigration.V1;

/// <summary>
/// Migrador de trámites Flit V1 → V2. Host de consola.
/// <para>
/// Este programa NO decide qué se migra: recibe una orden de trabajo (lista de ids de V1)
/// y la ejecuta tal cual, sin filtrar por estado ni por compañía. La selección es una
/// decisión de negocio que vive fuera de aquí.
/// </para>
/// <para>
/// Aquí solo quedan los argumentos, el arranque y la impresión. Todo lo que decide qué se
/// migra y cómo vive en <see cref="MigrationRunner"/>, que este host comparte con el host
/// HTTP para que el comando y el endpoint no puedan divergir.
/// </para>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        MigrationArgs parsed;
        try
        {
            parsed = MigrationArgs.Parse(args);
        }
        catch (HelpRequestedException)
        {
            Console.WriteLine(MigrationArgs.HelpText);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException)
        {
            Console.Error.WriteLine($"✗ {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(MigrationArgs.HelpText);
            return 2;
        }

        // Orden de precedencia: plantilla versionada < config local (ignorada por git) < entorno.
        // Las credenciales reales nunca viven en el archivo versionado.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables("FLITMIG_")
            .Build();

        var settings = MigrationSettings.Bind(configuration);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var token = cancellation.Token;

        // UseSnakeCaseNamingConvention es OBLIGATORIO: el modelo EF está en PascalCase y las
        // tablas de V2 en snake_case. Sin esto, EF genera SQL con "p.Id" y Postgres lo rechaza.
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql(settings.V2Connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new FlitDbContext(options);

        // Contenedor mínimo, solo para obtener IHttpClientFactory: los clientes HTTP se configuran
        // en un único sitio (MigrationHttpClients) que este host comparte con el host HTTP.
        var services = new ServiceCollection();
        services.AddMigrationHttpClients(settings);
        await using var provider = services.BuildServiceProvider();

        var runner = new MigrationRunner(db, settings, provider.GetRequiredService<IHttpClientFactory>());
        var request = new MigrationRequest(
            parsed.Kind, parsed.Instance, parsed.Ids, parsed.DryRun, parsed.Force,
            parsed.KeepIdentityImages, settings.BatchId, parsed.Tipo);

        // El encabezado va ANTES de tocar la base, como siempre: si algo falla al conectar, el
        // operador ya vio contra qué ambiente estaba apuntando.
        ConsoleReport.Header(Console.Out, runner.Describe(request));

        return parsed.Instance switch
        {
            MigrationInstance.Attachments => Finish(
                await runner.RunAttachmentsAsync(request, token), ConsoleReport.Attachments),
            MigrationInstance.Documents => Finish(
                await runner.RunDocumentsAsync(request, token), ConsoleReport.Documents),
            _ => Finish(
                await runner.RunDataAsync(request, token), ConsoleReport.Data),
        };
    }

    /// <summary>
    /// Traduce el resultado del motor a salida y código de salida.
    /// <para>
    /// Los valores de <see cref="MigrationFailureKind"/> SON los códigos históricos (2 y 3), así
    /// que aquí solo hay un cast: no existe una tabla de conversión que se pueda desalinear.
    /// </para>
    /// </summary>
    private static int Finish<TReport>(
        (TReport? Report, MigrationFailure? Failure) outcome,
        Action<TextWriter, TReport> print)
        where TReport : class
    {
        if (outcome.Failure is not null)
        {
            Console.Error.WriteLine($"✗ {outcome.Failure.Message}");
            return (int)outcome.Failure.Kind;
        }

        print(Console.Out, outcome.Report!);
        return HasProblems(outcome.Report!) ? 1 : 0;
    }

    private static bool HasProblems(object report) => report switch
    {
        DataInstanceReport data => data.HasProblems,
        AttachmentsInstanceReport attachments => attachments.HasProblems,
        DocumentsInstanceReport documents => documents.HasProblems,
        _ => false,
    };
}
