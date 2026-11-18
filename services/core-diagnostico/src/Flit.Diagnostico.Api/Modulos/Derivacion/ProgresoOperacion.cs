using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>Contadores de una operación «Crear trámite» (<c>ProgresoOperacion</c> del contrato).</summary>
public sealed record ResumenOperacionDto(
    [property: JsonPropertyName("operacion_id")] Guid OperacionId,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("tipo_tramite_codigo")] string TipoTramiteCodigo,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("creadas")] int Creadas,
    [property: JsonPropertyName("omitidas")] int Omitidas,
    [property: JsonPropertyName("fallidas")] int Fallidas,
    [property: JsonPropertyName("pendientes")] int Pendientes,
    [property: JsonPropertyName("creada_en")] DateTimeOffset CreadaEn,
    [property: JsonPropertyName("terminada_en")] DateTimeOffset? TerminadaEn);

/// <summary>Cuántos ítems cerraron OMITIDO o FALLIDO por cada motivo (resumen «omitidas por motivo»).</summary>
public sealed record MotivoOperacionDto(
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("motivo_codigo")] string MotivoCodigo,
    [property: JsonPropertyName("motivo")] string Motivo,
    [property: JsonPropertyName("cantidad")] int Cantidad);

/// <summary>Un vehículo de la operación: su estado, el motivo si no se creó y el trámite si se creó.</summary>
public sealed record ItemOperacionDto(
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("placa")] string Placa,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("motivo_codigo")] string? MotivoCodigo,
    [property: JsonPropertyName("motivo")] string? Motivo,
    [property: JsonPropertyName("tramite_id")] Guid? TramiteId,
    [property: JsonPropertyName("tramite_numero")] string? TramiteNumero,
    [property: JsonPropertyName("tramite_url")] string? TramiteUrl);

/// <summary>Detalle de una operación: contadores, motivos y una página de ítems (filtrable por estado).</summary>
public sealed record DetalleOperacionDto(
    [property: JsonPropertyName("operacion_id")] Guid OperacionId,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("tipo_tramite_codigo")] string TipoTramiteCodigo,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("creadas")] int Creadas,
    [property: JsonPropertyName("omitidas")] int Omitidas,
    [property: JsonPropertyName("fallidas")] int Fallidas,
    [property: JsonPropertyName("pendientes")] int Pendientes,
    [property: JsonPropertyName("creada_en")] DateTimeOffset CreadaEn,
    [property: JsonPropertyName("terminada_en")] DateTimeOffset? TerminadaEn,
    [property: JsonPropertyName("motivos")] IReadOnlyList<MotivoOperacionDto> Motivos,
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano_pagina")] int TamanoPagina,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("items")] IReadOnlyList<ItemOperacionDto> Items);

/// <summary>202 de reintentar fallidas: cuántos FALLIDO volvieron a PENDIENTE y cuántos quedaron OMITIDO por no ser ya elegibles.</summary>
public sealed record ReintentoFallidasDto(
    [property: JsonPropertyName("reencolados")] int Reencolados,
    [property: JsonPropertyName("omitidas")] int Omitidas);

/// <summary>
/// Progreso y reintento de las operaciones «Crear trámite» de un lote (Punto 4, incremento 3, diseño §5.1 filas 3-5). Lee SOLO tablas
/// de Diagnóstico (contadores propios de la operación; nunca llama a core-api) y siempre a través de <see cref="AccesoLote"/>:
/// <list type="bullet">
/// <item><see cref="ListarAsync"/>: las operaciones del lote, de la más reciente a la más antigua (20); con <c>ultima=true</c>, las
/// EN_CURSO y la más reciente (para retomar al volver a la pantalla).</item>
/// <item><see cref="LeerAsync"/>: contadores, motivos agrupados de OMITIDO/FALLIDO y los ítems por <c>orden</c>, filtrados por
/// <c>estado</c> y paginados de a <see cref="TamanoPagina"/>.</item>
/// <item><see cref="ReintentarFallidasAsync"/>: 409 <see cref="CodigoOperacionEnCurso"/> si quedan ítems PENDIENTE o ENVIADO; si no,
/// reabre los FALLIDO que siguen elegibles (el MISMO ítem a PENDIENTE, con quien reintenta como autor de su borrador), deja OMITIDO los que ya no lo son y libera el crédito, en una
/// transacción.</item>
/// </list>
/// </summary>
internal sealed class ProgresoOperacion(
    DiagnosticoDb db, UsuarioActual usuario, DespachadorCredito despachador, IOptions<OpcionesDerivacion> opciones, TimeProvider reloj)
{
    public const int TamanoPagina = 50;
    public const int MaxOperaciones = 20;
    public const string CodigoOperacionEnCurso = "OPERACION_EN_CURSO";
    public const string CodigoEstadoInvalido = "ESTADO_INVALIDO";

    public async Task<IReadOnlyList<ResumenOperacionDto>> ListarAsync(Guid id, bool ultima, CancellationToken ct)
    {
        await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var operaciones = db.Set<OperacionDerivacion>().AsNoTracking().Where(o => o.DiagnosticoId == id);
        var recientes = await operaciones.OrderByDescending(o => o.CreadaEn).ThenByDescending(o => o.Id).Take(ultima ? 1 : MaxOperaciones).ToListAsync(ct).ConfigureAwait(false);
        if (ultima)
        {
            var enCurso = await operaciones.Where(o => o.Estado == EstadoOperacionDerivacion.EnCurso)
                .OrderByDescending(o => o.CreadaEn).ThenByDescending(o => o.Id).Take(MaxOperaciones).ToListAsync(ct).ConfigureAwait(false);
            recientes = [.. enCurso.Concat(recientes).DistinctBy(o => o.Id).OrderByDescending(o => o.CreadaEn).ThenByDescending(o => o.Id)];
        }

        return [.. recientes.Select(Resumen)];
    }

    public async Task<DetalleOperacionDto> LeerAsync(Guid id, Guid operacionId, string? estado, int? pagina, CancellationToken ct)
    {
        EstadoItemDerivacion? filtro = null;
        if (!string.IsNullOrWhiteSpace(estado))
        {
            filtro = CodigosDeEnum.TryLeer<EstadoItemDerivacion>(estado.Trim().ToUpperInvariant(), out var e)
                ? e
                : throw new ReglaException(CodigoEstadoInvalido, "El estado no es válido (PENDIENTE, ENVIADO, CREADO, OMITIDO o FALLIDO).", StatusCodes.Status400BadRequest) { Campo = "estado" };
        }

        var numero = Math.Max(1, pagina ?? 1);
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var operacion = await db.Set<OperacionDerivacion>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == operacionId && o.DiagnosticoId == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La operación");
        var items = db.Set<ItemDerivacion>().AsNoTracking().Where(i => i.OperacionId == operacionId);
        var cerrados = await items.Where(i => (i.Estado == EstadoItemDerivacion.Omitido || i.Estado == EstadoItemDerivacion.Fallido) && i.MotivoCodigo != null)
            .GroupBy(i => new { i.Estado, i.MotivoCodigo })
            .Select(g => new { g.Key.Estado, g.Key.MotivoCodigo, Cantidad = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var filtrados = filtro is { } f ? items.Where(i => i.Estado == f) : items;
        var totalItems = await filtrados.CountAsync(ct).ConfigureAwait(false);
        var pagina1 = await (from i in filtrados
                             join v in db.Set<VehiculoDiagnostico>().AsNoTracking() on new { i.TenantId, i.VehiculoId } equals new { v.TenantId, VehiculoId = v.Id }
                             orderby i.Orden
                             select new { Item = i, v.Placa, v.Vin, v.IdentificadorNorm })
            .Skip((numero - 1) * TamanoPagina).Take(TamanoPagina).ToListAsync(ct).ConfigureAwait(false);
        var o = opciones.Value;
        var resumen = Resumen(operacion);
        return new DetalleOperacionDto(
            resumen.OperacionId, resumen.Tipo, resumen.Estado, resumen.TipoTramiteCodigo, resumen.Total, resumen.Creadas, resumen.Omitidas, resumen.Fallidas,
            resumen.Pendientes, resumen.CreadaEn, resumen.TerminadaEn,
            [.. cerrados.OrderByDescending(c => c.Cantidad).ThenBy(c => c.MotivoCodigo, StringComparer.Ordinal)
                .Select(c => new MotivoOperacionDto(CodigosDeEnum.Codigo(c.Estado), c.MotivoCodigo!, MotivoDerivacion.Mensaje(c.MotivoCodigo!), c.Cantidad))],
            numero, TamanoPagina, totalItems,
            [.. pagina1.Select(x => new ItemOperacionDto(
                x.Item.VehiculoId, x.Item.Orden, x.Placa ?? x.Vin ?? x.IdentificadorNorm, CodigosDeEnum.Codigo(x.Item.Estado), x.Item.MotivoCodigo,
                x.Item.MotivoCodigo is { } m ? MotivoDerivacion.Mensaje(m) : null, x.Item.TramiteId, x.Item.TramiteNumero,
                x.Item.TramiteId is { } t ? o.UrlTramite(t, cabecera.TenantId) : null))]);
    }

    public async Task<IResult> ReintentarFallidasAsync(Guid id, Guid operacionId, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        try
        {
            var resultado = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var operacion = await db.Set<OperacionDerivacion>().SingleOrDefaultAsync(o => o.Id == operacionId && o.DiagnosticoId == id, ct).ConfigureAwait(false)
                    ?? throw ReglaException.NoEncontrado("La operación");
                if (await db.Set<ItemDerivacion>().AnyAsync(i => i.OperacionId == operacionId && (i.Estado == EstadoItemDerivacion.Pendiente || i.Estado == EstadoItemDerivacion.Enviado), ct).ConfigureAwait(false))
                    throw ReglaException.Conflicto(CodigoOperacionEnCurso, "La operación todavía tiene vehículos en curso; espera a que termine para reintentar las fallidas.");

                var fallidos = await db.Set<ItemDerivacion>().Where(i => i.OperacionId == operacionId && i.Estado == EstadoItemDerivacion.Fallido).OrderBy(i => i.Orden).ToListAsync(ct).ConfigureAwait(false);
                if (fallidos.Count == 0)
                    return new ReintentoFallidasDto(0, 0);

                var elegibilidad = await LecturaElegibilidad.LeerAsync(db, id, cabecera.Modo, [.. fallidos.Select(i => i.VehiculoId)], ct).ConfigureAwait(false);
                var ahora = reloj.GetUtcNow();
                int reencolados = 0, omitidas = 0;
                foreach (var item in fallidos)
                {
                    var motivo = elegibilidad.TryGetValue(item.VehiculoId, out var e) ? e.Motivo : MotivoDerivacion.NoTerminado;
                    operacion.Fallidas--;
                    if (motivo is null)
                    {
                        item.Reabrir(usuario.Id, ahora);
                        reencolados++;
                    }
                    else
                    {
                        item.Estado = EstadoItemDerivacion.Omitido;
                        item.MotivoCodigo = motivo;
                        item.ActualizadoEn = ahora;
                        operacion.Omitidas++;
                        omitidas++;
                    }
                }

                if (reencolados > 0)
                {
                    operacion.Estado = EstadoOperacionDerivacion.EnCurso;
                    operacion.TerminadaEn = null;
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await despachador.LiberarAsync(cabecera.TenantId, ct).ConfigureAwait(false);
                await transaccion.CommitAsync(ct).ConfigureAwait(false);
                return new ReintentoFallidasDto(reencolados, omitidas);
            }).ConfigureAwait(false);
            return Results.Accepted(value: resultado);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ReglaException.Conflicto(CodigoOperacionEnCurso, "Otro reintento de esta operación está en curso.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ItemDerivacionConfig.IndiceVivo })
        {
            throw ReglaException.Conflicto(CodigoOperacionEnCurso, "Algún vehículo ya tiene otro trámite en curso; vuelve a intentarlo.");
        }
    }

    private static ResumenOperacionDto Resumen(OperacionDerivacion o) => new(
        o.Id, CodigosDeEnum.Codigo(o.Tipo), CodigosDeEnum.Codigo(o.Estado), o.TipoTramiteCodigo, o.Total, o.Creadas, o.Omitidas, o.Fallidas, o.Pendientes, o.CreadaEn, o.TerminadaEn);
}
