using System.Collections.Concurrent;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ingesta;

/// <summary>Una consulta hecha en el ciclo, con lo que devolvió (la «caché» del ciclo compartida entre gestoras).</summary>
public sealed record ConsultaHecha(ClaveConsulta Clave, ConsultaFuente Registro, ResultadoParcial Parcial);

/// <summary>Avisos de fallos de integración (HU #13462). Sin destinatarios o con el interruptor apagado, no hace nada.</summary>
public interface IAlertasIntegracion
{
    Task FallosDelCicloAsync(CicloIngesta ciclo, IReadOnlyList<(ConsultaFuente Consulta, IReadOnlyList<Guid> Empresas)> fallos, CancellationToken ct);

    Task FalloDelTrabajoAsync(CicloIngesta ciclo, Exception falla, CancellationToken ct);
}

/// <summary>
/// Un ciclo de ingesta de un canal (§6.4.2): arma las claves únicas de todas las empresas (o de una, si es manual),
/// consulta cada clave una sola vez (RN-033), guarda cada consulta con su respuesta cruda, y reparte el resultado a
/// cada empresa con el <see cref="Reconciliador"/>, en una transacción por clave. Un fallo en una clave no detiene las
/// demás (RN-027).
/// </summary>
public sealed partial class CicloDeIngesta(
    IServiceScopeFactory scopes,
    IFuenteComparendos fuente,
    CalendarioService calendario,
    IAlertasIntegracion alertas,
    Reloj reloj,
    IConfiguration configuration,
    ColaTrabajos cola,
    ILogger<CicloDeIngesta> logger) : IEjecutorTrabajo
{
    private int Paralelismo => Math.Clamp(configuration.GetValue("Comparendos:Ingesta:Paralelismo", 4), 1, 16);

    public bool Atiende(TipoTrabajo tipo) => Canal(tipo) is not null;

    public static CanalIngesta? Canal(TipoTrabajo tipo) => tipo switch
    {
        TipoTrabajo.IngestaSimitNit => CanalIngesta.SimitNit,
        TipoTrabajo.IngestaSimitPlaca => CanalIngesta.SimitPlaca,
        TipoTrabajo.IngestaMunicipalNit => CanalIngesta.MunicipalNit,
        TipoTrabajo.IngestaMunicipalPlaca => CanalIngesta.MunicipalPlaca,
        _ => null,
    };

    public static TipoTrabajo Trabajo(CanalIngesta canal) => canal switch
    {
        CanalIngesta.SimitNit => TipoTrabajo.IngestaSimitNit,
        CanalIngesta.SimitPlaca => TipoTrabajo.IngestaSimitPlaca,
        CanalIngesta.MunicipalNit => TipoTrabajo.IngestaMunicipalNit,
        _ => TipoTrabajo.IngestaMunicipalPlaca,
    };

    public async Task EjecutarAsync(SolicitudTrabajo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await EjecutarAsync(Canal(solicitud.Tipo)!.Value, solicitud.Disparo, solicitud.TenantId, solicitud.UsuarioId, ct).ConfigureAwait(false);
    }

    public async Task<Guid> EjecutarAsync(CanalIngesta canal, Disparo disparo, Guid? tenantManual, Guid? usuario, CancellationToken ct)
    {
        // HU #13521: solo empresas con el producto habilitado y el canal configurado (y activo, si es programado).
        Dictionary<Guid, string> proveedores;
        await using (var scope = scopes.CreateAsyncScope())
            proveedores = await scope.ServiceProvider.GetRequiredService<ElegibilidadConsultas>().ProveedoresAsync(canal, disparo, tenantManual, ct).ConfigureAwait(false);
        if (proveedores.Count == 0)
        {
            LogSinEmpresas(logger, canal);
            return Guid.Empty;
        }

        var ciclo = new CicloIngesta
        {
            Id = Guid.CreateVersion7(),
            Canal = canal,
            Disparo = disparo,
            TenantIdManual = tenantManual,
            SolicitadoPor = usuario,
            InicioEn = reloj.Ahora,
        };
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            db.CiclosIngesta.Add(ciclo);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                throw ReglaException.Conflicto("CICLO_EN_CURSO", $"Ya hay un ciclo de {Reconciliador.Etiqueta(canal)} en curso; espera a que termine.");
            }
        }

        try
        {
            await CorrerAsync(ciclo, proveedores, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFalloCiclo(logger, canal, ex);
            ciclo.Resultado = ResultadoCiclo.Fallido;
            ciclo.MensajeError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await alertas.FalloDelTrabajoAsync(ciclo, ex, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ciclo.FinEn = reloj.Ahora;
            if (ciclo.Resultado == ResultadoCiclo.EnCurso)
                ciclo.Resultado = ResultadoCiclo.Fallido;
            await CerrarAsync(ciclo, [.. proveedores.Keys]).ConfigureAwait(false);
        }

        return ciclo.Id;
    }

    /// <param name="Documento">Número de la identificación consultada (sin DV); null en los canales por placa.</param>
    /// <param name="EtiquetaLog">Etiqueta para logs y nombres de archivo: el documento de una persona va enmascarado (RN-011).</param>
    private sealed record ClaveMonitoreada(Guid TenantId, Guid? IdentificacionId, Guid? PlacaId, IReadOnlyList<ClaveConsulta> Variantes, string Etiqueta, string EtiquetaLog, string? Documento, string Proveedor = "");

    private async Task CorrerAsync(CicloIngesta ciclo, Dictionary<Guid, string> proveedores, CancellationToken ct)
    {
        var porNit = ciclo.Canal is CanalIngesta.SimitNit or CanalIngesta.MunicipalNit;
        List<ClaveMonitoreada> monitoreadas;
        List<string> municipiosActivos;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            municipiosActivos = await db.MunicipiosFuente.Where(m => m.Activo).Select(m => m.CodigoFuente).ToListAsync(ct).ConfigureAwait(false);
            if (porNit)
            {
                var nits = await db.IdentificacionesMonitoreadas.AsNoTracking().Where(n => n.Activo)
                    .Where(n => ciclo.TenantIdManual == null || n.TenantId == ciclo.TenantIdManual).ToListAsync(ct).ConfigureAwait(false);
                monitoreadas = nits.Select(n => new ClaveMonitoreada(n.TenantId, n.Id, null,
                    (n.TipoDocumento == TipoDocumento.NIT ? Nit.Variantes(n.NumeroBase) : [n.NumeroBase])
                        .Select(v => new ClaveConsulta(ciclo.Canal, n.TipoDocumento, v)).ToList(),
                    n.DigitoVerificacion is { } dv ? $"{n.TipoDocumento} {n.NumeroBase}-{dv}" : $"{n.TipoDocumento} {n.NumeroBase}",
                    n.TipoDocumento == TipoDocumento.NIT ? $"NIT {n.NumeroBase}" : $"{n.TipoDocumento} {HtmlCorreo.Enmascarar(n.NumeroBase)}",
                    n.NumeroBase)).ToList();
            }
            else
            {
                var placas = await db.PlacasMonitoreadas.AsNoTracking().Where(p => p.Activo)
                    .Where(p => ciclo.TenantIdManual == null || p.TenantId == ciclo.TenantIdManual).ToListAsync(ct).ConfigureAwait(false);
                monitoreadas = placas.Select(p => new ClaveMonitoreada(p.TenantId, null, p.Id, [new ClaveConsulta(ciclo.Canal, null, p.Placa)], $"placa {p.Placa}", $"placa {p.Placa}", null)).ToList();
            }
        }

        // Solo las empresas elegibles, cada una con el proveedor de su configuración (HU #13521).
        monitoreadas = monitoreadas.Where(m => proveedores.ContainsKey(m.TenantId)).Select(m => m with { Proveedor = proveedores[m.TenantId] }).ToList();
        // Una clave que vigilan varias empresas se consulta una vez, a nombre de una fija (la de menor id): core-consultas
        // registra la llamada real una sola vez (Excel S4).
        var unicas = monitoreadas.SelectMany(m => m.Variantes.Select(v => (Clave: v, m.Proveedor, m.TenantId)))
            .GroupBy(x => (x.Clave, x.Proveedor))
            .Select(g => (g.Key.Clave, g.Key.Proveedor, Empresa: g.Min(x => x.TenantId)))
            .ToList();
        ciclo.ClavesTotal = unicas.Count;

        // 1. Una sola llamada por clave única y proveedor, con paralelismo acotado.
        var cache = new ConcurrentDictionary<(ClaveConsulta Clave, string Proveedor), IReadOnlyList<ConsultaHecha>>();
        await Parallel.ForEachAsync(unicas, new ParallelOptions { MaxDegreeOfParallelism = Paralelismo, CancellationToken = ct }, async (u, token) =>
            cache[(u.Clave, u.Proveedor)] = await ConsultarAsync(ciclo, u.Clave, u.Proveedor, u.Empresa, ciclo.Canal is CanalIngesta.MunicipalNit or CanalIngesta.MunicipalPlaca ? municipiosActivos : [], token).ConfigureAwait(false))
            .ConfigureAwait(false);

        var errores = cache.Values.SelectMany(v => v).Where(h => h.Parcial.Resultado == ResultadoConsulta.Error).ToList();
        ciclo.ClavesError = cache.Count(kv => kv.Value.Any(h => h.Parcial.Resultado == ResultadoConsulta.Error));

        // 2. Repartir a cada empresa, una transacción por clave y municipio.
        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        foreach (var m in monitoreadas)
        {
            var hechas = m.Variantes.SelectMany(v => cache.TryGetValue((v, m.Proveedor), out var h) ? h : []).ToList();
            foreach (var grupo in hechas.GroupBy(h => h.Parcial.MunicipioCodigo))
            {
                // RN-027: un fallo al repartir una clave no detiene las demás. Si otra clave de la misma empresa creó el
                // mismo comparendo a la vez (índice único por número), se reintenta una vez: ahora entra como actualización.
                for (var intento = 1; ; intento++)
                {
                    try
                    {
                        await RepartirAsync(ciclo, m, grupo.Key, grupo.ToList(), m.Variantes.Count, motor, ct).ConfigureAwait(false);
                        break;
                    }
                    catch (DbUpdateException ex) when (intento == 1 && ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogFalloReparto(logger, ciclo.Canal, m.EtiquetaLog, ex);
                        ciclo.ClavesError++;
                        break;
                    }
                }
            }
        }

        ciclo.ClavesError = Math.Min(ciclo.ClavesError, ciclo.ClavesTotal);

        // §6.4.3: lo que entró nuevo y espera conductor por la API de contratos se consulta ya, sin esperar al trabajo diario.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            if (await db.Comparendos.AnyAsync(c => c.CreadoEn >= ciclo.InicioEn && c.EstadoConsultaContraventor == EstadoConsultaContraventor.Pendiente, ct).ConfigureAwait(false))
                await cola.EncolarAsync(new SolicitudTrabajo(TipoTrabajo.ReintentoContraventor, Disparo.Programado), ct).ConfigureAwait(false);
        }

        ciclo.Resultado = ciclo.ClavesTotal == 0 || ciclo.ClavesError == 0 ? ResultadoCiclo.Exitoso
            : ciclo.ClavesError == ciclo.ClavesTotal ? ResultadoCiclo.Fallido
            : ResultadoCiclo.Parcial;

        if (errores.Count > 0)
        {
            var fallos = errores.Select(e => (e.Registro, (IReadOnlyList<Guid>)monitoreadas.Where(m => m.Variantes.Contains(e.Clave)).Select(m => m.TenantId).Distinct().ToList())).ToList();
            await alertas.FallosDelCicloAsync(ciclo, fallos, ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ConsultaHecha>> ConsultarAsync(CicloIngesta ciclo, ClaveConsulta clave, string proveedor, Guid empresa, List<string> municipiosActivos, CancellationToken ct)
    {
        RespuestaFuente respuesta;
        var inicio = reloj.Ahora;
        try
        {
            respuesta = await fuente.ConsultarAsync(clave, proveedor, empresa, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            respuesta = RespuestaFuente.Error(ex is TaskCanceledException or TimeoutException ? TipoErrorFuente.Timeout : TipoErrorFuente.Excepcion,
                ex.Message, duracionMs: (int)(reloj.Ahora - inicio).TotalMilliseconds);
        }

        // Municipal: un municipio activo que no vino en la respuesta cuenta como error de ese municipio (S-48), y lo que
        // traiga de un municipio desactivado en el parametrizador no se procesa (§6.4.1).
        var parciales = respuesta.Parciales.ToList();
        if (municipiosActivos.Count > 0 && parciales.All(p => p.MunicipioCodigo is not null))
        {
            parciales.RemoveAll(p => !municipiosActivos.Contains(p.MunicipioCodigo!));
            foreach (var faltante in municipiosActivos.Where(m => parciales.All(p => p.MunicipioCodigo != m)))
                parciales.Add(new ResultadoParcial(faltante, ResultadoConsulta.Error, [], TipoErrorFuente.RespuestaInvalida, "El municipio no vino en la respuesta."));
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var respuestaError = parciales.Any(p => p.Resultado == ResultadoConsulta.Error) && respuesta.CuerpoCrudo is { Length: > 0 } cuerpo
            ? cuerpo[..Math.Min(cuerpo.Length, ConsultaFuente.MaxRespuestaError)]
            : null;

        var hechas = parciales.Select(p => new ConsultaHecha(clave, new ConsultaFuente
        {
            Id = Guid.CreateVersion7(),
            CicloId = ciclo.Id,
            Canal = clave.Canal,
            TipoDocumento = clave.TipoDocumento,
            ClaveConsultada = clave.Clave,
            MunicipioCodigo = p.MunicipioCodigo,
            HttpStatus = respuesta.HttpStatus,
            Resultado = p.Resultado,
            TipoError = p.TipoError,
            MensajeError = p.MensajeError,
            CantidadItems = p.Items.Count,
            RespuestaError = respuestaError,
            DuracionMs = respuesta.DuracionMs,
            ConsultadoEn = reloj.Ahora,
        }, p)).ToList();
        db.ConsultasFuente.AddRange(hechas.Select(h => h.Registro));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return hechas;
    }

    private async Task RepartirAsync(CicloIngesta ciclo, ClaveMonitoreada m, string? municipio, List<ConsultaHecha> hechas, int variantes, Domain.Plazos.MotorDePlazos motor, CancellationToken ct)
    {
        var exitosas = hechas.Where(h => h.Parcial.Resultado != ResultadoConsulta.Error).ToList();
        // Reconciliar (marcar ausentes) solo si todas las variantes de la clave respondieron bien (RN-034).
        var reconciliar = exitosas.Count == hechas.Count && hechas.Count >= variantes;
        var log = new LogMigracion
        {
            Id = Guid.CreateVersion7(),
            CicloId = ciclo.Id,
            TenantId = m.TenantId,
            Canal = ciclo.Canal,
            IdentificacionMonitoreadaId = m.IdentificacionId,
            PlacaMonitoreadaId = m.PlacaId,
            Clave = m.Etiqueta.Length > 20 ? m.Etiqueta[^20..] : m.Etiqueta,
            MunicipioCodigo = municipio,
            InicioEn = reloj.Ahora,
            Resultado = exitosas.Count == 0 ? ResultadoCiclo.Fallido : reconciliar ? ResultadoCiclo.Exitoso : ResultadoCiclo.Parcial,
            MensajeError = string.Join(" · ", hechas.Where(h => h.Parcial.Resultado == ResultadoConsulta.Error).Select(h => $"{h.Clave.Clave}: {h.Parcial.MensajeError}").Distinct()) is { Length: > 0 } e ? e : null,
        };

        // La estrategia de reintentos de Npgsql exige que la transacción corra dentro de ella; cada intento usa un
        // contexto nuevo para no arrastrar cambios de un intento fallido.
        await using var raiz = scopes.CreateAsyncScope();
        var estrategia = raiz.ServiceProvider.GetRequiredService<ComparendosDb>().Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (exitosas.Count > 0)
            {
                var reconciliador = scope.ServiceProvider.GetRequiredService<Reconciliador>();
                var clave = new ClaveEmpresa(m.TenantId, ciclo.Canal, m.IdentificacionId, m.PlacaId, municipio, m.Etiqueta, m.Documento);
                var c = await reconciliador.AplicarAsync(clave, ciclo.Id, exitosas.SelectMany(h => h.Parcial.Items).ToList(), reconciliar, motor, ct).ConfigureAwait(false);
                (log.CantidadObtenidas, log.CantidadNuevas, log.CantidadActualizadas, log.CantidadSinCambios) = (c.Obtenidas, c.Nuevas, c.Actualizadas, c.SinCambios);
                (log.CantidadDuplicadasOtraFuente, log.CantidadInactivadas, log.CantidadReactivadas, log.CantidadDpFavorableAutomatico) = (c.DuplicadasOtraFuente, c.Inactivadas, c.Reactivadas, c.DpFavorableAutomatico);
            }

            log.FinEn = reloj.Ahora;
            db.LogMigracion.Add(log);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task CerrarAsync(CicloIngesta ciclo, List<Guid> empresas)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        db.CiclosIngesta.Update(ciclo);
        var tipo = Trabajo(ciclo.Canal);
        await db.TrabajosProgramados.Where(t => t.Tipo == tipo)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.UltimoResultado, ciclo.Resultado), CancellationToken.None).ConfigureAwait(false);
        {
            await db.ConfiguracionConsultaCanales.Where(c => empresas.Contains(c.TenantId) && c.Canal == ciclo.Canal)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.UltimoResultado, ciclo.Resultado), CancellationToken.None).ConfigureAwait(false);
        }
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el reparto de {Canal} para {Clave}; el ciclo sigue con las demás.")]
    private static partial void LogFalloReparto(ILogger logger, CanalIngesta canal, string clave, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ciclo de {Canal} omitido: ninguna empresa elegible (producto habilitado y consultas configuradas).")]
    private static partial void LogSinEmpresas(ILogger logger, CanalIngesta canal);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el ciclo de ingesta {Canal}.")]
    private static partial void LogFalloCiclo(ILogger logger, CanalIngesta canal, Exception ex);
}
