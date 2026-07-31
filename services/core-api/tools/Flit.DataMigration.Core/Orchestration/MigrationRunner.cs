using Flit.DataMigration.V1.Configuration;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.DataMigration.V1.Storage;
using Flit.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.DataMigration.V1.Orchestration;

/// <summary>
/// Ejecuta una orden de migración y DEVUELVE lo que pasó. No imprime nada: cómo se le cuenta al
/// operador lo decide el host (la consola formatea, el API serializa).
/// <para>
/// Un solo tipo con tres métodos, uno por instancia, y no una estrategia por instancia: las tres
/// reciben dependencias distintas (la 1 necesita resolver tenants; la 2, dos file-managers y un
/// modo de copia; la 3, la API de V1) y devuelven resultados sin un solo campo en común. Una
/// firma común las obligaría a compartir un objeto de configuración gordo que nadie puede leer.
/// Lo único que sí comparten —abrir la libreta, resolver el entorno destino y leer de V1— es un
/// prólogo de cinco líneas, no una jerarquía.
/// </para>
/// </summary>
public sealed class MigrationRunner(
    FlitDbContext db,
    MigrationSettings settings,
    IHttpClientFactory httpClients)
{
    /// <summary>
    /// Encabezado de la ejecución. Es PURO: no toca la base ni la red, para que el host pueda
    /// imprimirlo antes de empezar (que es lo que hace la consola desde siempre).
    /// </summary>
    public MigrationOrigin Describe(MigrationRequest request) =>
        MigrationOrigin.From(request, settings.V1Connection, settings.V2Connection);

    // ------------------------------------------------------ instancia 1: data plana

    public async Task<(DataInstanceReport? Report, MigrationFailure? Failure)> RunDataAsync(
        MigrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (context, failure) = await PrepareAsync(request, cancellationToken);
        if (failure is not null)
        {
            return (null, failure);
        }

        var tenants = await TenantResolver.LoadAsync(db, cancellationToken);
        var provisioner = new TenantProvisioner(db, settings.V1Connection);
        var loader = new ProcedureInstanceLoader(db, context!.MigrationMap, request.BatchId);
        var results = new List<LoadResult>();

        foreach (var missing in context.MissingIds)
        {
            results.Add(new LoadResult
            {
                V1Id = missing,
                Status = LoadStatus.Quarantined,
                Reason = "No existe en la copia de V1.",
            });
        }

        foreach (var record in context.Records)
        {
            results.Add(await MigrateOneAsync(
                record, tenants, provisioner, loader, context.Target, request, cancellationToken));
        }

        // Provisioned se copia AL FINAL: lo escriben dos sitios (ResolveAsync y el bucle de arriba).
        return (new DataInstanceReport(Describe(request), results, [.. context.Target.Provisioned]), null);
    }

    // -------------------------------------------------------- instancia 2: adjuntos

    public async Task<(AttachmentsInstanceReport? Report, MigrationFailure? Failure)> RunAttachmentsAsync(
        MigrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // EL ORDEN IMPORTA: el prólogo va ANTES de validar la configuración de adjuntos, igual que
        // en la consola de siempre. Un entorno con las dos cosas rotas debe seguir reportando
        // "entorno no listo" (3) y no "modo inválido" (2); invertirlo cambiaría el exit code.
        var (context, failure) = await PrepareAsync(request, cancellationToken);
        if (failure is not null)
        {
            return (null, failure);
        }

        var (mode, configFailure) = settings.ValidateForAttachments();
        if (configFailure is not null)
        {
            return (null, configFailure);
        }

        var sourceClient = new FileManagerClient(
            httpClients.CreateClient(MigrationHttpClients.SourceFileManager),
            settings.SourceFileManager.FilesPath,
            settings.SourceFileManager.AuthToken);

        FileManagerClient? targetClient = null;
        if (mode == CopyMode.Copy)
        {
            targetClient = new FileManagerClient(
                httpClients.CreateClient(MigrationHttpClients.TargetFileManager),
                settings.TargetFileManager.FilesPath,
                settings.TargetFileManager.AuthToken);
        }

        var attachmentMap = new AttachmentMapStore(db);
        await attachmentMap.EnsureCreatedAsync(cancellationToken);

        var loader = new AttachmentLoader(
            request.Kind, db, context!.MigrationMap, attachmentMap, sourceClient, targetClient, mode,
            context.Target.SystemUserId, request.BatchId, request.KeepIdentityImages);

        var results = new List<AttachmentLoadResult>();
        foreach (var missing in context.MissingIds)
        {
            results.Add(new AttachmentLoadResult
            {
                V1Id = missing,
                Status = AttachmentLoadStatus.NotMigrated,
                Reason = "No existe en la copia de V1.",
            });
        }

        foreach (var record in context.Records)
        {
            results.Add(await loader.LoadAsync(record, request.DryRun, request.Force, cancellationToken));
        }

        return (new AttachmentsInstanceReport(
            Describe(request),
            results,
            mode,
            settings.SourceFileManager,
            settings.TargetFileManager,
            request.KeepIdentityImages,
            UndeclaredAttachmentColumns.Detect(request.Kind, context.Records)), null);
    }

    // ---------------------------------------- instancia 3: documentos generados por V1

    public async Task<(DocumentsInstanceReport? Report, MigrationFailure? Failure)> RunDocumentsAsync(
        MigrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // EL ORDEN IMPORTA: ver el comentario de RunAttachmentsAsync.
        var (context, failure) = await PrepareAsync(request, cancellationToken);
        if (failure is not null)
        {
            return (null, failure);
        }

        var (snapshotEndpoint, configFailure) = settings.ValidateForDocuments(request.Kind);
        if (configFailure is not null)
        {
            return (null, configFailure);
        }

        var snapshotClient = new V1SnapshotClient(
            httpClients.CreateClient(MigrationHttpClients.V1Snapshot), snapshotEndpoint);

        var targetClient = new FileManagerClient(
            httpClients.CreateClient(MigrationHttpClients.TargetFileManager),
            settings.TargetFileManager.FilesPath,
            settings.TargetFileManager.AuthToken);

        var attachmentMap = new AttachmentMapStore(db);
        await attachmentMap.EnsureCreatedAsync(cancellationToken);

        var loader = new SnapshotLoader(
            request.Kind, db, context!.MigrationMap, attachmentMap, snapshotClient, targetClient,
            context.Target.SystemUserId, request.BatchId);

        var results = new List<SnapshotLoadResult>();
        foreach (var missing in context.MissingIds)
        {
            results.Add(new SnapshotLoadResult
            {
                V1Id = missing,
                Status = SnapshotLoadStatus.NotMigrated,
                Reason = "No existe en la copia de V1.",
            });
        }

        foreach (var record in context.Records)
        {
            results.Add(await loader.LoadAsync(record, request.DryRun, request.Force, cancellationToken));
        }

        return (new DocumentsInstanceReport(
            Describe(request), results, snapshotEndpoint, settings.TargetFileManager), null);
    }

    // ------------------------------------------------------------------- compartido

    /// <summary>Lo que las tres instancias necesitan antes de empezar.</summary>
    private sealed record RunContext(
        MigrationMapStore MigrationMap,
        TargetEnvironment Target,
        IReadOnlyList<V1SourceRecord> Records,
        IReadOnlyList<long> MissingIds);

    private async Task<(RunContext? Context, MigrationFailure? Failure)> PrepareAsync(
        MigrationRequest request, CancellationToken cancellationToken)
    {
        var migrationMap = new MigrationMapStore(db);
        await migrationMap.EnsureCreatedAsync(cancellationToken);

        TargetEnvironment target;
        try
        {
            // El tipo de trámite de V2 depende de lo que se esté migrando: matrícula y traspaso
            // no comparten ni tipo, ni tipología, ni pasos del wizard.
            target = await TargetEnvironment.ResolveAsync(
                db, request.Kind.ProcedureTypeCode, settings.SystemUserEmail, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // El prefijo forma parte del mensaje que el operador lleva años leyendo.
            return (null, new MigrationFailure(
                MigrationFailureKind.EnvironmentNotReady,
                "migracion.entorno.no_listo",
                $"Entorno destino no listo: {ex.Message}"));
        }

        var reader = new PostgresV1SourceReader(settings.V1Connection, request.Kind.Tables);
        var records = await reader.ReadAsync(request.Ids, cancellationToken);
        var found = records.Select(r => r.Id).ToHashSet();
        var missingIds = request.Ids.Where(id => !found.Contains(id)).ToList();

        return (new RunContext(migrationMap, target, records, missingIds), null);
    }

    private async Task<LoadResult> MigrateOneAsync(
        V1SourceRecord record,
        TenantResolver tenants,
        TenantProvisioner provisioner,
        ProcedureInstanceLoader loader,
        TargetEnvironment target,
        MigrationRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = tenants.Resolve(record.CompanyNit);
        Guid tenantId;

        if (resolution.Resolved)
        {
            tenantId = resolution.TenantId!.Value;
        }
        else if (settings.CreateTenantIfMissing
                 && record.CompanyNit is not null
                 && !resolution.Problem!.Contains("Ambigüedad", StringComparison.Ordinal))
        {
            // Ambigüedad NUNCA se auto-resuelve: crear un tenant más empeoraría el problema.
            var created = await provisioner.CreateFromV1Async(record.CompanyNit, cancellationToken);
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

        var mapped = request.Kind.Map(record, context);
        return await loader.LoadAsync(mapped, request.DryRun, request.Force, cancellationToken);
    }
}
