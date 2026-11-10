using System.Globalization;
using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Lista de cobros en caja de un organismo y un día de Colombia (HU #14459 para la caja; el hub del OT la usa por gRPC, HU #14461).
/// Solo cobros del organismo pedido. Los anulados salen marcados y NO suman al total del día. Un cobro es anulable mientras la
/// solicitud siga <c>pagada</c> y la ventana de <see cref="CajaOptions.VentanaAnulacionMinutos"/> no haya vencido.
/// </summary>
public sealed class CajaListaService(PersonaNaturalDb db, IOptions<CajaOptions> opciones, TimeProvider reloj)
{
    public const int TamanoMax = 50;
    public const int TamanoPorDefecto = 10;
    public const string MotivoYaAnulado = "El cobro ya fue anulado.";
    public const string MotivoYaEnFlit2 = "El trámite ya está en FLIT 2.0 y su cobro no se puede anular.";

    public static (int Pagina, int Tamano) Paginar(int? pagina, int? tamano) =>
        (Math.Max(1, pagina ?? 1), Math.Clamp(tamano is null or <= 0 ? TamanoPorDefecto : tamano.Value, 1, TamanoMax));

    public async Task<PaginaDeCobros> ListarAsync(Guid organismoId, DateOnly fecha, int pagina, int tamano, CancellationToken ct)
    {
        (pagina, tamano) = Paginar(pagina, tamano);
        var ahora = reloj.GetUtcNow();
        var ventana = opciones.Value.Ventana;

        var baseQuery = from c in db.Set<CobroSolicitud>().AsNoTracking()
                        join s in db.Set<SolicitudTramite>().AsNoTracking() on c.SolicitudId equals s.Id
                        where c.OrganismoId == organismoId && c.PagadoEnDia == fecha && c.Medio == MedioDePago.Caja
                              && (c.Estado == EstadoCobro.Pagado || c.Estado == EstadoCobro.Anulado)
                        select new { Cobro = c, EstadoSolicitud = s.Estado };

        var total = await baseQuery.CountAsync(ct).ConfigureAwait(false);
        var totalDia = await baseQuery.Where(x => x.Cobro.Estado == EstadoCobro.Pagado).SumAsync(x => x.Cobro.ValorCop ?? 0, ct).ConfigureAwait(false);
        var filas = await baseQuery.OrderByDescending(x => x.Cobro.PagadoEn).ThenBy(x => x.Cobro.Id).Skip((pagina - 1) * tamano).Take(tamano).ToListAsync(ct).ConfigureAwait(false);

        var items = filas.Select(x =>
        {
            var c = x.Cobro;
            var (anulable, motivo) = Evaluar(c.Estado, x.EstadoSolicitud, c.PagadoEn, ahora, ventana);
            return new CobroDelOrganismo(
                c.Id, c.Codigo, c.ValorCop ?? 0, c.MedioCaja, c.MedioDetalle, c.PagadoEn ?? c.UpdatedAt, c.PagadoPorNombre, c.Estado,
                anulable, motivo, c.AnuladoEn, c.AnuladoPorNombre, c.AnulacionMotivo, c.RowVersion);
        }).ToList();
        return new PaginaDeCobros(items, total, totalDia);
    }

    /// <summary>¿Se puede anular este cobro ahora? Devuelve el motivo en español cuando no.</summary>
    public static (bool Anulable, string? Motivo) Evaluar(string estadoCobro, string estadoSolicitud, DateTimeOffset? pagadoEn, DateTimeOffset ahora, TimeSpan ventana)
    {
        if (estadoCobro == EstadoCobro.Anulado)
            return (false, MotivoYaAnulado);
        if (estadoCobro != EstadoCobro.Pagado)
            return (false, MotivoYaEnFlit2);
        var abierta = estadoSolicitud == EstadoSolicitud.Pagada && RadicacionDePagadasService.EnVentana(MedioDePago.Caja, pagadoEn, ahora, ventana);
        return abierta ? (true, null) : (false, MotivoYaEnFlit2);
    }

    /// <summary>La lista del día de hoy en Colombia para la caja (HU #14459).</summary>
    public async Task<CobrosDelDia> HoyAsync(Cajero cajero, int? pagina, int? tamano, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cajero);
        var (p, t) = Paginar(pagina, tamano);
        var fecha = VigenciaDelCobro.DiaColombia(reloj.GetUtcNow());
        var lista = await ListarAsync(cajero.OrganismoId, fecha, p, t, ct).ConfigureAwait(false);
        var colombia = TimeSpan.FromHours(-5);
        var items = lista.Items.Select(i => new ItemCobroDelDia(
            i.Codigo, i.ValorCop, i.MedioCaja ?? string.Empty, i.MedioDetalle, i.PagadoEn.ToOffset(colombia).ToString("HH:mm", CultureInfo.InvariantCulture), i.PagadoEn,
            i.PagadoPorNombre ?? string.Empty, i.Estado)).ToList();
        return new CobrosDelDia(fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), lista.TotalDiaCop, items, lista.Total, p, t);
    }
}
