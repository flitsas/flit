using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Orchestration;

namespace Flit.DataMigration.V1.Reporting;

/// <summary>
/// Imprime el reporte del migrador. Es la ÚNICA parte del programa que sabe de consola.
/// <para>
/// El reporte es el producto de esta herramienta, no un adorno: los avisos, la cuarentena y la
/// reconciliación son lo que un humano lee para decidir qué hacer con los trámites que no
/// entraron. Está congelado carácter por carácter con golden files
/// (<c>tests/Flit.DataMigration.Tests</c>) porque los runbooks de operación citan estas líneas.
/// </para>
/// </summary>
internal static class ConsoleReport
{
    internal static void Header(TextWriter output, MigrationOrigin origin)
    {
        var instancia = origin.Instance switch
        {
            MigrationInstance.Attachments => "instancia 2 (adjuntos cargados)",
            MigrationInstance.Documents => "instancia 3 (documentos generados)",
            _ => "instancia 1 (data plana)",
        };
        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine($"  Migración Flit V1 → V2 · {origin.KindNombre.ToUpperInvariant()} · {instancia}");
        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine($"  Modo      : {(origin.DryRun ? "DRY-RUN (no escribe, hace rollback)" : "REAL (escribe en V2)")}");
        output.WriteLine($"  Tipo      : {origin.Tipo}");
        // La tabla se imprime SIEMPRE: 12.807 ids existen en las dos tablas de V1, así que el id por
        // sí solo no identifica un trámite. Esta línea es la que deja ver un --tipo equivocado.
        output.WriteLine($"  Tabla V1  : {origin.V1Table}");
        output.WriteLine($"  Tipo V2   : {origin.ProcedureTypeCode}");
        output.WriteLine($"  Lote      : {origin.BatchId}");
        output.WriteLine($"  Trámites  : {origin.Ids.Count} → {string.Join(", ", origin.Ids.Take(10))}{(origin.Ids.Count > 10 ? ", …" : "")}");
        output.WriteLine($"  Origen V1 : {origin.V1Database}");
        output.WriteLine($"  Destino V2: {origin.V2Database}");
        output.WriteLine();
    }

    internal static void Data(TextWriter output, DataInstanceReport report)
    {
        var results = report.Results;
        var provisioned = report.Provisioned;
        var dryRun = report.Origin.DryRun;

        if (provisioned.Count > 0)
        {
            output.WriteLine("── Prerrequisitos creados en V2 ───────────────────────────────");
            foreach (var item in provisioned)
            {
                output.WriteLine($"  + {item}");
            }

            output.WriteLine("  (se crean fuera de la transacción: PERSISTEN aunque sea dry-run)");
            output.WriteLine();
        }

        output.WriteLine("── Resultado por trámite ──────────────────────────────────────");
        foreach (var result in results.OrderBy(r => r.V1Id))
        {
            var icon = result.Status switch
            {
                LoadStatus.Migrated => "✓",
                LoadStatus.Simulated => "◇",
                LoadStatus.Skipped => "=",
                _ => "✗",
            };

            output.WriteLine($"  {icon} V1 #{result.V1Id}  {result.Status}");
            if (result.V2Id is not null)
            {
                var estado = result.FinalStatus is null ? "" : $"  estado '{result.FinalStatus}'";
                output.WriteLine($"      → V2 {result.V2Id}{estado}");
            }

            // Los conteos solo tienen sentido cuando de verdad se escribió (o se simuló) algo.
            if (result.Status is LoadStatus.Migrated or LoadStatus.Simulated)
            {
                output.WriteLine($"      → {result.FieldCount} campos · {result.ActorCount} actores · {result.HistoryCount} eventos de historial");
            }

            if (result.Reason is not null)
            {
                output.WriteLine($"      motivo: {result.Reason}");
            }

            foreach (var warning in result.Warnings)
            {
                output.WriteLine($"      ⚠ {warning}");
            }
        }

        output.WriteLine();
        output.WriteLine("── Reconciliación ─────────────────────────────────────────────");
        foreach (var group in results.GroupBy(r => r.Status).OrderBy(g => g.Key))
        {
            output.WriteLine($"  {group.Key,-12} {group.Count()}");
        }

        var warnings = results.Sum(r => r.Warnings.Count);
        if (warnings > 0)
        {
            output.WriteLine($"  {"con avisos",-12} {results.Count(r => r.Warnings.Count > 0)} trámites ({warnings} avisos)");
        }

        output.WriteLine();
        output.WriteLine(dryRun
            ? "◇ DRY-RUN: ningún trámite se escribió en V2. Todo se revirtió."
            : "✓ Ejecución real terminada.");
    }

    internal static void Attachments(TextWriter output, AttachmentsInstanceReport report)
    {
        var results = report.Results;
        var mode = report.Mode;
        var source = report.Source;
        var targetEndpoint = report.Target;
        var dryRun = report.Origin.DryRun;
        var keepIdentityImages = report.KeepIdentityImages;
        var columnasNoDeclaradas = report.UndeclaredColumns;

        output.WriteLine("── Configuración de adjuntos ──────────────────────────────────");
        output.WriteLine($"  Modo      : {mode} {(mode == CopyMode.Copy ? "(descarga del origen y sube al destino)" : "(mismo store: referencia el id de V1, cero copia)")}");
        output.WriteLine($"  Origen    : {source.BaseUrl}{source.FilesPath}");
        if (mode == CopyMode.Copy)
        {
            output.WriteLine($"  Destino   : {targetEndpoint.BaseUrl}{targetEndpoint.FilesPath}");
        }

        output.WriteLine(keepIdentityImages
            ? "  Identidad : se migran las imágenes sueltas (--conservar-jpg-identidad)."
            : "  Identidad : las imágenes sueltas se omiten donde hay carta selfie que las contiene.");

        output.WriteLine();
        output.WriteLine("── Resultado por trámite ──────────────────────────────────────");
        foreach (var result in results.OrderBy(r => r.V1Id))
        {
            var icon = result.Status switch
            {
                AttachmentLoadStatus.Migrated => "✓",
                AttachmentLoadStatus.Simulated => "◇",
                AttachmentLoadStatus.NoAttachments => "·",
                _ => "✗",
            };

            output.WriteLine($"  {icon} V1 #{result.V1Id}  {result.Status}");
            if (result.V2Id is not null)
            {
                output.WriteLine($"      → V2 {result.V2Id}");
            }

            if (result.Status is AttachmentLoadStatus.Migrated or AttachmentLoadStatus.Simulated)
            {
                output.WriteLine($"      → {result.Copied} adjunto(s) · {result.Skipped} ya migrados · {result.Failed} fallidos · {result.Excluded} excluidos (PDF generados) · {result.Redundant} imágenes en la carta selfie");
            }

            if (result.Reason is not null)
            {
                output.WriteLine($"      motivo: {result.Reason}");
            }

            foreach (var warning in result.Warnings)
            {
                output.WriteLine($"      ⚠ {warning}");
            }
        }

        output.WriteLine();
        output.WriteLine("── Reconciliación ─────────────────────────────────────────────");
        output.WriteLine($"  {"adjuntos copiados",-22} {results.Sum(r => r.Copied)}");
        output.WriteLine($"  {"ya migrados (saltados)",-22} {results.Sum(r => r.Skipped)}");
        output.WriteLine($"  {"fallidos",-22} {results.Sum(r => r.Failed)}");
        output.WriteLine($"  {"excluidos (PDF gen.)",-22} {results.Sum(r => r.Excluded)}");

        var redundantes = results.Sum(r => r.Redundant);
        output.WriteLine($"  {"imágenes en la carta",-22} {redundantes}");
        if (redundantes > 0)
        {
            output.WriteLine("    ↳ estas imágenes NO quedan en V2 por esta vía: dependen de que se corra");
            output.WriteLine("      la instancia -documents, que es quien trae la carta selfie.");
        }

        if (columnasNoDeclaradas.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"  ⚠ {columnasNoDeclaradas.Count} columna(s) de adjunto de V1 sin declarar en el mapa:");
            foreach (var columna in columnasNoDeclaradas)
            {
                output.WriteLine($"      · {columna}");
            }

            output.WriteLine("    Vienen vacías en este lote, pero si traen dato NO se migrarán.");
            output.WriteLine("    Decídelas en V1AttachmentMap: mapeadas a un 'tipo' o excluidas a propósito.");
        }

        output.WriteLine();
        output.WriteLine(dryRun
            ? "◇ DRY-RUN: nada se escribió en V2 ni se subió al destino. Todo se revirtió."
            : "✓ Ejecución real terminada.");
    }

    internal static void Documents(TextWriter output, DocumentsInstanceReport report)
    {
        var results = report.Results;
        var snapshotEndpoint = report.Snapshot;
        var targetEndpoint = report.Target;
        var dryRun = report.Origin.DryRun;

        output.WriteLine("── Materialización de documentos generados por V1 ─────────────");
        output.WriteLine($"  Origen    : {snapshotEndpoint.BaseUrl}{snapshotEndpoint.Path}");
        output.WriteLine($"  Destino   : {targetEndpoint.BaseUrl}{targetEndpoint.FilesPath}");
        output.WriteLine($"  Alcance   : include={snapshotEndpoint.Include} · consolidado={snapshotEndpoint.Consolidated}");

        output.WriteLine();
        output.WriteLine("── Resultado por trámite ──────────────────────────────────────");
        foreach (var result in results.OrderBy(r => r.V1Id))
        {
            var icon = result.Status switch
            {
                SnapshotLoadStatus.Materialized => "✓",
                SnapshotLoadStatus.Simulated => "◇",
                _ => "✗",
            };

            output.WriteLine($"  {icon} V1 #{result.V1Id}  {result.Status}");
            if (result.V2Id is not null)
            {
                output.WriteLine($"      → V2 {result.V2Id}");
            }

            if (result.Status is SnapshotLoadStatus.Materialized or SnapshotLoadStatus.Simulated)
            {
                output.WriteLine($"      → {result.Materialized} documento(s) · {result.Skipped} ya materializados · {result.Failed} fallidos · {result.Duplicated} ya venían como adjunto");
            }

            if (result.Reason is not null)
            {
                output.WriteLine($"      motivo: {result.Reason}");
            }

            foreach (var issue in result.Issues)
            {
                output.WriteLine($"      ⚠ V1 no entregó: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                output.WriteLine($"      ⚠ {warning}");
            }
        }

        output.WriteLine();
        output.WriteLine("── Reconciliación ─────────────────────────────────────────────");
        output.WriteLine($"  {"documentos materializados",-26} {results.Sum(r => r.Materialized)}");
        output.WriteLine($"  {"ya materializados (saltados)",-26} {results.Sum(r => r.Skipped)}");
        output.WriteLine($"  {"fallidos",-26} {results.Sum(r => r.Failed)}");
        output.WriteLine($"  {"ya venían como adjunto",-26} {results.Sum(r => r.Duplicated)}");
        output.WriteLine($"  {"piezas no entregadas por V1",-26} {results.Sum(r => r.Issues.Count)}");

        if (dryRun)
        {
            output.WriteLine();
            output.WriteLine("  ◇ DRY-RUN: no se subió ningún binario ni se escribió en V2.");
        }

        // Un trámite sin piezas materializadas y sin issues es sospechoso: o ya estaba todo hecho,
        // o V1 no tenía nada que entregar. Se dice explícitamente para que nadie lo lea como éxito.
        var vacios = results.Count(r =>
            r.Status is SnapshotLoadStatus.Materialized or SnapshotLoadStatus.Simulated
            && r.Materialized == 0 && r.Skipped == 0 && r.Duplicated == 0);
        if (vacios > 0)
        {
            output.WriteLine();
            output.WriteLine($"  ⚠ {vacios} trámite(s) no materializaron ninguna pieza: revisa los motivos arriba.");
        }
    }
}
