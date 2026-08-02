using Flit.DataMigration.Api.Authorization;
using Flit.DataMigration.Api.Contracts;
using Flit.DataMigration.Api.Logging;
using Flit.DataMigration.V1.Configuration;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Flit.DataMigration.Api.Endpoints;

/// <summary>
/// Migra UN trámite de V1 a V2 y devuelve el reporte de esa migración.
/// <para>
/// Un id por petición a propósito: un lote entero no cabe en un ciclo request/response (una ola de
/// 500 trámites con instancia 3 son horas) y exigiría cola, estado persistido y reanudación. Con un
/// id, el Runner de Postman iterando un CSV ya ES la cola, y guarda el resultado de cada iteración.
/// </para>
/// </summary>
internal static class MigracionEndpoints
{
    /// <summary>Orden canónico: los adjuntos exigen que la data plana exista.</summary>
    private static readonly MigrationInstance[] Todas =
        [MigrationInstance.Data, MigrationInstance.Attachments, MigrationInstance.Documents];

    internal static IEndpointRouteBuilder MapMigracionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/migracion")
            // En el grupo, para que ninguna ruta futura pueda olvidar la llave.
            .AddEndpointFilter<MigracionKeyFilter>();

        // El tope de concurrencia va SOLO aquí y no en el grupo: protege la memoria del contenedor
        // frente a los buffers de 256 MB de la instancia 3, y una consulta de estado no reserva ni
        // un byte de esos. Colgarlo del grupo haría que refrescar la consola —que reconcilia al
        // cargar— devolviera 429 mientras hay dos migraciones en curso, justo cuando más falta hace
        // poder mirar.
        group.MapPost("/{tramite}/{v1Id:long}", MigrarAsync)
            .AddEndpointFilter<MigracionConcurrencyFilter>()
            .WithName("MigrarTramite");

        group.MapGet("/estado/{tramite}", ConsultarEstadoAsync)
            .WithName("ConsultarEstadoMigracion");

        return app;
    }

    /// <summary>
    /// Solo lectura: dice qué ids de la lista ya están en la libreta. No toca V1, no toca V2 y no
    /// pide el candado — no hay nada que serializar contra una lectura.
    /// <para>
    /// Los ids viajan en el query como lista separada por comas y no en un cuerpo JSON porque un
    /// GET con cuerpo es terreno resbaladizo (proxies que lo descartan, <c>fetch</c> que lo
    /// prohíbe). El tope de <see cref="MaxIdsPorConsulta"/> es lo que impide que una URL enorme
    /// tumbe la petición contra el límite de cabeceras de nginx con un 414 críptico.
    /// </para>
    /// </summary>
    private static async Task<IResult> ConsultarEstadoAsync(
        string tramite,
        [FromServices] MigrationMapStore migrationMap,
        CancellationToken cancellationToken,
        [FromQuery] string? ids = null)
    {
        var kind = ResolveKind(tramite);
        if (kind is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "migracion.tramite_desconocido",
                detail: $"'{tramite}' no es un tipo de trámite. Válidos: registration, transfer.");
        }

        var (pedidos, invalido) = ParseIds(ids);
        if (invalido is not null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "migracion.id_invalido",
                detail: $"'{invalido}' no es un id de V1. Se espera una lista de enteros separados por comas.");
        }

        if (pedidos.Count == 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "migracion.sin_ids",
                detail: "Indica al menos un id en el parámetro 'ids'.");
        }

        if (pedidos.Count > MaxIdsPorConsulta)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "migracion.demasiados_ids",
                detail: $"Máximo {MaxIdsPorConsulta} ids por consulta; llegaron {pedidos.Count}. Divide la lista.");
        }

        var encontrados = await migrationMap.FindEntriesAsync(
            kind.Tables.Master, pedidos, cancellationToken);

        // Se responde en el ORDEN en que llegaron y con un item por id pedido, incluidos los que no
        // están en la libreta: así el cliente casa la respuesta con su tabla por posición y no tiene
        // que inventar los huecos.
        var items = pedidos
            .Select(id => EstadoItemDto.From(id, encontrados.GetValueOrDefault(id)))
            .ToList();

        return Results.Ok(new EstadoRespuesta(kind.CliName, kind.Tables.Master, items));
    }

    /// <summary>
    /// Tope de ids por consulta de estado. Doscientos caben de sobra en una URL (unos 1,4 kB en el
    /// peor caso, frente a los 8 kB que admite nginx por defecto) y cubren un CSV grande de una ola.
    /// </summary>
    private const int MaxIdsPorConsulta = 200;

    private static (IReadOnlyList<long> Ids, string? Invalido) ParseIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return ([], null);
        }

        var vistos = new List<long>();
        foreach (var token in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(token, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var id))
            {
                return ([], token);
            }

            if (!vistos.Contains(id))
            {
                vistos.Add(id);
            }
        }

        return (vistos, null);
    }

    /// <summary>
    /// <c>force</c> NO existe aquí, y no por descuido: <c>--force</c> hace
    /// <c>UPDATE … SET status='borrador'</c> para esquivar el trigger de inmutabilidad y luego
    /// BORRA el trámite y sus hijos en cascada. En Postman se duplica la petición anterior y se
    /// cambia un carácter. Re-migrar sigue siendo del comando, donde alguien tuvo que entrar por
    /// SSH. Reintentar por HTTP es inofensivo sin él: los loaders devuelven <c>Skipped</c>.
    /// </summary>
    private static async Task<IResult> MigrarAsync(
        string tramite,
        long v1Id,
        [FromServices] MigrationRunner runner,
        [FromServices] MigrationSettings settings,
        [FromServices] MigrationMapStore migrationMap,
        [FromServices] MigrationLock migrationLock,
        [FromServices] ILogger<MigracionApiOptions> logger,
        CancellationToken cancellationToken,
        [FromQuery] string? instancias = null,
        [FromQuery] bool dryRun = false,
        [FromQuery] string? batchId = null)
    {
        var kind = ResolveKind(tramite);
        if (kind is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "migracion.tramite_desconocido",
                detail: $"'{tramite}' no es un tipo de trámite. Válidos: registration, transfer.");
        }

        var (secuencia, instanciaInvalida) = ResolveInstances(instancias);
        if (instanciaInvalida is not null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "migracion.instancia_desconocida",
                detail: $"'{instanciaInvalida}' no es una instancia. Válidas: todas, datos, adjuntos, documentos.");
        }

        var lote = string.IsNullOrWhiteSpace(batchId) ? settings.BatchId : batchId;

        // El candado es por trámite y NO espera: una migración en curso puede durar minutos, así
        // que encolar solo trasladaría el problema al timeout del cliente.
        await using var candado = await migrationLock.TryAcquireAsync(
            kind.Tables.Master, v1Id, cancellationToken);
        if (candado is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "migracion.tramite_en_curso",
                detail: $"El trámite {v1Id} ya se está migrando ahora mismo. Reintenta cuando termine.");
        }

        // Se lee ANTES de tocar nada: es la foto de cómo estaba el trámite al llegar la petición.
        var previo = await migrationMap.FindEntryAsync(kind.Tables.Master, v1Id, cancellationToken);

        MigrationOrigin? origen = null;
        var resultados = new List<InstanciaDto>();

        foreach (var instancia in secuencia)
        {
            var request = new MigrationRequest(
                kind, instancia, [v1Id], dryRun, Force: false,
                KeepIdentityImages: false, lote, $"{tramite}-{instancia}".ToLowerInvariant());

            origen ??= runner.Describe(request);

            var (dto, failure) = instancia switch
            {
                MigrationInstance.Attachments => Project(
                    await runner.RunAttachmentsAsync(request, cancellationToken), InstanciaDto.FromAttachments),
                MigrationInstance.Documents => Project(
                    await runner.RunDocumentsAsync(request, cancellationToken), InstanciaDto.FromDocuments),
                _ => Project(
                    await runner.RunDataAsync(request, cancellationToken), InstanciaDto.FromData),
            };

            if (failure is not null)
            {
                MigracionLog.ConfiguracionInvalida(logger, v1Id, failure.Code, failure.Message);

                // 503 y no 400: ninguno de estos fallos lo causó quien llamó — son configuración
                // del propio host de migración. Un 400 mandaría a operaciones a revisar su
                // petición en vez del .env del ambiente.
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: failure.Code,
                    detail: failure.Message);
            }

            resultados.Add(dto!);

            // Si la data plana no entró, adjuntos y documentos no tienen a qué colgarse.
            if (instancia == MigrationInstance.Data && dto!.ConProblemas)
            {
                MigracionLog.DetenidoTrasDatos(logger, v1Id, dto.Estado, dto.Motivo);
                break;
            }
        }

        // Se relee la libreta DESPUÉS de correr para saber dónde quedó el trámite. No se deduce del
        // V2Id que ya traen las instancias porque falta la otra mitad —el tenant— y porque en un
        // dry-run ese id es el que TENDRÍA, no uno que exista: ofrecer un enlace ahí llevaría a un
        // 404. Por eso se consulta la libreta, que solo tiene fila si algo se escribió de verdad.
        var destino = dryRun
            ? null
            : DestinoDe(await migrationMap.FindEntryAsync(kind.Tables.Master, v1Id, cancellationToken));

        var respuesta = new MigracionRespuesta(
            OrigenDto.From(origen!, v1Id), YaMigradoDto.From(previo), resultados)
        {
            Destino = destino,
        };

        MigracionLog.Terminado(logger, v1Id, lote, respuesta.ConProblemas);
        return Results.Ok(respuesta);
    }

    private static DestinoDto? DestinoDe(MigrationMapEntry? entry) =>
        entry is null ? null : new DestinoDto(entry.V2Id, entry.TenantId);

    private static (InstanciaDto? Dto, MigrationFailure? Failure) Project<TReport>(
        (TReport? Report, MigrationFailure? Failure) outcome, Func<TReport, InstanciaDto> map)
        where TReport : class =>
        outcome.Failure is not null ? (null, outcome.Failure) : (map(outcome.Report!), null);

    /// <summary>
    /// Se resuelve contra <see cref="V1ProcedureKind.All"/>, la misma lista que usa el parser de
    /// <c>--tipo</c>: añadir un tipo de trámite nuevo no debe exigir tocar también el endpoint.
    /// </summary>
    private static V1ProcedureKind? ResolveKind(string tramite) =>
        V1ProcedureKind.All.FirstOrDefault(k =>
            string.Equals(k.CliName, tramite, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Las instancias se ejecutan SIEMPRE en orden canónico, sin importar el orden en que
    /// lleguen: un CSV no debería fallar por escribirlas al revés, y el orden no es negociable
    /// (los adjuntos exigen que el trámite exista).
    /// </summary>
    private static (IReadOnlyList<MigrationInstance> Secuencia, string? Invalida) ResolveInstances(
        string? instancias)
    {
        if (string.IsNullOrWhiteSpace(instancias) ||
            string.Equals(instancias, "todas", StringComparison.OrdinalIgnoreCase))
        {
            return (Todas, null);
        }

        var pedidas = new HashSet<MigrationInstance>();
        foreach (var token in instancias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "datos": pedidas.Add(MigrationInstance.Data); break;
                case "adjuntos": pedidas.Add(MigrationInstance.Attachments); break;
                case "documentos": pedidas.Add(MigrationInstance.Documents); break;
                default: return ([], token);
            }
        }

        return ([.. Todas.Where(pedidas.Contains)], null);
    }
}
