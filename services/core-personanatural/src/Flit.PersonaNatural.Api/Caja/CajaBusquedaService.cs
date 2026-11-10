using System.Text.RegularExpressions;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Buscar el trámite por su ID en la caja del organismo (HU #14456). Responde SOLO con lo de su organismo y nunca con nombre,
/// documento ni placa de las partes. Los códigos: <c>no_encontrado</c>, <c>otro_organismo</c> (sin ningún dato), <c>ya_pagado</c>,
/// <c>cobro_vencido</c> y <c>no_habilitado</c>. Las búsquedas sin resultado cuentan contra un tope por usuario.
/// </summary>
public sealed partial class CajaBusquedaService(
    PersonaNaturalDb db, CotizadorDelPunto cotizador, CajaTopeBusquedas tope, CajaAuditoria auditoria, TimeProvider reloj)
{
    public const string CodigoNoEncontrado = "no_encontrado";
    public const string CodigoOtroOrganismo = "otro_organismo";
    public const string CodigoYaPagado = "ya_pagado";
    public const string CodigoCobroVencido = "cobro_vencido";
    public const string CodigoNoHabilitado = "no_habilitado";
    public const string CodigoDemasiadosIntentos = "demasiados_intentos";

    [GeneratedRegex(@"^PN-\d{4}-\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoCodigo();

    /// <summary>El código como lo guarda la base (mayúsculas, sin espacios) o <c>null</c> si no tiene la forma <c>PN-AAAA-NNNNNN</c>.</summary>
    public static string? Normalizar(string? codigo)
    {
        var limpio = (codigo ?? string.Empty).Trim().ToUpperInvariant();
        return FormatoCodigo().IsMatch(limpio) ? limpio : null;
    }

    public async Task<TramiteParaCobrar> BuscarAsync(Cajero cajero, string? codigoCrudo, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cajero);
        if (await tope.EsperaAsync(cajero.UsuarioId, ct).ConfigureAwait(false) is { } espera)
        {
            throw new ReglaDeNegocioException(
                StatusCodes.Status429TooManyRequests, CodigoDemasiadosIntentos, "Demasiados intentos", "Demasiadas búsquedas sin resultado. Espere unos minutos.",
                new Dictionary<string, object?> { ["retryAfterSegundos"] = (int)Math.Ceiling(espera.TotalSeconds) });
        }

        var codigo = Normalizar(codigoCrudo);
        var s = codigo is null
            ? null
            : await db.Set<SolicitudTramite>().AsNoTracking().Where(x => x.CodigoPublico == codigo)
                .Select(x => new { x.Id, x.Estado, x.Datos, x.OrganismoId, x.TipoTramiteCodigo, x.DefinicionSnapshot }).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // Una solicitud de la web (no del punto) no tiene cobro en caja: para la caja no existe.
        if (s is null || !SolicitudesService.EsCanalPunto(s.Datos))
            throw await FallidaAsync(cajero, codigo, 404, CodigoNoEncontrado, "No encontramos ese trámite.", ip, ct).ConfigureAwait(false);
        if (s.OrganismoId != cajero.OrganismoId)
            throw await FallidaAsync(cajero, codigo, 403, CodigoOtroOrganismo, "Este trámite no es de este organismo.", ip, ct).ConfigureAwait(false);

        var cobros = await db.Set<CobroSolicitud>().AsNoTracking().Where(c => c.SolicitudId == s.Id && c.Estado != EstadoCobro.Anulado).ToListAsync(ct).ConfigureAwait(false);
        if (cobros.Any(c => c.Estado == EstadoCobro.Pagado) || s.Estado is EstadoSolicitud.Pagada or EstadoSolicitud.EnCola or EstadoSolicitud.Creada)
            throw await ResueltaAsync(cajero, codigo!, 409, CodigoYaPagado, "Este trámite ya fue pagado.", ip, ct).ConfigureAwait(false);

        var habilitado = cobros.FirstOrDefault(c => c.Estado == EstadoCobro.Habilitado);
        if (s.Estado != EstadoSolicitud.PendientePago || habilitado is null)
            throw await ResueltaAsync(cajero, codigo!, 409, CodigoNoHabilitado, "Este trámite no tiene el pago en caja habilitado.", ip, ct).ConfigureAwait(false);
        if (!VigenciaDelCobro.EstaVigente(habilitado.VenceEn, reloj.GetUtcNow()))
            throw await ResueltaAsync(cajero, codigo!, 409, CodigoCobroVencido, "El pago en caja de este trámite venció.", ip, ct).ConfigureAwait(false);

        // El desglose y el total se calculan AHORA con el medio PIN y el ámbito del organismo (la misma cotización que cobra la confirmación).
        CotizacionDto cotizacion;
        try
        {
            cotizacion = await cotizador.CotizarAsync(s.TipoTramiteCodigo, s.OrganismoId, ct).ConfigureAwait(false);
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo == CotizadorDelPunto.CodigoMedioNoDisponible)
        {
            throw await ResueltaAsync(cajero, codigo!, 409, CodigoNoHabilitado, "Este trámite no tiene el pago en caja habilitado.", ip, ct).ConfigureAwait(false);
        }

        await auditoria.RegistrarAsync(cajero, EventoCaja.AccionBusquedaOk, codigo, "ok", ip, ct).ConfigureAwait(false);
        var nombreTipo = DefinicionesService.Leer(s.DefinicionSnapshot)?.Name is { Length: > 0 } n ? n : s.TipoTramiteCodigo;
        return new TramiteParaCobrar(
            codigo!, new CodigoNombre(s.TipoTramiteCodigo, nombreTipo), new CodigoNombre(cajero.OrganismoCodigo, cajero.OrganismoNombre),
            [.. cotizacion.Lineas.Select(l => new LineaCaja(l.Nombre, l.ValorCop))], cotizacion.TotalCop, cotizacion.VersionTarifa, habilitado.VenceEn!.Value);
    }

    /// <summary>Sin resultado: cuenta contra el tope. No devuelve ningún campo del trámite.</summary>
    private async Task<ReglaDeNegocioException> FallidaAsync(Cajero cajero, string? codigo, int http, string code, string mensaje, string? ip, CancellationToken ct)
    {
        await auditoria.RegistrarAsync(cajero, EventoCaja.AccionBusquedaFallida, codigo, code, ip, ct).ConfigureAwait(false);
        return new ReglaDeNegocioException(http, code, "Búsqueda sin resultado", mensaje);
    }

    /// <summary>El trámite es de este organismo pero no se puede cobrar: es una respuesta, no un intento a ciegas.</summary>
    private async Task<ReglaDeNegocioException> ResueltaAsync(Cajero cajero, string codigo, int http, string code, string mensaje, string? ip, CancellationToken ct)
    {
        await auditoria.RegistrarAsync(cajero, EventoCaja.AccionBusquedaOk, codigo, code, ip, ct).ConfigureAwait(false);
        return new ReglaDeNegocioException(http, code, "No se puede cobrar", mensaje);
    }
}
