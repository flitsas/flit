using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Saneamiento del lado de la compañía (plan §6.1, §6.3; HU #14576 AC1–AC8, AC26, AC33, AC34; SP-9, SP-15): marcar, desmarcar, el
/// masivo y el detalle, siempre con el contexto de la compañía del token y la visibilidad de <see cref="AccesoLote"/>. Orden de cada
/// ruta con <c>{id}/{vid}</c> (SP-9): lote visible (404), vehículo de ese lote (404), y solo entonces los códigos de negocio. Las
/// escrituras van por <see cref="EscritorSaneamiento"/> en una transacción. Logs: ids, estados y conteos; nunca placa ni textos.
/// </summary>
internal sealed partial class SolicitudSaneamiento(
    DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj, ILogger<SolicitudSaneamiento> logger, Infraestructura.Archivos.IFabricaAlmacen almacenes)
{
    public const string CodigoNoTerminado = "VEHICULO_NO_TERMINADO";
    public const string CodigoYaSolicitado = ElegibilidadSaneamiento.YaSolicitado;
    public const string CodigoEnGestion = "SANEAMIENTO_EN_GESTION";
    public const string CodigoSinSaneamiento = "SIN_SANEAMIENTO";
    public const string CodigoListaInvalida = "LISTA_INVALIDA";
    public const string MotivoNoEncontrado = "NO_ENCONTRADO";

    /// <summary>Tope del masivo (§6.1), sobre los ids distintos (SP-9).</summary>
    public const int MaximoMasivo = 500;

    /// <summary>PUT: 201 con el saneamiento nuevo; 200 si ya estaba SOLICITADO (idempotente); 409 si no se puede.</summary>
    public async Task<IResult> MarcarAsync(Guid id, Guid vid, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        await VehiculoDelLoteAsync(cabecera, vid, ct).ConfigureAwait(false);
        var vigente = await Vigentes(cabecera.TenantId, vid).AsNoTracking().Select(s => (EstadoSaneamiento?)s.Estado).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (vigente == EstadoSaneamiento.Solicitado)
            return Results.Ok(await DetalleDeAsync(cabecera.TenantId, vid, ct).ConfigureAwait(false));
        var estados = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.TenantId == cabecera.TenantId && c.DiagnosticoId == id && c.VehiculoId == vid)
            .Select(c => c.Estado).ToListAsync(ct).ConfigureAwait(false);
        switch (ElegibilidadSaneamiento.MotivoNoElegible(estados, vigente))
        {
            case ElegibilidadSaneamiento.YaSolicitado:
                throw YaSolicitado();
            case ElegibilidadSaneamiento.NoTerminado:
                throw ReglaException.Conflicto(CodigoNoTerminado, "El vehículo todavía no termina sus consultas.");
        }

        // La segunda de dos marcas simultáneas espera el bloqueo de la cabecera y relee: sale 409 y su transacción se revierte (AC4).
        var aplicadas = await EnTransaccionAsync(async () =>
        {
            var escritas = await EscritorSaneamiento.SolicitarAsync(db, [new(cabecera.TenantId, id, vid)], usuario.Id, reloj.GetUtcNow(), ct).ConfigureAwait(false);
            return escritas.Creados.Count == 0 ? throw YaSolicitado() : escritas;
        }, ct).ConfigureAwait(false);
        LogMarcado(logger, aplicadas.Creados[0].Id, vid, id);
        return Results.Created((string?)null, await DetalleDeAsync(cabecera.TenantId, vid, ct).ConfigureAwait(false));
    }

    /// <summary>DELETE: 204; 409 <see cref="CodigoEnGestion"/> fuera de SOLICITADO; 404 <see cref="CodigoSinSaneamiento"/> sin uno no anulado.</summary>
    public async Task<IResult> DesmarcarAsync(Guid id, Guid vid, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        await VehiculoDelLoteAsync(cabecera, vid, ct).ConfigureAwait(false);
        try
        {
            var saneamiento = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var saneamiento = await Vigentes(cabecera.TenantId, vid).SingleOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw SinSaneamiento();
                if (saneamiento.Estado != EstadoSaneamiento.Solicitado)
                    throw EnGestion();
                await EscritorSaneamiento.TransicionarAsync(
                    db, saneamiento, AccionSaneamiento.Desmarcar, ActorSaneamiento.Compania, usuario.Id, null, reloj.GetUtcNow(), ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaccion.CommitAsync(ct).ConfigureAwait(false);
                return saneamiento;
            }).ConfigureAwait(false);
            LogDesmarcado(logger, saneamiento.Id, vid, id);
            return Results.NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            // FLIT lo movió entre la lectura y el guardado: ya no está SOLICITADO para la compañía.
            throw EnGestion();
        }
    }

    /// <summary>
    /// POST masivo: lista de 1 a <see cref="MaximoMasivo"/> uuids distintos (los repetidos no cuentan, SP-9), de uno o varios lotes.
    /// Los ids que el usuario no ve, por compañía o por visibilidad PROPIOS, salen como <see cref="MotivoNoEncontrado"/>, igual que
    /// los inexistentes. Síncrono, en una transacción, sin llamadas externas.
    /// </summary>
    public async Task<IResult> SolicitarMasivoAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = await LeerListaAsync(request, ct).ConfigureAwait(false);
        var visibles = await AccesoLote.VisiblesAsync(db.Set<CabeceraDiagnostico>(), db, usuario, ct).ConfigureAwait(false);
        var vehiculos = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => ids.Contains(v.Id) && visibles.Any(c => c.Id == v.DiagnosticoId && c.TenantId == v.TenantId))
            .Select(v => new VehiculoParaSolicitar(v.TenantId, v.DiagnosticoId, v.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        var encontrados = vehiculos.Select(v => v.VehiculoId).ToList();
        var estados = (await db.Set<CeldaConsulta>().AsNoTracking()
                .Where(c => encontrados.Contains(c.VehiculoId) && visibles.Any(l => l.Id == c.DiagnosticoId && l.TenantId == c.TenantId))
                .Select(c => new { c.VehiculoId, c.Estado })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(c => c.VehiculoId, c => c.Estado);
        var vigentes = (await db.Set<Saneamiento>().AsNoTracking()
                .Where(s => encontrados.Contains(s.VehiculoId) && s.Estado != EstadoSaneamiento.Anulado)
                .Select(s => new { s.VehiculoId, s.Estado })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(s => s.VehiculoId, s => (EstadoSaneamiento?)s.Estado);

        var porId = vehiculos.ToDictionary(v => v.VehiculoId);
        var motivos = new Dictionary<Guid, string>();
        var elegibles = new List<VehiculoParaSolicitar>();
        foreach (var vid in ids)
        {
            if (!porId.TryGetValue(vid, out var vehiculo))
                motivos[vid] = MotivoNoEncontrado;
            else if (ElegibilidadSaneamiento.MotivoNoElegible(estados[vid], vigentes.GetValueOrDefault(vid)) is { } motivo)
                motivos[vid] = motivo;
            else
                elegibles.Add(vehiculo);
        }

        var aplicadas = elegibles.Count == 0
            ? new SolicitudesAplicadas([], [])
            : await EnTransaccionAsync(() => EscritorSaneamiento.SolicitarAsync(db, elegibles, usuario.Id, reloj.GetUtcNow(), ct), ct).ConfigureAwait(false);
        foreach (var vid in aplicadas.YaSolicitados)
            motivos[vid] = ElegibilidadSaneamiento.YaSolicitado;
        LogMasivo(logger, ids.Count, aplicadas.Creados.Count, motivos.Count);
        return Results.Ok(new SolicitudesSaneamientoDto(
            aplicadas.Creados.Count,
            [.. ids.Where(motivos.ContainsKey).Select(vid => new OmitidoSaneamientoDto(vid, motivos[vid]))]));
    }

    /// <summary>GET: el saneamiento no anulado del vehículo para la compañía (§6.3); 404 <see cref="CodigoSinSaneamiento"/> si no hay.</summary>
    public async Task<SaneamientoClienteDto> DetalleAsync(Guid id, Guid vid, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        await VehiculoDelLoteAsync(cabecera, vid, ct).ConfigureAwait(false);
        return await DetalleDeAsync(cabecera.TenantId, vid, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GET evidencia (AC47, SP-9, SP-18): lote visible → vehículo del lote → saneamiento no anulado → renglón no quitado con evidencia;
    /// cualquier eslabón que falte es 404. El nombre es <c>evidencia-{placa}-{fuente}.pdf</c>, nunca el original.
    /// </summary>
    public async Task<IResult> EvidenciaAsync(Guid id, Guid vid, Guid rid, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        await VehiculoDelLoteAsync(cabecera, vid, ct).ConfigureAwait(false);
        var tenant = cabecera.TenantId;
        var sid = await Vigentes(tenant, vid).AsNoTracking().Select(s => (Guid?)s.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La evidencia");
        var renglon = await db.Set<RenglonSaneamiento>().AsNoTracking()
            .Where(r => r.TenantId == tenant && r.Id == rid && r.SaneamientoId == sid && r.QuitadoEn == null && r.EvidenciaReferencia != null)
            .Select(r => new { r.EvidenciaReferencia, r.Fuente })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La evidencia");
        var placa = await db.Set<VehiculoDiagnostico>().AsNoTracking().Where(v => v.TenantId == tenant && v.Id == vid)
            .Select(v => v.Placa ?? v.IdentificadorNorm).SingleAsync(ct).ConfigureAwait(false);
        var bytes = await almacenes.Para(db).LeerAsync(renglon.EvidenciaReferencia!, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La evidencia");
        return Results.File(bytes, Backoffice.EvidenciaSaneamiento.ContentTypePdf, Backoffice.EvidenciaSaneamiento.NombreDescarga(placa, renglon.Fuente));
    }

    private async Task<SaneamientoClienteDto> DetalleDeAsync(Guid tenant, Guid vid, CancellationToken ct)
    {
        var saneamiento = await Vigentes(tenant, vid).AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw SinSaneamiento();
        var renglones = await db.Set<RenglonSaneamiento>().AsNoTracking()
            .Where(r => r.TenantId == tenant && r.SaneamientoId == saneamiento.Id && r.QuitadoEn == null)
            .OrderBy(r => r.CreadoEn).ThenBy(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        var historial = await db.Set<EventoSaneamiento>().AsNoTracking()
            .Where(e => e.TenantId == tenant && e.SaneamientoId == saneamiento.Id
                && e.Evento != TipoEventoSaneamiento.RenglonActualizado && e.Evento != TipoEventoSaneamiento.ObservacionEditada)
            .OrderBy(e => e.OcurridoEn).ThenBy(e => e.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return new SaneamientoClienteDto(
            saneamiento.Id, saneamiento.Xmin, CodigosDeEnum.Codigo(saneamiento.Estado), saneamiento.SolicitadoEn, saneamiento.SolicitadoPor == usuario.Id,
            saneamiento.TomadoEn, saneamiento.CerradoEn, saneamiento.MotivoCierre, saneamiento.MotivoAnulacion, saneamiento.ObservacionFlit,
            [.. renglones.Select(r => new RenglonClienteDto(
                r.Id, r.Xmin, CodigosDeEnum.Codigo(r.Fuente), r.Hallazgo is { } h ? CodigosDeEnum.Codigo(h) : null, r.Descripcion, r.Automatico,
                CodigosDeEnum.Codigo(r.Resultado), r.Nota,
                r.EvidenciaNombre is { } nombre && r.EvidenciaTamano is { } tamano && r.EvidenciaSubidaEn is { } subida
                    ? new EvidenciaClienteDto(nombre, tamano, subida)
                    : null))],
            [.. historial.Select(e => new EventoClienteDto(
                CodigosDeEnum.Codigo(e.Evento), e.OcurridoEn, e.EsFlit ? "FLIT" : "COMPANIA", e.Fuente is { } f ? CodigosDeEnum.Codigo(f) : null, e.Motivo))]);
    }

    /// <summary>El saneamiento no anulado del vehículo en la compañía del lote (seguido salvo que se pida AsNoTracking).</summary>
    private IQueryable<Saneamiento> Vigentes(Guid tenant, Guid vid) =>
        db.Set<Saneamiento>().Where(s => s.TenantId == tenant && s.VehiculoId == vid && s.Estado != EstadoSaneamiento.Anulado);

    /// <summary>404 si el vehículo no es de ese lote (SP-9: cadena lote → vehículo), comparando también la empresa (SP-5).</summary>
    private async Task VehiculoDelLoteAsync(CabeceraDiagnostico cabecera, Guid vid, CancellationToken ct)
    {
        if (!await db.Set<VehiculoDiagnostico>().AsNoTracking()
                .AnyAsync(v => v.Id == vid && v.DiagnosticoId == cabecera.Id && v.TenantId == cabecera.TenantId, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El vehículo");
    }

    /// <summary>
    /// Corre la escritura en una transacción de la estrategia de reintentos y guarda. La carrera de dos marcas la resuelve el bloqueo de
    /// la cabecera dentro de <see cref="EscritorSaneamiento.SolicitarAsync"/>; si aun así choca el único parcial, es YA_SOLICITADO.
    /// </summary>
    private async Task<SolicitudesAplicadas> EnTransaccionAsync(Func<Task<SolicitudesAplicadas>> escribir, CancellationToken ct)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var aplicadas = await escribir().ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaccion.CommitAsync(ct).ConfigureAwait(false);
                return aplicadas;
            }).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SaneamientoConfig.IndiceVigente })
        {
            throw YaSolicitado();
        }
    }

    /// <summary>
    /// <c>{"vehiculo_ids": [uuid…]}</c> con 1 a <see cref="MaximoMasivo"/> distintos, en el orden pedido. Cuerpo vacío, JSON inválido, sin
    /// lista o con un elemento que no es un uuid: 400 <see cref="CodigoListaInvalida"/> (mensaje fijo, no repite lo enviado).
    /// </summary>
    private static async Task<List<Guid>> LeerListaAsync(HttpRequest request, CancellationToken ct)
    {
        JsonElement raiz;
        try
        {
            raiz = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw ListaInvalida();
        }

        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("vehiculo_ids", out var lista) || lista.ValueKind != JsonValueKind.Array)
            throw ListaInvalida();
        var ids = new List<Guid>();
        var vistos = new HashSet<Guid>();
        foreach (var elemento in lista.EnumerateArray())
        {
            if (elemento.ValueKind != JsonValueKind.String || !Guid.TryParse(elemento.GetString(), out var vid))
                throw ListaInvalida();
            if (vistos.Add(vid))
                ids.Add(vid);
        }

        return ids.Count is >= 1 and <= MaximoMasivo ? ids : throw ListaInvalida();
    }

    private static ReglaException ListaInvalida() =>
        new(CodigoListaInvalida, $"vehiculo_ids debe ser una lista de 1 a {MaximoMasivo} identificadores de vehículo distintos.", StatusCodes.Status400BadRequest)
        {
            Campo = "vehiculo_ids",
        };

    private static ReglaException YaSolicitado() =>
        ReglaException.Conflicto(CodigoYaSolicitado, "El vehículo ya tiene un saneamiento solicitado o en gestión.");

    private static ReglaException EnGestion() =>
        ReglaException.Conflicto(CodigoEnGestion, "FLIT ya empezó a gestionar este saneamiento; ya no se puede desmarcar.");

    private static ReglaException SinSaneamiento() =>
        new(CodigoSinSaneamiento, "El vehículo no tiene un saneamiento solicitado.", StatusCodes.Status404NotFound);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saneamiento {SaneamientoId} solicitado para el vehículo {VehiculoId} del lote {DiagnosticoId}.")]
    private static partial void LogMarcado(ILogger logger, Guid saneamientoId, Guid vehiculoId, Guid diagnosticoId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saneamiento {SaneamientoId} desmarcado por la compañía (vehículo {VehiculoId}, lote {DiagnosticoId}).")]
    private static partial void LogDesmarcado(ILogger logger, Guid saneamientoId, Guid vehiculoId, Guid diagnosticoId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saneamiento masivo: {Pedidos} vehículos, {Solicitados} solicitados, {Omitidos} omitidos.")]
    private static partial void LogMasivo(ILogger logger, int pedidos, int solicitados, int omitidos);
}
