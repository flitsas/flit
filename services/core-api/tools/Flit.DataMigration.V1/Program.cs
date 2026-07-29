using System.Globalization;
using System.Net.Http;
using Flit.DataMigration.V1.Cli;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.DataMigration.V1.Storage;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Flit.DataMigration.V1;

/// <summary>
/// Migrador de trámites Flit V1 → V2, instancia 1 (data plana).
/// <para>
/// Este programa NO decide qué se migra: recibe una orden de trabajo (lista de ids de V1)
/// y la ejecuta tal cual, sin filtrar por estado ni por compañía. La selección es una
/// decisión de negocio que vive fuera de aquí.
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

        // Qué trámite y qué instancia: lo resolvió MigrationArgs y viaja junto para que la tabla
        // de origen, el catálogo de estados y el mapa de adjuntos no se puedan desemparejar.
        var kind = parsed.Kind;

        // Orden de precedencia: plantilla versionada < config local (ignorada por git) < entorno.
        // Las credenciales reales nunca viven en el archivo versionado.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables("FLITMIG_")
            .Build();

        var v1Connection = Require(configuration, "ConnectionStrings:V1Source");
        var v2Connection = Require(configuration, "ConnectionStrings:V2Target");
        var batchId = configuration["Migration:BatchId"] ?? "sin-lote";
        var systemUserEmail = configuration["Migration:SystemUserEmail"] ?? "migracion.v1@flitsas.io";
        var createTenant = configuration.GetValue("Migration:CreateTenantIfMissing", false);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var token = cancellation.Token;

        // UseSnakeCaseNamingConvention es OBLIGATORIO: el modelo EF está en PascalCase y las
        // tablas de V2 en snake_case. Sin esto, EF genera SQL con "p.Id" y Postgres lo rechaza.
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql(v2Connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new FlitDbContext(options);

        Header(parsed, batchId, v1Connection, v2Connection);

        var migrationMap = new MigrationMapStore(db);
        await migrationMap.EnsureCreatedAsync(token);

        TargetEnvironment target;
        try
        {
            // El tipo de trámite de V2 depende de lo que se esté migrando: matrícula y traspaso
            // no comparten ni tipo, ni tipología, ni pasos del wizard.
            target = await TargetEnvironment.ResolveAsync(db, kind.ProcedureTypeCode, systemUserEmail, token);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"✗ Entorno destino no listo: {ex.Message}");
            return 3;
        }

        var reader = new PostgresV1SourceReader(v1Connection, kind.Tables);
        var records = await reader.ReadAsync(parsed.Ids, token);
        var found = records.Select(r => r.Id).ToHashSet();
        var missingIds = parsed.Ids.Where(id => !found.Contains(id)).ToList();

        // Instancia 2 (adjuntos) es un camino aparte: no toca tenants ni carga trámites, solo
        // enlaza binarios a trámites que YA existen en migration_map.
        if (parsed.Instance == MigrationInstance.Attachments)
        {
            return await RunAttachmentsAsync(
                db, configuration, migrationMap, target, records, missingIds, parsed, batchId, token);
        }

        // Instancia 3 (documentos generados): tampoco toca tenants. Le pide a V1 que materialice los
        // documentos que arma en caliente y nunca guarda, y los persiste en V2.
        if (parsed.Instance == MigrationInstance.Documents)
        {
            return await RunDocumentsAsync(
                db, configuration, migrationMap, target, records, missingIds, parsed, batchId, token);
        }

        var tenants = await TenantResolver.LoadAsync(db, token);
        var provisioner = new TenantProvisioner(db, v1Connection);
        var loader = new ProcedureInstanceLoader(db, migrationMap, batchId);
        var results = new List<LoadResult>();

        foreach (var missing in missingIds)
        {
            results.Add(new LoadResult
            {
                V1Id = missing,
                Status = LoadStatus.Quarantined,
                Reason = "No existe en la copia de V1.",
            });
        }

        foreach (var record in records)
        {
            results.Add(await MigrateOneAsync(
                record, tenants, provisioner, loader, target, parsed, createTenant, token));
        }

        Report(results, target.Provisioned, parsed.DryRun);
        return results.Any(r => r.Status == LoadStatus.Quarantined) ? 1 : 0;
    }

    // ------------------------------------------------------- instancia 2: adjuntos

    private static async Task<int> RunAttachmentsAsync(
        FlitDbContext db,
        IConfiguration configuration,
        MigrationMapStore migrationMap,
        TargetEnvironment target,
        IReadOnlyList<V1SourceRecord> records,
        IReadOnlyList<long> missingIds,
        MigrationArgs parsed,
        string batchId,
        CancellationToken token)
    {
        var modeText = configuration["Attachments:Mode"] ?? "Copy";
        if (!Enum.TryParse<CopyMode>(modeText, ignoreCase: true, out var mode))
        {
            Console.Error.WriteLine($"✗ Attachments:Mode '{modeText}' inválido. Use 'Copy' o 'Reference'.");
            return 2;
        }

        var sourceEndpoint = ReadEndpoint(configuration, "SourceFileManager");
        if (!sourceEndpoint.IsConfigured)
        {
            Console.Error.WriteLine("✗ Falta SourceFileManager:BaseUrl (file-manager ORIGEN, donde viven los binarios de V1).");
            return 3;
        }

        var targetEndpoint = ReadEndpoint(configuration, "TargetFileManager");
        if (mode == CopyMode.Copy && !targetEndpoint.IsConfigured)
        {
            Console.Error.WriteLine("✗ El modo Copy exige TargetFileManager:BaseUrl (file-manager DESTINO de V2).");
            return 3;
        }

        using var sourceHttp = BuildHttp(sourceEndpoint);
        var sourceClient = new FileManagerClient(sourceHttp, sourceEndpoint.FilesPath, sourceEndpoint.AuthToken);

        HttpClient? targetHttp = null;
        FileManagerClient? targetClient = null;
        try
        {
            if (mode == CopyMode.Copy)
            {
                targetHttp = BuildHttp(targetEndpoint);
                targetClient = new FileManagerClient(targetHttp, targetEndpoint.FilesPath, targetEndpoint.AuthToken);
            }

            var attachmentMap = new AttachmentMapStore(db);
            await attachmentMap.EnsureCreatedAsync(token);

            var loader = new AttachmentLoader(
                parsed.Kind, db, migrationMap, attachmentMap, sourceClient, targetClient, mode,
                target.SystemUserId, batchId, parsed.KeepIdentityImages);

            var results = new List<AttachmentLoadResult>();
            foreach (var missing in missingIds)
            {
                results.Add(new AttachmentLoadResult
                {
                    V1Id = missing,
                    Status = AttachmentLoadStatus.NotMigrated,
                    Reason = "No existe en la copia de V1.",
                });
            }

            foreach (var record in records)
            {
                results.Add(await loader.LoadAsync(record, parsed.DryRun, parsed.Force, token));
            }

            ReportAttachments(
                results, mode, sourceEndpoint, targetEndpoint, parsed.DryRun, parsed.KeepIdentityImages,
                ColumnasNoDeclaradas(parsed.Kind, records));
            return results.Any(r => r.Status == AttachmentLoadStatus.NotMigrated || r.Failed > 0) ? 1 : 0;
        }
        finally
        {
            targetHttp?.Dispose();
        }
    }

    /// <summary>
    /// Columnas <c>id_attach*</c> que la tabla de V1 tiene y el mapa de adjuntos no declara — ni
    /// mapeadas ni excluidas.
    /// <para>
    /// El aviso por trámite solo salta cuando la columna trae valor, así que una columna nueva de V1
    /// puede pasar meses invisible y aparecer el día de la migración real con datos. Esto la detecta
    /// mirando el ESQUEMA, no los datos: basta con leer un trámite para verla.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> ColumnasNoDeclaradas(
        V1ProcedureKind kind, IReadOnlyList<V1SourceRecord> records)
    {
        if (records.Count == 0)
        {
            return [];
        }

        var declaradas = kind.AttachmentMap.DeclaredColumns().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. records[0].Columns.Keys
            .Where(V1ProcedureKind.IsAttachmentColumn)
            .Where(c => !declaradas.Contains(c))
            .OrderBy(c => c, StringComparer.Ordinal)];
    }

    private static FileManagerEndpoint ReadEndpoint(IConfiguration configuration, string section) =>
        new()
        {
            BaseUrl = configuration[$"{section}:BaseUrl"] ?? string.Empty,
            FilesPath = configuration[$"{section}:FilesPath"] ?? "api/v1/files",
            AuthToken = configuration[$"{section}:AuthToken"] ?? string.Empty,
        };

    /// <summary>
    /// Cliente contra un file-manager. 5 minutos, no 60 segundos: se mueven PDF de varios MB entre
    /// dos servicios remotos, y con 60 s los expedientes grandes (el consolidado ronda los 9-12 MB)
    /// se caían por tiempo de forma intermitente. No era pérdida de datos —el reintento los
    /// recupera— pero en un lote de cientos de trámites convierte el reporte en ruido y obliga a
    /// pasadas extra.
    /// </summary>
    private static HttpClient BuildHttp(FileManagerEndpoint endpoint)
    {
        var baseUrl = endpoint.BaseUrl.EndsWith('/') ? endpoint.BaseUrl : endpoint.BaseUrl + "/";
        return new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
    }

    private static async Task<LoadResult> MigrateOneAsync(
        V1SourceRecord record,
        TenantResolver tenants,
        TenantProvisioner provisioner,
        ProcedureInstanceLoader loader,
        TargetEnvironment target,
        MigrationArgs parsed,
        bool createTenant,
        CancellationToken token)
    {
        var resolution = tenants.Resolve(record.CompanyNit);
        Guid tenantId;

        if (resolution.Resolved)
        {
            tenantId = resolution.TenantId!.Value;
        }
        else if (createTenant && record.CompanyNit is not null && !resolution.Problem!.Contains("Ambigüedad", StringComparison.Ordinal))
        {
            // Ambigüedad NUNCA se auto-resuelve: crear un tenant más empeoraría el problema.
            var created = await provisioner.CreateFromV1Async(record.CompanyNit, token);
            tenants.Register(created);
            tenantId = created.Id;
            target.Provisioned.Add($"tenant para el NIT {record.CompanyNit} (modo laboratorio)");
        }
        else
        {
            return new LoadResult
            {
                V1Id = record.Id,
                Status = LoadStatus.Quarantined,
                Reason = resolution.Problem,
            };
        }

        var context = new MappingContext
        {
            TenantId = tenantId,
            ProcedureTypeId = target.ProcedureTypeId,
            SystemUserId = target.SystemUserId,
            OwnerEntityId = target.OwnerEntityId,
            BuyerEntityId = target.BuyerEntityId,
        };

        var mapped = parsed.Kind.Map(record, context);
        return await loader.LoadAsync(mapped, parsed.DryRun, parsed.Force, token);
    }

    // ------------------------------------------------------------------ salida

    private static void Header(MigrationArgs parsed, string batchId, string v1, string v2)
    {
        var instancia = parsed.Instance switch
        {
            MigrationInstance.Attachments => "instancia 2 (adjuntos cargados)",
            MigrationInstance.Documents => "instancia 3 (documentos generados)",
            _ => "instancia 1 (data plana)",
        };
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Migración Flit V1 → V2 · {parsed.Kind.Nombre.ToUpperInvariant()} · {instancia}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Modo      : {(parsed.DryRun ? "DRY-RUN (no escribe, hace rollback)" : "REAL (escribe en V2)")}");
        Console.WriteLine($"  Tipo      : {parsed.Tipo}");
        // La tabla se imprime SIEMPRE: 12.807 ids existen en las dos tablas de V1, así que el id por
        // sí solo no identifica un trámite. Esta línea es la que deja ver un --tipo equivocado.
        Console.WriteLine($"  Tabla V1  : {parsed.Kind.Tables.Master}");
        Console.WriteLine($"  Tipo V2   : {parsed.Kind.ProcedureTypeCode}");
        Console.WriteLine($"  Lote      : {batchId}");
        Console.WriteLine($"  Trámites  : {parsed.Ids.Count} → {string.Join(", ", parsed.Ids.Take(10))}{(parsed.Ids.Count > 10 ? ", …" : "")}");
        Console.WriteLine($"  Origen V1 : {Describe(v1)}");
        Console.WriteLine($"  Destino V2: {Describe(v2)}");
        Console.WriteLine();
    }

    /// <summary>Muestra a qué base apunta, sin filtrar credenciales al log.</summary>
    private static string Describe(string connectionString)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Database} @ {builder.Host}:{builder.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static void Report(List<LoadResult> results, List<string> provisioned, bool dryRun)
    {
        if (provisioned.Count > 0)
        {
            Console.WriteLine("── Prerrequisitos creados en V2 ───────────────────────────────");
            foreach (var item in provisioned)
            {
                Console.WriteLine($"  + {item}");
            }

            Console.WriteLine("  (se crean fuera de la transacción: PERSISTEN aunque sea dry-run)");
            Console.WriteLine();
        }

        Console.WriteLine("── Resultado por trámite ──────────────────────────────────────");
        foreach (var result in results.OrderBy(r => r.V1Id))
        {
            var icon = result.Status switch
            {
                LoadStatus.Migrated => "✓",
                LoadStatus.Simulated => "◇",
                LoadStatus.Skipped => "=",
                _ => "✗",
            };

            Console.WriteLine($"  {icon} V1 #{result.V1Id}  {result.Status}");
            if (result.V2Id is not null)
            {
                var estado = result.FinalStatus is null ? "" : $"  estado '{result.FinalStatus}'";
                Console.WriteLine($"      → V2 {result.V2Id}{estado}");
            }

            // Los conteos solo tienen sentido cuando de verdad se escribió (o se simuló) algo.
            if (result.Status is LoadStatus.Migrated or LoadStatus.Simulated)
            {
                Console.WriteLine($"      → {result.FieldCount} campos · {result.ActorCount} actores · {result.HistoryCount} eventos de historial");
            }

            if (result.Reason is not null)
            {
                Console.WriteLine($"      motivo: {result.Reason}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"      ⚠ {warning}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("── Reconciliación ─────────────────────────────────────────────");
        foreach (var group in results.GroupBy(r => r.Status).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  {group.Key,-12} {group.Count()}");
        }

        var warnings = results.Sum(r => r.Warnings.Count);
        if (warnings > 0)
        {
            Console.WriteLine($"  {"con avisos",-12} {results.Count(r => r.Warnings.Count > 0)} trámites ({warnings} avisos)");
        }

        Console.WriteLine();
        Console.WriteLine(dryRun
            ? "◇ DRY-RUN: ningún trámite se escribió en V2. Todo se revirtió."
            : "✓ Ejecución real terminada.");
    }

    // ------------------------------------------- instancia 3: documentos generados por V1

    private static async Task<int> RunDocumentsAsync(
        FlitDbContext db,
        IConfiguration configuration,
        MigrationMapStore migrationMap,
        TargetEnvironment target,
        IReadOnlyList<V1SourceRecord> records,
        IReadOnlyList<long> missingIds,
        MigrationArgs parsed,
        string batchId,
        CancellationToken token)
    {
        var snapshotEndpoint = new V1SnapshotEndpoint
        {
            BaseUrl = configuration["V1Snapshot:BaseUrl"] ?? string.Empty,
            // La ruta por defecto la fija el tipo de trámite: traspaso y matrícula tienen endpoints
            // distintos en V1. Solo se sobrescribe si la config trae un valor real.
            Path = Blank(configuration["V1Snapshot:Path"]) ?? parsed.Kind.SnapshotPath,
            AuthToken = configuration["V1Snapshot:AuthToken"] ?? string.Empty,
            Include = configuration["V1Snapshot:Include"] ?? "generated",
            Consolidated = configuration["V1Snapshot:Consolidated"] ?? "auto",
        };

        if (!snapshotEndpoint.IsConfigured)
        {
            Console.Error.WriteLine(
                $"✗ Falta V1Snapshot:BaseUrl (API de V1 que materializa los documentos generados de {parsed.Kind.Nombre}).");
            return 3;
        }

        var targetEndpoint = ReadEndpoint(configuration, "TargetFileManager");
        if (!targetEndpoint.IsConfigured)
        {
            Console.Error.WriteLine("✗ Falta TargetFileManager:BaseUrl (file-manager DESTINO de V2).");
            return 3;
        }

        // Los expedientes completos rondan los 9-12 MB y V1 los arma al vuelo: el timeout por
        // defecto de 100 s se queda corto.
        using var snapshotHttp = new HttpClient
        {
            BaseAddress = new Uri(snapshotEndpoint.BaseUrl.EndsWith('/') ? snapshotEndpoint.BaseUrl : snapshotEndpoint.BaseUrl + "/"),
            Timeout = TimeSpan.FromMinutes(5),
            MaxResponseContentBufferSize = 256L * 1024 * 1024,
        };
        var snapshotClient = new V1SnapshotClient(snapshotHttp, snapshotEndpoint);

        using var targetHttp = BuildHttp(targetEndpoint);
        var targetClient = new FileManagerClient(targetHttp, targetEndpoint.FilesPath, targetEndpoint.AuthToken);

        var attachmentMap = new AttachmentMapStore(db);
        await attachmentMap.EnsureCreatedAsync(token);

        var loader = new SnapshotLoader(
            parsed.Kind, db, migrationMap, attachmentMap, snapshotClient, targetClient,
            target.SystemUserId, batchId);

        var results = new List<SnapshotLoadResult>();
        foreach (var missing in missingIds)
        {
            results.Add(new SnapshotLoadResult
            {
                V1Id = missing,
                Status = SnapshotLoadStatus.NotMigrated,
                Reason = "No existe en la copia de V1.",
            });
        }

        foreach (var record in records)
        {
            results.Add(await loader.LoadAsync(record, parsed.DryRun, parsed.Force, token));
        }

        ReportDocuments(results, snapshotEndpoint, targetEndpoint, parsed.DryRun);

        var conProblemas = results.Any(r =>
            r.Status is SnapshotLoadStatus.NotMigrated or SnapshotLoadStatus.NotFoundInV1 or SnapshotLoadStatus.Failed
            || r.Failed > 0);
        return conProblemas ? 1 : 0;
    }

    private static void ReportDocuments(
        List<SnapshotLoadResult> results,
        V1SnapshotEndpoint snapshotEndpoint,
        FileManagerEndpoint targetEndpoint,
        bool dryRun)
    {
        Console.WriteLine("── Materialización de documentos generados por V1 ─────────────");
        Console.WriteLine($"  Origen    : {snapshotEndpoint.BaseUrl}{snapshotEndpoint.Path}");
        Console.WriteLine($"  Destino   : {targetEndpoint.BaseUrl}{targetEndpoint.FilesPath}");
        Console.WriteLine($"  Alcance   : include={snapshotEndpoint.Include} · consolidado={snapshotEndpoint.Consolidated}");

        Console.WriteLine();
        Console.WriteLine("── Resultado por trámite ──────────────────────────────────────");
        foreach (var result in results.OrderBy(r => r.V1Id))
        {
            var icon = result.Status switch
            {
                SnapshotLoadStatus.Materialized => "✓",
                SnapshotLoadStatus.Simulated => "◇",
                _ => "✗",
            };

            Console.WriteLine($"  {icon} V1 #{result.V1Id}  {result.Status}");
            if (result.V2Id is not null)
            {
                Console.WriteLine($"      → V2 {result.V2Id}");
            }

            if (result.Status is SnapshotLoadStatus.Materialized or SnapshotLoadStatus.Simulated)
            {
                Console.WriteLine($"      → {result.Materialized} documento(s) · {result.Skipped} ya materializados · {result.Failed} fallidos · {result.Duplicated} ya venían como adjunto");
            }

            if (result.Reason is not null)
            {
                Console.WriteLine($"      motivo: {result.Reason}");
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"      ⚠ V1 no entregó: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"      ⚠ {warning}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("── Reconciliación ─────────────────────────────────────────────");
        Console.WriteLine($"  {"documentos materializados",-26} {results.Sum(r => r.Materialized)}");
        Console.WriteLine($"  {"ya materializados (saltados)",-26} {results.Sum(r => r.Skipped)}");
        Console.WriteLine($"  {"fallidos",-26} {results.Sum(r => r.Failed)}");
        Console.WriteLine($"  {"ya venían como adjunto",-26} {results.Sum(r => r.Duplicated)}");
        Console.WriteLine($"  {"piezas no entregadas por V1",-26} {results.Sum(r => r.Issues.Count)}");

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("  ◇ DRY-RUN: no se subió ningún binario ni se escribió en V2.");
        }

        // Un trámite sin piezas materializadas y sin issues es sospechoso: o ya estaba todo hecho,
        // o V1 no tenía nada que entregar. Se dice explícitamente para que nadie lo lea como éxito.
        var vacios = results.Count(r =>
            r.Status is SnapshotLoadStatus.Materialized or SnapshotLoadStatus.Simulated
            && r.Materialized == 0 && r.Skipped == 0 && r.Duplicated == 0);
        if (vacios > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠ {vacios} trámite(s) no materializaron ninguna pieza: revisa los motivos arriba.");
        }
    }

    private static void ReportAttachments(
        List<AttachmentLoadResult> results,
        CopyMode mode,
        FileManagerEndpoint source,
        FileManagerEndpoint targetEndpoint,
        bool dryRun,
        bool keepIdentityImages,
        IReadOnlyList<string> columnasNoDeclaradas)
    {
        Console.WriteLine("── Configuración de adjuntos ──────────────────────────────────");
        Console.WriteLine($"  Modo      : {mode} {(mode == CopyMode.Copy ? "(descarga del origen y sube al destino)" : "(mismo store: referencia el id de V1, cero copia)")}");
        Console.WriteLine($"  Origen    : {source.BaseUrl}{source.FilesPath}");
        if (mode == CopyMode.Copy)
        {
            Console.WriteLine($"  Destino   : {targetEndpoint.BaseUrl}{targetEndpoint.FilesPath}");
        }

        Console.WriteLine(keepIdentityImages
            ? "  Identidad : se migran las imágenes sueltas (--conservar-jpg-identidad)."
            : "  Identidad : las imágenes sueltas se omiten donde hay carta selfie que las contiene.");

        Console.WriteLine();
        Console.WriteLine("── Resultado por trámite ──────────────────────────────────────");
        foreach (var result in results.OrderBy(r => r.V1Id))
        {
            var icon = result.Status switch
            {
                AttachmentLoadStatus.Migrated => "✓",
                AttachmentLoadStatus.Simulated => "◇",
                AttachmentLoadStatus.NoAttachments => "·",
                _ => "✗",
            };

            Console.WriteLine($"  {icon} V1 #{result.V1Id}  {result.Status}");
            if (result.V2Id is not null)
            {
                Console.WriteLine($"      → V2 {result.V2Id}");
            }

            if (result.Status is AttachmentLoadStatus.Migrated or AttachmentLoadStatus.Simulated)
            {
                Console.WriteLine($"      → {result.Copied} adjunto(s) · {result.Skipped} ya migrados · {result.Failed} fallidos · {result.Excluded} excluidos (PDF generados) · {result.Redundant} imágenes en la carta selfie");
            }

            if (result.Reason is not null)
            {
                Console.WriteLine($"      motivo: {result.Reason}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"      ⚠ {warning}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("── Reconciliación ─────────────────────────────────────────────");
        Console.WriteLine($"  {"adjuntos copiados",-22} {results.Sum(r => r.Copied)}");
        Console.WriteLine($"  {"ya migrados (saltados)",-22} {results.Sum(r => r.Skipped)}");
        Console.WriteLine($"  {"fallidos",-22} {results.Sum(r => r.Failed)}");
        Console.WriteLine($"  {"excluidos (PDF gen.)",-22} {results.Sum(r => r.Excluded)}");

        var redundantes = results.Sum(r => r.Redundant);
        Console.WriteLine($"  {"imágenes en la carta",-22} {redundantes}");
        if (redundantes > 0)
        {
            Console.WriteLine("    ↳ estas imágenes NO quedan en V2 por esta vía: dependen de que se corra");
            Console.WriteLine("      la instancia -documents, que es quien trae la carta selfie.");
        }

        if (columnasNoDeclaradas.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠ {columnasNoDeclaradas.Count} columna(s) de adjunto de V1 sin declarar en el mapa:");
            foreach (var columna in columnasNoDeclaradas)
            {
                Console.WriteLine($"      · {columna}");
            }

            Console.WriteLine("    Vienen vacías en este lote, pero si traen dato NO se migrarán.");
            Console.WriteLine("    Decídelas en V1AttachmentMap: mapeadas a un 'tipo' o excluidas a propósito.");
        }

        Console.WriteLine();
        Console.WriteLine(dryRun
            ? "◇ DRY-RUN: nada se escribió en V2 ni se subió al destino. Todo se revirtió."
            : "✓ Ejecución real terminada.");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Require(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException(
            $"Falta la configuración '{key}' (appsettings.json o variable FLITMIG_{key.Replace(':', '_')}).");
}
