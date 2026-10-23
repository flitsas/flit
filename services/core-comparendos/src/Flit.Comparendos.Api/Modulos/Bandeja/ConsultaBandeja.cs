using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plazos;
using Flit.Comparendos.Domain.Tiempo;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Bandeja;

/// <summary>Filtros de la bandeja (§7.1). Todos opcionales; se combinan con AND.</summary>
public sealed record FiltrosBandeja
{
    public Guid? Empresa { get; init; }
    public Guid[]? Nits { get; init; }

    /// <summary>HU #13870: busca el texto dentro del número o de la placa a la vez.</summary>
    public string? Buscar { get; init; }
    public string? Placa { get; init; }
    public string? Numero { get; init; }

    /// <summary>HU #13868: listas pegadas desde Excel; coinciden completas, no por parte.</summary>
    public string[]? Placas { get; init; }
    public string[]? Numeros { get; init; }
    public Guid[]? Organismos { get; init; }
    public DateOnly? ImposicionDesde { get; init; }
    public DateOnly? ImposicionHasta { get; init; }
    public DateOnly? NotificacionDesde { get; init; }
    public DateOnly? NotificacionHasta { get; init; }
    public DateOnly? RegistroDesde { get; init; }
    public DateOnly? RegistroHasta { get; init; }
    public string[]? EstadoLegal { get; init; }
    public string[]? EstadoProceso { get; init; }
    public string[]? UltimoAviso { get; init; }
    public string[]? CambioContraventor { get; init; }
    public string[]? Fuente { get; init; }
    public bool? ConContraventor { get; init; }
    public bool? Pagado { get; init; }
    public bool? ConDp { get; init; }
    public string? ResultadoDp { get; init; }

    /// <summary>activos (por defecto), inactivos o todos.</summary>
    public string? Activos { get; init; }
    public bool? CobroCoactivo { get; init; }
    public bool? SinGestion { get; init; }

    /// <summary>ProximosAVencer, SinContraventor o RechazadosOt (§6.11); Radar es el del dashboard (S-56), al que lleva su KPI.</summary>
    public string? Rapido { get; init; }

    /// <summary>CaducidadInmediata, NoImpugnables o C038 (§6.11; C-321 y pico y placa siguen sin definición, S-57).</summary>
    public string? Juridico { get; init; }

    /// <summary>HU #13868: los filtros de lista que se piden con «no es» (nits, placas, numeros, organismos, estadoProceso, ultimoAviso, cambioContraventor, fuente).</summary>
    public string[]? Negar { get; init; }
    public string? Orden { get; init; }
    public bool Descendente { get; init; }
    public int? Pagina { get; init; }
    public int? Tamano { get; init; }

    public static FiltrosBandeja Desde(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var q = request.Query;
        string? S(string k) => q.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;
        string[]? L(string k) => q.TryGetValue(k, out var v) && v.Count > 0 ? v.SelectMany(x => (x ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray() : null;
        Guid[]? G(string k) => L(k)?.Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();
        bool? B(string k) => bool.TryParse(S(k), out var b) ? b : null;
        DateOnly? D(string k) => DateOnly.TryParse(S(k), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        int? I(string k) => int.TryParse(S(k), out var i) ? i : null;
        return new FiltrosBandeja
        {
            Empresa = Guid.TryParse(S("empresa"), out var e) ? e : null,
            Nits = G("nits"),
            Buscar = S("buscar"),
            Placa = S("placa"),
            Numero = S("numero"),
            Placas = L("placas"),
            Numeros = L("numeros"),
            Organismos = G("organismos"),
            ImposicionDesde = D("imposicionDesde"),
            ImposicionHasta = D("imposicionHasta"),
            NotificacionDesde = D("notificacionDesde"),
            NotificacionHasta = D("notificacionHasta"),
            RegistroDesde = D("registroDesde"),
            RegistroHasta = D("registroHasta"),
            EstadoLegal = L("estadoLegal"),
            EstadoProceso = L("estadoProceso"),
            UltimoAviso = L("ultimoAviso"),
            CambioContraventor = L("cambioContraventor"),
            Fuente = L("fuente"),
            ConContraventor = B("conContraventor"),
            Pagado = B("pagado"),
            ConDp = B("conDp"),
            ResultadoDp = S("resultadoDp"),
            Activos = S("activos"),
            CobroCoactivo = B("cobroCoactivo"),
            SinGestion = B("sinGestion"),
            Rapido = S("rapido"),
            Juridico = S("juridico"),
            Negar = L("negar"),
            Orden = S("orden"),
            Descendente = B("descendente") ?? false,
            Pagina = I("pagina"),
            Tamano = I("tamano"),
        };
    }
}

/// <summary>Una fila de la bandeja con lo calculado al consultar (estado legal, días restantes, ventana de descuento).</summary>
public sealed record FilaBandeja(
    Guid Id,
    string Numero,
    string? Placa,
    string? NitConsultado,
    string? Titular,
    Guid? OrganismoTransitoId,
    string? Organismo,
    string? SecretariaTexto,
    DateOnly? FechaImposicion,
    DateOnly? FechaNotificacion,
    decimal? ValorTotal,
    string Fuente,
    string? FuentePrincipal,
    string EstadoLegal,
    int? DiasHabilesRestantes,
    bool ProximoAVencer,
    string VentanaDescuento,
    DateOnly? FinDescuentoVigente,
    string EstadoProceso,
    string EstadoGestion,
    string? ContraventorNombre,
    string EstadoConsultaContraventor,
    string UltimoAviso,
    string UltimoHito,
    string CambioContraventor,
    string? DpRadicado,
    string? DpEstado,
    string? DpResultado,
    bool Pagado,
    bool Activo,
    string? MotivoInactivacion,
    bool EnCobroCoactivo,
    bool EsFotomulta,
    DateTimeOffset UltimoEventoEn,
    DateTimeOffset CreadoEn);

/// <summary>La bandeja unificada (HU #13496): misma consulta para todos los roles; los permisos deciden las acciones.</summary>
public sealed class ConsultaBandeja(ComparendosDb db, CalendarioService calendario, Reloj reloj)
{
    public sealed record Hoy(DateOnly Dia, DateOnly Mas2Habiles, DateOnly Mas7Habiles, MotorDePlazos Motor);

    public async Task<Hoy> HoyAsync(CancellationToken ct)
    {
        var cal = await calendario.ActualAsync(ct).ConfigureAwait(false);
        var hoy = reloj.Hoy;
        return new Hoy(hoy, cal.SumarHabiles(hoy, MotorDePlazos.DiasProximoAVencer), cal.SumarHabiles(hoy, 7), new MotorDePlazos(cal));
    }

    public IQueryable<Comparendo> Filtrar(Guid? tenant, FiltrosBandeja f, Hoy h)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(h);
        var hoy = h.Dia;
        var q = db.Comparendos.AsNoTracking();
        if (tenant is { } t)
            q = q.Where(c => c.TenantId == t);

        q = (f.Activos ?? "activos") switch
        {
            "inactivos" => q.Where(c => !c.Activo),
            "todos" => q,
            _ => q.Where(c => c.Activo),
        };
        // «no es» (HU #13868): el mismo criterio negado; lo que no tiene el dato (sin organismo, sin NIT) sí entra.
        var negados = new HashSet<string>(f.Negar ?? [], StringComparer.OrdinalIgnoreCase);
        IQueryable<Comparendo> Donde(IQueryable<Comparendo> origen, string campo, Expression<Func<Comparendo, bool>> criterio) =>
            origen.Where(negados.Contains(campo) ? Expression.Lambda<Func<Comparendo, bool>>(Expression.Not(criterio.Body), criterio.Parameters) : criterio);

        if (f.Nits is { Length: > 0 } nits)
            q = Donde(q, "nits", c => c.IdentificacionMonitoreadaId != null && nits.Contains(c.IdentificacionMonitoreadaId.Value));
        if (NumeroComparendo.Normalizar(f.Buscar) is { } buscar)
            q = q.Where(c => c.NumeroNormalizado.Contains(buscar) || (c.Placa != null && c.Placa.Contains(buscar)));
        if (!string.IsNullOrWhiteSpace(f.Placa))
        {
            var placa = new string(f.Placa.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
            q = q.Where(c => c.Placa != null && c.Placa.Contains(placa));
        }

        if (NumeroComparendo.Normalizar(f.Numero) is { } numero)
            q = q.Where(c => c.NumeroNormalizado.Contains(numero));
        if (Normalizados(f.Placas) is { Count: > 0 } placas)
            q = Donde(q, "placas", c => c.Placa != null && placas.Contains(c.Placa));
        if (Normalizados(f.Numeros) is { Count: > 0 } numeros)
            q = Donde(q, "numeros", c => numeros.Contains(c.NumeroNormalizado));
        if (f.Organismos is { Length: > 0 } ots)
            q = Donde(q, "organismos", c => c.OrganismoTransitoId != null && ots.Contains(c.OrganismoTransitoId.Value));
        if (f.ImposicionDesde is { } id)
            q = q.Where(c => c.FechaImposicion >= id);
        if (f.ImposicionHasta is { } ih)
            q = q.Where(c => c.FechaImposicion <= ih);
        if (f.NotificacionDesde is { } nd)
            q = q.Where(c => c.FechaNotificacion >= nd);
        if (f.NotificacionHasta is { } nh)
            q = q.Where(c => c.FechaNotificacion <= nh);
        if (f.RegistroDesde is { } rd)
        {
            var desde = new DateTimeOffset(rd.ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime();
            q = q.Where(c => c.CreadoEn >= desde);
        }

        if (f.RegistroHasta is { } rh)
        {
            var hasta = new DateTimeOffset(rh.AddDays(1).ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime();
            q = q.Where(c => c.CreadoEn < hasta);
        }

        if (f.EstadoLegal is { Length: > 0 } legales)
        {
            var sin = legales.Contains("SinNotificar");
            var enTiempo = legales.Contains("EnTiempo");
            var vencido = legales.Contains("Vencido");
            var caducado = legales.Contains("Caducado");
            var prescrito = legales.Contains("Prescrito");
            q = q.Where(c =>
                (sin && c.FechaLimiteGestion == null)
                || (enTiempo && c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion)
                || (vencido && c.FechaLimiteGestion != null && hoy > c.FechaLimiteGestion && hoy <= c.FechaCaducidad)
                || (caducado && hoy > c.FechaCaducidad && hoy <= c.FechaPrescripcion)
                || (prescrito && hoy > c.FechaPrescripcion));
        }

        if (f.EstadoProceso is { Length: > 0 } procesos)
        {
            var noEncontrado = procesos.Contains("NoEncontrado");
            var inactivado = procesos.Contains("Inactivado");
            var favorable = procesos.Contains("RespuestaFavorable");
            var desfavorable = procesos.Contains("RespuestaDesfavorable");
            var dp = procesos.Contains("DerechoPeticion");
            var sinProcesar = procesos.Contains("SinProcesar");
            q = Donde(q, "estadoProceso", c =>
                (noEncontrado && !c.Activo && c.MotivoInactivacion == MotivoInactivacion.DesaparecidoFuente)
                || (inactivado && !c.Activo && c.MotivoInactivacion == MotivoInactivacion.InactivadoOperador)
                || (c.Activo && favorable && c.EstadoGestion == EstadoGestion.RespuestaOt
                    && db.DerechosPeticion.Any(d => d.ComparendoId == c.Id && d.RespuestaSentido == SentidoRespuestaOt.Favorable))
                || (c.Activo && desfavorable && c.EstadoGestion == EstadoGestion.RespuestaOt
                    && db.DerechosPeticion.Any(d => d.ComparendoId == c.Id && d.RespuestaSentido == SentidoRespuestaOt.Desfavorable))
                || (c.Activo && dp && c.EstadoGestion == EstadoGestion.DpEnviado)
                || (c.Activo && sinProcesar && c.EstadoGestion == EstadoGestion.SinGestion));
        }

        if (Enums<EstadoNotificacionConductor>(f.UltimoAviso) is { Count: > 0 } avisos)
            q = Donde(q, "ultimoAviso", c => avisos.Contains(c.EstadoNotificacionConductor));
        if (Enums<EstadoCambioContraventor>(f.CambioContraventor) is { Count: > 0 } cambios)
            q = Donde(q, "cambioContraventor", c => cambios.Contains(c.EstadoCambioContraventor));
        if (f.Fuente is { Length: > 0 } fuentes)
        {
            var simit = fuentes.Contains("Simit");
            var municipal = fuentes.Contains("Municipal");
            var ocr = fuentes.Contains("Ocr");
            q = Donde(q, "fuente", c =>
                (simit && (c.TipoIngreso == TipoIngreso.SimitNit || c.TipoIngreso == TipoIngreso.SimitPlaca))
                || (municipal && (c.TipoIngreso == TipoIngreso.MunicipalNit || c.TipoIngreso == TipoIngreso.MunicipalPlaca))
                || (ocr && (c.TipoIngreso == TipoIngreso.Ocr || c.TipoIngreso == TipoIngreso.OcrComplemento)));
        }

        if (f.ConContraventor is { } conContraventor)
            q = q.Where(c => (c.Contraventor != null) == conContraventor);
        if (f.Pagado is { } pagado)
            q = q.Where(c => c.Pagado == pagado);
        if (f.ConDp is { } conDp)
            q = q.Where(c => c.TieneDp == conDp);
        if (f.ResultadoDp is { } resultado)
        {
            q = resultado switch
            {
                "Favorable" => q.Where(c => db.DerechosPeticion.Any(d => d.ComparendoId == c.Id && d.RespuestaSentido == SentidoRespuestaOt.Favorable)),
                "Desfavorable" => q.Where(c => db.DerechosPeticion.Any(d => d.ComparendoId == c.Id && d.RespuestaSentido == SentidoRespuestaOt.Desfavorable)),
                "SinRespuesta" => q.Where(c => db.DerechosPeticion.Any(d => d.ComparendoId == c.Id && d.RespuestaSentido == null)),
                _ => q,
            };
        }

        if (f.CobroCoactivo is { } coactivo)
            q = q.Where(c => c.EnCobroCoactivo == coactivo);
        if (f.SinGestion == true)
            q = q.Where(c => !c.Gestionado);

        q = AplicarRapido(q, f.Rapido, h);
        return AplicarJuridico(q, f.Juridico, h);
    }

    /// <summary>Filtros rápidos (§6.11).</summary>
    public IQueryable<Comparendo> AplicarRapido(IQueryable<Comparendo> q, string? rapido, Hoy h)
    {
        ArgumentNullException.ThrowIfNull(h);
        var hoy = h.Dia;
        var limite = h.Mas2Habiles;
        return rapido switch
        {
            "ProximosAVencer" => q.Where(c => c.Activo && !c.Pagado && !c.TieneDp && c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion && c.FechaLimiteGestion <= limite),
            "SinContraventor" => q.Where(c => c.Activo && c.Contraventor == null
                && (db.ConfiguracionesEmpresa.Any(e => e.TenantId == c.TenantId && e.TipoIntegracionConductor == TipoIntegracionConductor.Manual)
                    || !db.ConfiguracionesEmpresa.Any(e => e.TenantId == c.TenantId)
                    || c.EstadoConsultaContraventor == EstadoConsultaContraventor.NoAplica
                    || c.EstadoConsultaContraventor == EstadoConsultaContraventor.NoEncontrado
                    || c.EstadoConsultaContraventor == EstadoConsultaContraventor.Error)),
            "RechazadosOt" => q.Where(c => c.Activo && c.EstadoCambioContraventor == EstadoCambioContraventor.RechazadoOt),
            // El mismo criterio que ServicioReportes.Radar: sin DP ni pago y la gestión vence en los próximos 7 días hábiles.
            "Radar" => q.Where(c => c.Activo && !c.Pagado && !c.TieneDp && c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion && c.FechaLimiteGestion <= h.Mas7Habiles),
            _ => q,
        };
    }

    /// <summary>Filtros jurídicos predefinidos (RN-182) con las definiciones provisionales de §6.11 (S-57).</summary>
    private static IQueryable<Comparendo> AplicarJuridico(IQueryable<Comparendo> q, string? juridico, Hoy h)
    {
        var hoy = h.Dia;
        var en30 = hoy.AddDays(30);
        return juridico switch
        {
            "CaducidadInmediata" => q.Where(c => c.Activo && !c.TieneDp && c.FechaCaducidad != null
                && ((hoy > c.FechaCaducidad && hoy <= c.FechaPrescripcion) || (hoy <= c.FechaCaducidad && c.FechaCaducidad <= en30))),
            "NoImpugnables" => q.Where(c => c.FechaLimiteGestion != null && hoy > c.FechaLimiteGestion && hoy <= c.FechaCaducidad),
            "C038" => q.Where(c => c.EsFotomulta == true && c.TipoDocumentoInfractor == TipoDocumento.NIT),
            _ => q,
        };
    }

    public static IOrderedQueryable<Comparendo> Ordenar(IQueryable<Comparendo> q, string? orden, bool descendente, DateOnly hoy) => (orden, descendente) switch
    {
        ("valor", false) => q.OrderBy(c => c.ValorTotal).ThenBy(c => c.Id),
        ("valor", true) => q.OrderByDescending(c => c.ValorTotal).ThenBy(c => c.Id),
        ("imposicion", false) => q.OrderBy(c => c.FechaImposicion).ThenBy(c => c.Id),
        ("imposicion", true) => q.OrderByDescending(c => c.FechaImposicion).ThenBy(c => c.Id),
        ("notificacion", false) => q.OrderBy(c => c.FechaNotificacion).ThenBy(c => c.Id),
        ("notificacion", true) => q.OrderByDescending(c => c.FechaNotificacion).ThenBy(c => c.Id),
        ("actividad", false) => q.OrderBy(c => c.UltimoEventoEn).ThenBy(c => c.Id),
        ("actividad", true) => q.OrderByDescending(c => c.UltimoEventoEn).ThenBy(c => c.Id),
        ("numero", false) => q.OrderBy(c => c.NumeroNormalizado),
        ("numero", true) => q.OrderByDescending(c => c.NumeroNormalizado),
        // Por defecto: primero los EN_TIEMPO por fecha límite (lo más urgente arriba), luego el resto del más nuevo al más viejo.
        _ => q.OrderBy(c => c.Activo && c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion ? 0 : 1)
            .ThenBy(c => c.Activo && c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion ? c.FechaLimiteGestion : null)
            .ThenByDescending(c => c.CreadoEn)
            .ThenBy(c => c.Id),
    };

    public async Task<List<FilaBandeja>> ProyectarAsync(IQueryable<Comparendo> q, Hoy h, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(h);
        var filas = await q.Select(c => new
        {
            c,
            Organismo = db.OrganismosTransito.Where(o => o.Id == c.OrganismoTransitoId).Select(o => o.Nombre).FirstOrDefault(),
            Titular = c.NombreInfractor ?? db.IdentificacionesMonitoreadas.Where(n => n.Id == c.IdentificacionMonitoreadaId).Select(n => n.NombreTitular).FirstOrDefault(),
            Contraventor = c.Contraventor == null ? null : c.Contraventor.Nombres,
            Dp = db.DerechosPeticion.Where(d => d.ComparendoId == c.Id).Select(d => new { d.Radicado, d.Estado, d.RespuestaSentido }).FirstOrDefault(),
        }).ToListAsync(ct).ConfigureAwait(false);

        return filas.Select(x =>
        {
            var c = x.c;
            var s = h.Motor.Evaluar(Plazos.Fechas(c), h.Dia);
            return new FilaBandeja(
                c.Id, c.Numero, c.Placa, c.NitConsultado, x.Titular, c.OrganismoTransitoId, x.Organismo, c.SecretariaTexto,
                c.FechaImposicion, c.FechaNotificacion, c.ValorTotal, FuenteDe(c.TipoIngreso).ToString(), c.FuentePrincipal?.ToString(),
                s.Estado.ToString(), s.Estado == Domain.Plazos.EstadoLegal.EnTiempo ? s.HabilesRestantesGestion : null,
                c.Activo && !c.Pagado && !c.TieneDp && s.ProximoAVencer,
                Plazos.Ventana(s, c).ToString(), s.FinDescuentoVigente,
                EstadoProceso(c, x.Dp?.RespuestaSentido), c.EstadoGestion.ToString(), x.Contraventor, c.EstadoConsultaContraventor.ToString(),
                c.EstadoNotificacionConductor.ToString(), UltimoHito(c), c.EstadoCambioContraventor.ToString(),
                x.Dp?.Radicado, x.Dp?.Estado.ToString(), x.Dp?.RespuestaSentido?.ToString(),
                c.Pagado, c.Activo, c.MotivoInactivacion?.ToString(), c.EnCobroCoactivo, c.EsFotomulta != false, c.UltimoEventoEn, c.CreadoEn);
        }).ToList();
    }

    public static Fuente FuenteDe(TipoIngreso t) => t switch
    {
        TipoIngreso.SimitNit or TipoIngreso.SimitPlaca => Domain.Modelo.Fuente.Simit,
        TipoIngreso.MunicipalNit or TipoIngreso.MunicipalPlaca => Domain.Modelo.Fuente.Municipal,
        TipoIngreso.Ocr or TipoIngreso.OcrComplemento => Domain.Modelo.Fuente.Ocr,
        _ => Domain.Modelo.Fuente.MigracionV1,
    };

    /// <summary>Etiqueta «estado del proceso» derivada (§4.9).</summary>
    public static string EstadoProceso(Comparendo c, SentidoRespuestaOt? sentido)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!c.Activo)
            return c.MotivoInactivacion == MotivoInactivacion.InactivadoOperador ? "Inactivado" : "NoEncontrado";
        return c.EstadoGestion switch
        {
            EstadoGestion.RespuestaOt => sentido == SentidoRespuestaOt.Desfavorable ? "RespuestaDesfavorable" : "RespuestaFavorable",
            EstadoGestion.DpEnviado => "DerechoPeticion",
            _ => "SinProcesar",
        };
    }

    /// <summary>
    /// Etiqueta «Último hito de notificación» (§4.9, reemplaza al estado acumulativo de la Epic): el hito más avanzado
    /// en el orden de la tabla de la spec. Aviso 4 = se pidió el cambio de contraventor a la secretaría.
    /// </summary>
    public static string UltimoHito(Comparendo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.EstadoCambioContraventor is EstadoCambioContraventor.Solicitado or EstadoCambioContraventor.RechazadoOt or EstadoCambioContraventor.AceptadoOt)
            return "Aviso4";
        if (c.EstadoNotificacionConductor != EstadoNotificacionConductor.SinNotificar)
            return c.EstadoNotificacionConductor.ToString();
        return c.TieneDp || c.EstadoGestion is EstadoGestion.DpEnviado or EstadoGestion.RespuestaOt ? "DpEnviado" : "SinProcesar";
    }

    /// <summary>Texto de la etiqueta para el Excel (el mismo del frontend, lib/etiquetas.ts).</summary>
    public static string TextoHito(string hito) => hito switch
    {
        "Aviso4" => "Notificación 4 — solicitud de cambio de contraventor",
        "Aviso3" => "Notificación al conductor 3 — sin descuento",
        "Aviso2" => "Notificación al conductor 2 — descuento 25%",
        "Aviso1" => "Notificación al conductor 1 — aviso inicial",
        "DpEnviado" => "Notificado a secretaría — derecho de petición",
        _ => "Sin procesar",
    };

    /// <summary>Placas y números de una lista pegada, con el mismo formato con que se guardan.</summary>
    private static List<string>? Normalizados(string[]? valores) =>
        valores?.Select(NumeroComparendo.Normalizar).OfType<string>().Distinct().ToList();

    private static List<T>? Enums<T>(string[]? valores)
        where T : struct, Enum =>
        valores?.Select(v => Enum.TryParse<T>(v, true, out var e) ? (T?)e : null).Where(e => e is not null).Select(e => e!.Value).ToList();
}
