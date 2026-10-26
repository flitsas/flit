using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Publica el trabajo de consulta de una celda (ADR-0075, T6, HU #13727): arma sus consultas con el <see cref="IConstructorSolicitud"/>
/// de su fuente, le da un <c>trabajo_id</c> nuevo (UUIDv7, <see cref="CeldaConsulta.TrabajoId"/>) y deja en la outbox del scope un
/// trabajo <c>consultas.diagnostico[-express].&lt;fuente&gt;</c> por consulta (en las celdas de persona, uno por persona con el
/// mismo <c>trabajo_id</c>), con la solicitud en proto-JSON y en claro (U1 = b). El prefijo <c>-express</c> sale de
/// <see cref="CabeceraDiagnostico.EsExpress"/> del lote. No guarda: el trabajo entra en la transacción de quien guarda la celda,
/// así que si esa se revierte no queda mensaje. La celda debe venir del MISMO contexto que la outbox (el del scope, con
/// <c>EmpresaDelTrabajo</c> en los trabajos en segundo plano).
/// <para>
/// Tanda A: nadie consume todavía y el procesador de Postgres sigue ejecutando la celda; la reutilización del mismo día no se
/// mira aquí (la sigue haciendo el procesador). Si la celda no tiene qué consultar, o su constructor falla (p. ej. un documento
/// que no se puede descifrar), no se publica nada, el <c>trabajo_id</c> queda en null y el motor de hoy sigue igual; en la tanda B
/// el reconciliador publica las PENDIENTE con <c>trabajo_id</c> null. Los logs llevan ids y fuente, nunca la solicitud.
/// </para>
/// </summary>
/// <example>
/// <code>
/// db.Add(celda);
/// await publicador.PublicarAsync(celda, ct); // trabajo_id nuevo + outbox, sin guardar
/// await db.SaveChangesAsync(ct);             // celda y trabajo en la misma transacción
/// </code>
/// </example>
internal sealed partial class PublicadorCeldas(
    DiagnosticoDb db, IPlatformOutbox outbox, IEnumerable<IConstructorSolicitud> constructores, ILogger<PublicadorCeldas> log)
{
    private readonly Dictionary<Guid, bool> _express = [];

    /// <summary>Publica el trabajo de la celda y devuelve cuántas consultas dejó en la outbox (0 si ninguna).</summary>
    public async Task<int> PublicarAsync(CeldaConsulta celda, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(celda);
        if (db.Entry(celda).State == EntityState.Detached)
            throw new InvalidOperationException("La celda que se publica debe venir del contexto del scope, el mismo de la outbox.");

        IReadOnlyList<SolicitudConsulta> solicitudes = [];
        if (constructores.FirstOrDefault(c => c.Fuente == celda.Fuente) is { } constructor)
        {
            try
            {
                solicitudes = await constructor.ConstruirAsync(db, celda, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Tanda A: publicar no cambia el motor; la celda sigue por el procesador de siempre.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                SinSolicitud(log, celda.Id, celda.Fuente, ex.GetType().Name);
                solicitudes = [];
            }
        }

        if (solicitudes.Count == 0)
        {
            // Un trabajo anterior (un reproceso sin nada que pedir) deja de ser el vigente.
            celda.TrabajoId = null;
            return 0;
        }

        var esExpress = await EsExpressAsync(celda.DiagnosticoId, ct).ConfigureAwait(false);
        var trabajo = Guid.CreateVersion7();
        celda.TrabajoId = trabajo;
        foreach (var solicitud in solicitudes)
        {
            outbox.EnqueueJob(TrabajoConsultaDiagnostico.Clave(solicitud.Fuente, esExpress), TrabajoConsultaDiagnostico.VersionContrato,
                celda.TenantId, TrabajoConsultaDiagnostico.Crear(trabajo, celda.Id, solicitud));
        }

        Publicada(log, celda.Id, celda.Fuente, trabajo, solicitudes.Count, esExpress);
        return solicitudes.Count;
    }

    private async Task<bool> EsExpressAsync(Guid diagnosticoId, CancellationToken ct)
    {
        if (_express.TryGetValue(diagnosticoId, out var esExpress))
            return esExpress;
        esExpress = db.ChangeTracker.Entries<CabeceraDiagnostico>().FirstOrDefault(e => e.Entity.Id == diagnosticoId)?.Entity.EsExpress
            ?? await db.Set<CabeceraDiagnostico>().Where(c => c.Id == diagnosticoId).Select(c => c.EsExpress).SingleAsync(ct).ConfigureAwait(false);
        _express[diagnosticoId] = esExpress;
        return esExpress;
    }

    [LoggerMessage(EventId = 137275, Level = LogLevel.Information,
        Message = "La celda {CeldaId} de {Fuente} publicó el trabajo {TrabajoId} con {Consultas} consultas (express: {EsExpress})")]
    private static partial void Publicada(ILogger logger, Guid celdaId, Fuente fuente, Guid trabajoId, int consultas, bool esExpress);

    [LoggerMessage(EventId = 137276, Level = LogLevel.Warning,
        Message = "La celda {CeldaId} de {Fuente} no pudo armar su solicitud ({TipoError}); no se publica y sigue por el procesador")]
    private static partial void SinSolicitud(ILogger logger, Guid celdaId, Fuente fuente, string tipoError);
}
