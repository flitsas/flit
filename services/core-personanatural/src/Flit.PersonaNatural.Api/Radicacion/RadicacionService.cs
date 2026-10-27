using System.Text.Json;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Solicitudes;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>Resultado de radicar: lo que se le responde al ciudadano con el <c>202</c>.</summary>
public sealed record RadicacionAceptada(Guid SolicitudId, string CodigoPublico, string Estado);

/// <summary>
/// Radicar el trámite (HU #14049, ADR-0075): valida que la solicitud esté completa contra su snapshot y que la compuerta de
/// identidad la deje pasar, la pasa a <c>en_cola</c> y deja el trabajo <c>tramites.persona_natural.radicacion_solicitada</c> en
/// la outbox del SDK en LA MISMA transacción: o quedan las dos cosas o ninguna. El broker no está en el camino de la
/// respuesta: si está caído, la solicitud ya quedó encolada y el publicador de la outbox entrega cuando vuelva. La llave de
/// idempotencia hace que reintentar no cree un segundo evento ni cambie el resultado.
/// </summary>
public sealed class RadicacionService(
    PersonaNaturalDb db,
    SolicitudesService solicitudes,
    PersonaService personas,
    DefinicionesService definiciones,
    ICompuertaDeRadicacion compuerta,
    IPlatformOutbox outbox,
    CifradoPii cifrado,
    TimeProvider reloj)
{
    public const string TipoEvento = "tramites.persona_natural.radicacion_solicitada";
    public const int VersionEvento = 1;
    public const string CodigoIdempotenciaRequerida = "idempotency_key_requerida";
    public const string CodigoIdempotenciaConflicto = "idempotency_conflicto";
    public const string CodigoRequisitosIncompletos = "requisitos_incompletos";
    public const string CodigoIdentidadPendiente = "identity_pending";
    public const string ContextoActores = "radicacion.actores";
    public const int MaxLlave = 100;
    private const string IndiceLlave = "uq_solicitud_idem";

    public async Task<RadicacionAceptada> RadicarAsync(Guid id, string? llave, CancellationToken ct)
    {
        llave = llave?.Trim();
        if (string.IsNullOrEmpty(llave) || llave.Length > MaxLlave)
            throw new ReglaDeNegocioException(StatusCodes.Status400BadRequest, CodigoIdempotenciaRequerida, "Falta la clave", $"Envíe una Idempotency-Key de hasta {MaxLlave} caracteres.");

        try
        {
            return await EnTransaccionAsync(ct2 => RadicarEnTransaccionAsync(id, llave, ct2), ct).ConfigureAwait(false);
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo == SolicitudesService.CodigoConflictoVersion)
        {
            // Dos peticiones a la vez con la misma llave: la que perdió vuelve a mirar y encuentra el resultado de la ganadora.
            return await EnTransaccionAsync(ct2 => RadicarEnTransaccionAsync(id, llave, ct2), ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndiceLlave })
        {
            throw LlaveEnUso();
        }
    }

    private async Task<RadicacionAceptada> RadicarEnTransaccionAsync(Guid id, string llave, CancellationToken ct)
    {
        var s = await db.Set<SolicitudTramite>().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw SolicitudesService.NoEncontrada();

        // Misma llave sobre la misma solicitud ya aceptada: el mismo resultado y ningún evento nuevo.
        if (s.IdempotencyKey == llave && !SolicitudesService.EsEditable(s.Estado))
            return new RadicacionAceptada(s.Id, s.CodigoPublico, s.Estado);
        if (!SolicitudesService.EsEditable(s.Estado))
            throw SolicitudesService.NoPermiteCambio(s.Estado);
        if (await db.Set<SolicitudTramite>().AnyAsync(x => x.TenantId == s.TenantId && x.IdempotencyKey == llave && x.Id != s.Id, ct).ConfigureAwait(false))
            throw LlaveEnUso();

        // El tipo tiene que seguir siendo elegible. Si FLIT 2 no responde no se bloquea: la cola existe para eso.
        try
        {
            await definiciones.ObtenerAsync(s.TipoTramiteCodigo, s.OrganismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException ex) when (ex.Codigo == DefinicionesService.CodigoTipoNoDisponible)
        {
            throw SolicitudesService.TipoNoDisponible();
        }
        catch (FachadaException)
        {
        }

        var borrador = await solicitudes.ConstruirAsync(s, conPersonas: false, null, ct).ConfigureAwait(false);
        if (borrador.Pendientes.Count > 0)
        {
            throw new ReglaDeNegocioException(
                StatusCodes.Status422UnprocessableEntity, CodigoRequisitosIncompletos, "Falta información", "Complete lo que falta para enviar el trámite.",
                new Dictionary<string, object?> { ["pendientes"] = borrador.Pendientes });
        }

        var veredicto = await compuerta.EvaluarAsync(s.Id, ct).ConfigureAwait(false);
        if (!veredicto.Permite)
        {
            throw new ReglaDeNegocioException(
                StatusCodes.Status409Conflict, CodigoIdentidadPendiente, "Falta validar la identidad", "Todas las partes del trámite deben validar su identidad antes de enviarlo.",
                new Dictionary<string, object?> { ["motivo"] = veredicto.Motivo, ["partes"] = veredicto.Partes });
        }

        var evento = await ArmarEventoAsync(s, borrador, ct).ConfigureAwait(false);
        var ahora = reloj.GetUtcNow();
        db.Add(new HistorialEstado { Id = Guid.CreateVersion7(), SolicitudId = s.Id, Desde = s.Estado, Hasta = EstadoSolicitud.EnCola, Origen = "bff", Motivo = "radicada", OcurrioEn = ahora });
        s.Estado = EstadoSolicitud.EnCola;
        s.IdempotencyKey = llave;
        s.ExpiraBorradorEn = null;
        outbox.EnqueueJob(TipoEvento, VersionEvento, PlatformTenants.PersonaNatural, evento);
        await solicitudes.GuardarAsync(s, ct).ConfigureAwait(false);
        return new RadicacionAceptada(s.Id, s.CodigoPublico, s.Estado);
    }

    /// <summary>Arma el trabajo para FLIT 2. Los datos personales se leen por <see cref="PersonaService"/> (queda en la auditoría) y salen cifrados.</summary>
    private async Task<RadicacionSolicitada> ArmarEventoAsync(SolicitudTramite s, SolicitudDto borrador, CancellationToken ct)
    {
        var personasClaro = await personas.LeerAsync(s.Id, "radicacion", ct).ConfigureAwait(false);
        var validaciones = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == s.Id)
            .Select(p => new { p.Parte, p.Ordinal, p.ValidacionId, p.ValidacionEstado }).ToListAsync(ct).ConfigureAwait(false);

        var actores = personasClaro.Select(p =>
        {
            var v = validaciones.First(x => x.Parte == p.Parte && x.Ordinal == p.Ordinal);
            return new ActorRadicado(p.Parte, p.Ordinal, p.TipoDocumento, v.ValidacionId, v.ValidacionEstado);
        }).ToList();
        var cifrados = cifrado.Cifrar(
            JsonSerializer.Serialize(personasClaro.Select(p => new DatosParteRadicada(p.Parte, p.Ordinal, p.TipoDocumento, p.Documento, p.Nombre, p.Correo, p.Celular, p.Direccion)), EventEnvelope.JsonOptions),
            ContextoActores)!;

        var adjuntos = await db.Set<Adjunto>().AsNoTracking().Where(a => a.SolicitudId == s.Id).OrderBy(a => a.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var comprobante = adjuntos.LastOrDefault(a => a.Tipo == SolicitudesService.TipoComprobantePago);
        return new RadicacionSolicitada(
            s.Id, s.CodigoPublico, s.TipoTramiteCodigo, s.DefinicionVersion, borrador.DefinicionHuella, s.OrganismoId, s.Placa, s.Vin,
            borrador.FieldValues, borrador.Datos, actores, Convert.ToBase64String(cifrados),
            [.. adjuntos.Select(a => new AdjuntoRadicado(a.Id, a.Tipo, a.NombreArchivo, a.MimeType, a.TamanoBytes, a.Sha256, a.StoragePath))],
            comprobante?.Id);
    }

    private static ReglaDeNegocioException LlaveEnUso() =>
        new(StatusCodes.Status409Conflict, CodigoIdempotenciaConflicto, "Clave ya usada", "Esa Idempotency-Key ya se usó con otro trámite.");

    /// <summary>La transacción propia exige correr dentro de la estrategia de ejecución (reintentos de Npgsql).</summary>
    private Task<T> EnTransaccionAsync<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async ct2 =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct2).ConfigureAwait(false);
            var resultado = await trabajo(ct2).ConfigureAwait(false);
            await tx.CommitAsync(ct2).ConfigureAwait(false);
            return resultado;
        }, ct);
}
