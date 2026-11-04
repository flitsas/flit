using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Radicados;

public sealed record MacroEstadoDto(string Codigo, string Etiqueta, int Indice);

/// <summary>Paso del tracking: <c>estado</c> es done, current, rejected o pending; la fecha solo viaja si el paso ya ocurrió.</summary>
public sealed record PasoTrackingDto(string Clave, string Titulo, string Estado, DateTimeOffset? Fecha);

public sealed record SubsanacionDto(string? Motivo, bool AccionCorregir);

/// <summary>
/// Respuesta de la consulta de radicado. Dato mínimo (§8): nunca documentos, correos, teléfonos, nombres ni la cédula de las partes.
/// <c>SoloLectura</c> es verdadero cuando quien consulta es una parte que no es el solicitante principal.
/// </summary>
public sealed record RadicadoConsultaResponse(
    string Radicado, string? NumeroReferencia, string? Placa, string TipoTramite, string Organismo,
    MacroEstadoDto MacroEstado, string Mensaje, IReadOnlyList<PasoTrackingDto> Pasos, SubsanacionDto? Subsanacion, bool SoloLectura);

public abstract record ResultadoConsultaRadicado
{
    public sealed record Exito(RadicadoConsultaResponse Radicado) : ResultadoConsultaRadicado;

    /// <summary>No existe el trámite o la combinación placa + radicado/cédula no coincide: nunca se distingue cuál.</summary>
    public sealed record NoEncontrado : ResultadoConsultaRadicado;

    public sealed record Bloqueado(TimeSpan EsperarHasta) : ResultadoConsultaRadicado;
}

/// <summary>
/// Consulta pública del estado de un trámite (HU #14042 / diseño UI): placa + radicado, o placa + cédula.
/// El radicado es el código del portal o el número de FLIT 2. Sin crear sesión.
/// </summary>
public sealed class RadicadoConsultaService(
    PersonaNaturalDb db, PiiHasher hasher, BloqueoPorRadicado bloqueo, IEstadoTramiteFuente fuente)
{
    public const string AccionConsulta = "consulta_radicado";

    private static readonly string[] EstadosEnRevision = ["preasignacion", "asignado", "entregado"];
    private static readonly string[] EstadosFinales = ["aprobado", "rechazado", "anulado", "revocado"];

    public Task<ResultadoConsultaRadicado> ConsultarAsync(string? radicado, string? cedula, string placa, byte[]? ipHash, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(radicado))
            return ConsultarPorRadicadoAsync(radicado, cedula, placa, ipHash, ct);
        return ConsultarPorPlacaYCedulaAsync(cedula!, placa, ipHash, ct);
    }

    private async Task<ResultadoConsultaRadicado> ConsultarPorRadicadoAsync(string radicado, string? cedula, string placa, byte[]? ipHash, CancellationToken ct)
    {
        var clave = PiiHasher.ClaveRadicado(radicado);
        if (await bloqueo.ReservarIntentoAsync(clave, ipHash, ct).ConfigureAwait(false) is { } espera)
            return new ResultadoConsultaRadicado.Bloqueado(espera);

        var codigo = PiiHasher.NormalizarRadicado(radicado);
        var placaNorm = PiiHasher.NormalizarDocumento(placa);
#pragma warning disable CA1862 // EF no traduce string.Equals con StringComparison; ToUpper() sí (upper(columna) = parámetro).
        var solicitud = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.Estado != EstadoSolicitud.Cancelada && (s.CodigoPublico == codigo || (s.ReferenceNumber != null && s.ReferenceNumber.ToUpper() == codigo)))
            .OrderBy(s => s.CodigoPublico == codigo ? 0 : 1)
            .Select(s => new SolicitudConsulta(
                s.Id, s.CodigoPublico, s.ReferenceNumber, s.Placa, s.TipoTramiteCodigo, s.OrganismoNombre, s.Estado, s.CedulaSolicitanteHash,
                s.ProcedureInstanceId, s.EstadoTramite, s.SubsanacionActiva, s.SubsanacionMotivo, s.CreatedAt))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
#pragma warning restore CA1862

        var placaCoincide = solicitud?.Placa is not null
            && PiiHasher.NormalizarDocumento(solicitud.Placa) == placaNorm;

        // Con cédula: misma verificación de identidad de siempre. Sin cédula (UI: placa + radicado): basta la placa.
        var esPrincipal = false;
        var esParte = false;
        if (!string.IsNullOrWhiteSpace(cedula))
        {
            var huella = hasher.HashCedula(cedula);
            var principalEsperada = solicitud?.CedulaSolicitanteHash is { Length: 32 } h ? h : new byte[32];
            esPrincipal = PiiHasher.Iguales(huella, principalEsperada) & solicitud is not null;
            var solicitudId = solicitud?.Id ?? Guid.Empty;
            var huellasPartes = await db.Set<PersonaSolicitante>().AsNoTracking()
                .Where(p => p.SolicitudId == solicitudId).Select(p => p.DocumentoHash).ToListAsync(ct).ConfigureAwait(false);
            foreach (var parte in huellasPartes)
                esParte |= PiiHasher.Iguales(huella, parte is { Length: 32 } ? parte : new byte[32]);
        }

        var identidadOk = string.IsNullOrWhiteSpace(cedula) || (esPrincipal | esParte);
        if (solicitud is null || !placaCoincide || !identidadOk)
            return new ResultadoConsultaRadicado.NoEncontrado();

        // Sin cédula no afirmamos que sea el solicitante: solo lectura.
        var soloLectura = string.IsNullOrWhiteSpace(cedula) || !esPrincipal;
        return await EmitirExitoAsync(solicitud, clave, ipHash, soloLectura, ct).ConfigureAwait(false);
    }

    private async Task<ResultadoConsultaRadicado> ConsultarPorPlacaYCedulaAsync(string cedula, string placa, byte[]? ipHash, CancellationToken ct)
    {
        var placaNorm = PiiHasher.NormalizarDocumento(placa);
        var huella = hasher.HashCedula(cedula);
        var clave = PiiHasher.ClaveConsultaPlacaCedula(placaNorm, huella);
        if (await bloqueo.ReservarIntentoAsync(clave, ipHash, ct).ConfigureAwait(false) is { } espera)
            return new ResultadoConsultaRadicado.Bloqueado(espera);

#pragma warning disable CA1862
        var candidatas = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.Estado != EstadoSolicitud.Cancelada && s.Placa != null && s.Placa.ToUpper() == placaNorm)
            .OrderByDescending(s => s.CreatedAt)
            .Take(20)
            .Select(s => new SolicitudConsulta(
                s.Id, s.CodigoPublico, s.ReferenceNumber, s.Placa, s.TipoTramiteCodigo, s.OrganismoNombre, s.Estado, s.CedulaSolicitanteHash,
                s.ProcedureInstanceId, s.EstadoTramite, s.SubsanacionActiva, s.SubsanacionMotivo, s.CreatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
#pragma warning restore CA1862

        SolicitudConsulta? elegida = null;
        var esPrincipal = false;
        foreach (var candidata in candidatas)
        {
            var principalOk = candidata.CedulaSolicitanteHash is { Length: 32 }
                && PiiHasher.Iguales(huella, candidata.CedulaSolicitanteHash);
            if (principalOk)
            {
                elegida = candidata;
                esPrincipal = true;
                break;
            }

            var huellasPartes = await db.Set<PersonaSolicitante>().AsNoTracking()
                .Where(p => p.SolicitudId == candidata.Id).Select(p => p.DocumentoHash).ToListAsync(ct).ConfigureAwait(false);
            var hayParte = false;
            foreach (var parte in huellasPartes)
                hayParte |= PiiHasher.Iguales(huella, parte is { Length: 32 } ? parte : new byte[32]);
            if (hayParte)
            {
                elegida = candidata;
                esPrincipal = false;
                break;
            }
        }

        // Comparación constante con relleno si no hubo match, para no filtrar por tiempo.
        if (elegida is null)
        {
            _ = PiiHasher.Iguales(huella, new byte[32]);
            return new ResultadoConsultaRadicado.NoEncontrado();
        }

        return await EmitirExitoAsync(elegida, clave, ipHash, soloLectura: !esPrincipal, ct).ConfigureAwait(false);
    }

    private async Task<ResultadoConsultaRadicado> EmitirExitoAsync(
        SolicitudConsulta solicitud, byte[] clave, byte[]? ipHash, bool soloLectura, CancellationToken ct)
    {
        await bloqueo.LiberarAsync(clave, ipHash, ct).ConfigureAwait(false);
        var externo = await fuente.ObtenerAsync(
            new SolicitudParaEstado(solicitud.Id, solicitud.ProcedureInstanceId, solicitud.ReferenceNumber, solicitud.EstadoTramite, solicitud.SubsanacionActiva, solicitud.SubsanacionMotivo), ct)
            .ConfigureAwait(false);
        var historial = await db.Set<HistorialEstado>().AsNoTracking().Where(x => x.SolicitudId == solicitud.Id)
            .OrderBy(x => x.OcurrioEn).Select(x => new { x.Hasta, x.OcurrioEn }).ToListAsync(ct).ConfigureAwait(false);

        var macro = MacroEstados.Resolver(solicitud.Estado, externo.EstadoTramite);
        var pasos = ArmarPasos(solicitud.Estado, externo.EstadoTramite, solicitud.CreatedAt, historial.ConvertAll(x => (x.Hasta, x.OcurrioEn)));
        var subsanacion = macro.Macro == MacroEstado.Subsanacion ? new SubsanacionDto(externo.SubsanacionMotivo, externo.SubsanacionActiva) : null;

        db.Add(new AccesoPiiLog { Actor = "consulta_publica", Accion = AccionConsulta, SolicitudId = solicitud.Id, IpHash = ipHash });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ResultadoConsultaRadicado.Exito(new RadicadoConsultaResponse(
            solicitud.CodigoPublico, solicitud.ReferenceNumber, solicitud.Placa, solicitud.TipoTramiteCodigo, solicitud.OrganismoNombre,
            new MacroEstadoDto(macro.Codigo, macro.Etiqueta, (int)macro.Macro), macro.Mensaje, pasos, subsanacion, SoloLectura: soloLectura));
    }

    /// <summary>
    /// Cinco pasos como en el tracking del front. Una posición «actual»: lo anterior está hecho y lo posterior pendiente; un
    /// trámite aprobado los completa todos; uno rechazado, anulado o revocado cierra el último en rojo.
    /// </summary>
    internal static IReadOnlyList<PasoTrackingDto> ArmarPasos(
        string estado, string? estadoTramite, DateTimeOffset creadoEn, IReadOnlyList<(string Hasta, DateTimeOffset OcurrioEn)> historial)
    {
        var tramite = estadoTramite?.Trim().ToLowerInvariant();
        var actual = 0;
        var rechazadoEn = -1;
        var todoListo = false;
        if (tramite is "aprobado")
            todoListo = true;
        else if (tramite is "rechazado" or "anulado" or "revocado")
        {
            actual = 4;
            rechazadoEn = 4;
        }
        else if (tramite is not null && EstadosEnRevision.Contains(tramite))
            actual = 3;
        else if (tramite is "borrador" or "preparado" || estado is EstadoSolicitud.Creada or EstadoSolicitud.EnCola)
            actual = 2;
        else if (estado == EstadoSolicitud.RechazadaCreacion)
        {
            actual = 2;
            rechazadoEn = 2;
        }
        else if (estado == EstadoSolicitud.PendienteValidacion)
            actual = 1;

        DateTimeOffset? Primera(params string[] estados) => historial.Where(x => estados.Contains(x.Hasta)).Select(x => (DateTimeOffset?)x.OcurrioEn).FirstOrDefault();
        DateTimeOffset? Ultima(params string[] estados) => historial.Where(x => estados.Contains(x.Hasta)).Select(x => (DateTimeOffset?)x.OcurrioEn).LastOrDefault();

        var tituloFinal = tramite switch
        {
            "aprobado" => "Aprobado",
            "rechazado" => "Rechazado",
            "anulado" => "Anulado",
            "revocado" => "Revocado",
            _ => "Aprobado / Rechazado",
        };
        (string Clave, string Titulo, DateTimeOffset? Fecha)[] definicion =
        [
            ("borrador", "Borrador del trámite", creadoEn),
            ("validacion_identidad", "Validación de identidad", Primera(EstadoSolicitud.EnCola)),
            ("enviado_ot", "Enviado al OT", Primera(EstadoSolicitud.Creada)),
            ("en_revision", "En revisión", Primera(EstadosEnRevision)),
            ("resolucion", tituloFinal, Ultima(EstadosFinales)),
        ];

        var pasos = new List<PasoTrackingDto>(definicion.Length);
        for (var i = 0; i < definicion.Length; i++)
        {
            var estadoPaso = i == rechazadoEn ? "rejected"
                : todoListo || i < actual ? "done"
                : i == actual ? "current"
                : "pending";
            pasos.Add(new PasoTrackingDto(definicion[i].Clave, definicion[i].Titulo, estadoPaso, estadoPaso == "pending" ? null : definicion[i].Fecha));
        }

        return pasos;
    }

    private sealed record SolicitudConsulta(
        Guid Id, string CodigoPublico, string? ReferenceNumber, string? Placa, string TipoTramiteCodigo, string OrganismoNombre,
        string Estado, byte[]? CedulaSolicitanteHash, Guid? ProcedureInstanceId, string? EstadoTramite, bool SubsanacionActiva,
        string? SubsanacionMotivo, DateTimeOffset CreatedAt);
}
