using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Reportes;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Flit.Queries.Domain;
using Microsoft.EntityFrameworkCore;
using C = Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas.ComparendoQueryFieldCatalog;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>Sobre qué se consulta: una empresa, o todas (solo el SuperAdmin, con <see cref="Tenant"/> null).</summary>
public sealed record AlcanceConsulta(Guid? Tenant)
{
    public bool TodasLasEmpresas => Tenant is null;

    public C Catalogo => C.Para(TodasLasEmpresas);
}

/// <summary>
/// Ejecuta las consultas personalizadas de Comparendos con el motor compartido (<see cref="QueryEngine{TRow}"/>).
///
/// <para><b>Volumen (CONTRATO §3).</b> Renting tiene unos 49.000 comparendos, más que <see cref="QueryLimits.MaxUniverso"/>.
/// Por eso, al revés que en Trámites, a la base se empuja SIEMPRE el rango de la fecha elegida y los «es alguno» de los
/// identificadores (número, placa, radicado), y se CUENTA antes de cargar: si no cabe, se pide acotar en vez de traer
/// una porción arbitraria.</para>
///
/// <para><b>Cobertura.</b> Con el universo acotado por fecha, un número pedido que existe fuera del rango no llegaría a
/// memoria y saldría como «no existe». Se cargan aparte, sin fecha y con tope, las filas de los identificadores pedidos
/// que no salieron; entran al universo de la cobertura y NO a <c>matched</c>.</para>
///
/// <para><b>Sin identificadores no hay cobertura</b>, así que ahí se empujan también las condiciones «es alguno» que
/// tienen columna (activo, pagado, estado legal…). Es lo que deja abrir las consultas de fábrica de Renting sin pasar
/// el tope. En memoria se vuelven a evaluar todas: lo empujado solo achica, nunca decide.</para>
/// </summary>
public sealed class ServicioConsultasPersonalizadas(ComparendosDb db, ConsultaBandeja bandeja)
{
    /// <summary>Tope de filas extra para la cobertura (identificadores pedidos fuera del rango).</summary>
    public const int MaxExtraCobertura = 500;

    // ── Ejecución ─────────────────────────────────────────────────────────────────────────────

    public async Task<ComparendoQueryResult> EjecutarAsync(AlcanceConsulta alcance, QueryRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(request);
        var catalogo = alcance.Catalogo;
        var (definicion, originales) = CanonizarIdentificadores(QueryNormalizer.Normalize(catalogo, request.Definition));
        var h = await bandeja.HoyAsync(ct).ConfigureAwait(false);
        var (desde, hasta) = QueryRangePreset.Resolve(definicion.Fechas, h.Dia);
        var motor = Motor(alcance);

        var universoQ = Universo(alcance, definicion, h, desde, hasta);
        var total = await universoQ.CountAsync(ct).ConfigureAwait(false);
        if (total > QueryLimits.MaxUniverso)
            throw new SuperAdminQueryTooBroadException(total, QueryLimits.MaxUniverso, C.Instance.RowNounPlural,
                alcance.TodasLasEmpresas ? "una empresa, un NIT o un rango de fecha más corto" : "un NIT o un rango de fecha más corto");

        var filas = await CargarAsync(universoQ, h, ct).ConfigureAwait(false);

        var condiciones = definicion.Condiciones.Select(c => (Condition: c, Predicate: motor.BuildPredicate(c))).ToList();
        bool Pasa(Fila f) => condiciones.All(c => c.Predicate(f));
        var (from, to) = Rango(desde, hasta);
        var matched = filas.Where(f => motor.InRange(f, definicion.Fechas.Campo, from, to) && Pasa(f)).ToList();

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, QueryLimits.MaxPageSize);
        int? anterior = page == 1 ? await AnteriorAsync(alcance, definicion, h, motor, desde, hasta, Pasa, ct).ConfigureAwait(false) : null;

        var extra = await ExtraCoberturaAsync(alcance, definicion, filas, h, ct).ConfigureAwait(false);
        var cobertura = motor.BuildCoverage(definicion, extra.Count == 0 ? filas : [.. filas, .. extra], matched, condiciones, from, to)
            .Select(i => originales.TryGetValue((i.Campo, i.Valor), out var original) ? i with { Valor = original } : i)
            .ToList();

        var pagina = Ordenar(matched, definicion).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var nombres = await NombresAsync(pagina, alcance, ct).ConfigureAwait(false);
        return new ComparendoQueryResult(matched.Count, page, pageSize, desde, hasta, anterior,
            pagina.Select(f => ARow(f, alcance, nombres)).ToList(), cobertura);
    }

    /// <summary>Empresas que aparecen en el resultado (el SuperAdmin con «todas» las deja anotadas en la auditoría).</summary>
    public static IReadOnlyCollection<Guid> EmpresasDe(ComparendoQueryResult resultado) =>
        resultado?.Filas.Where(f => f.EmpresaId is not null).Select(f => f.EmpresaId!.Value).Distinct().ToList() ?? [];

    /// <summary>
    /// Para el número y la placa, el motor compara con <see cref="QueryEngine{TRow}.SinSeparadores"/>, pero en la base se
    /// guardan con su normalización propia (el número además pierde el prefijo de letras: «D-1100…» → «1100…»). Sin esto
    /// la base encontraría el comparendo y la memoria lo descartaría: una fila perdida en silencio. Los valores se
    /// canonizan igual que se guardan, y la cobertura se devuelve con lo que escribió el usuario.
    /// </summary>
    private static (QueryDefinition Definicion, Dictionary<(string, string), string> Originales) CanonizarIdentificadores(QueryDefinition d)
    {
        var originales = new Dictionary<(string, string), string>();
        var condiciones = d.Condiciones.Select(c =>
        {
            if (c.FieldId is not (C.Numero or C.Placa) || c.Operator is not (QueryOperator.EsAlguno or QueryOperator.NoEsNinguno))
                return c;
            var valores = new List<string>();
            foreach (var v in c.Values)
            {
                var canonico = c.FieldId == C.Numero ? NumeroComparendo.Normalizar(v) ?? v : Placa.Normalizar(v) ?? QueryEngine<Fila>.SinSeparadores(v);
                if (valores.Contains(canonico, StringComparer.Ordinal))
                    continue;
                valores.Add(canonico);
                originales.TryAdd((c.FieldId, canonico), v);
            }

            return c with { Values = valores };
        }).ToList();
        return (d with { Condiciones = condiciones }, originales);
    }

    private static (DateTimeOffset From, DateTimeOffset To) Rango(DateOnly desde, DateOnly hasta) =>
        (Alcance.Utc(desde), Alcance.Utc(hasta.AddDays(1)).AddTicks(-1));

    // ── Universo (lo que se empuja a la base) ─────────────────────────────────────────────────

    private static List<QueryCondition> Identificadores(QueryDefinition d) =>
        d.Condiciones.Where(c => c.Operator == QueryOperator.EsAlguno && C.EsIdentificador(c.FieldId)).ToList();

    private IQueryable<Comparendo> Universo(AlcanceConsulta alcance, QueryDefinition d, ConsultaBandeja.Hoy h, DateOnly desde, DateOnly hasta)
    {
        var identificadores = Identificadores(d);
        var empujar = identificadores.Count == 0;
        string[]? Valores(string campo) =>
            empujar ? d.Condiciones.FirstOrDefault(c => c.FieldId == campo && c.Operator == QueryOperator.EsAlguno)?.Values.ToArray() : null;

        var campo = d.Fechas.Campo;
        var filtros = new FiltrosBandeja
        {
            // Inactivos incluidos: «activo» es una condición más de la consulta, no un filtro implícito como en la bandeja.
            Activos = Valores(C.Activo) is [var activo] ? (activo == "true" ? "activos" : "inactivos") : "todos",
            ImposicionDesde = campo == C.FechaImposicion ? desde : null,
            ImposicionHasta = campo == C.FechaImposicion ? hasta : null,
            NotificacionDesde = campo == C.FechaNotificacion ? desde : null,
            NotificacionHasta = campo == C.FechaNotificacion ? hasta : null,
            RegistroDesde = campo == C.FechaIngreso ? desde : null,
            RegistroHasta = campo == C.FechaIngreso ? hasta : null,
            Numeros = identificadores.FirstOrDefault(c => c.FieldId == C.Numero)?.Values.ToArray(),
            Placas = identificadores.FirstOrDefault(c => c.FieldId == C.Placa)?.Values.ToArray(),
            // El estado legal, con la misma traducción a SQL que la bandeja (R1).
            EstadoLegal = Valores(C.EstadoLegal),
            Organismos = Guids(Valores(C.Organismo)),
            Nits = Guids(Valores(C.Nit)),
            UltimoAviso = Valores(C.NotificacionConductor),
            CambioContraventor = Valores(C.CambioContraventor),
            Pagado = Bool(Valores(C.Pagado)),
            ConDp = Bool(Valores(C.TieneDp)),
            CobroCoactivo = Bool(Valores(C.CobroCoactivo)),
        };
        var q = bandeja.Filtrar(alcance.Tenant, filtros, h);

        if (campo == C.FechaDpRadicacion)
            q = q.Where(c => db.DerechosPeticion.Any(x => x.ComparendoId == c.Id && x.FechaRadicacion >= desde && x.FechaRadicacion <= hasta));
        else if (campo == C.FechaDpRespuesta)
            q = q.Where(c => db.DerechosPeticion.Any(x => x.ComparendoId == c.Id && x.RespuestaFecha >= desde && x.RespuestaFecha <= hasta));
        else if (campo == C.FechaPago)
            q = q.Where(c => db.Pagos.Any(x => x.ComparendoId == c.Id && x.FechaPago >= desde && x.FechaPago <= hasta));

        if (identificadores.FirstOrDefault(c => c.FieldId == C.DpRadicado) is { } radicados)
        {
            // La misma normalización que SinSeparadores, escrita del lado de la base (el radicado lleva guiones).
            var valores = radicados.Values.Select(QueryEngine<Fila>.SinSeparadores).ToList();
            q = q.Where(c => db.DerechosPeticion.Any(x => x.ComparendoId == c.Id
                && valores.Contains(x.Radicado.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))));
        }

        if (!empujar)
            return q;

        if (Guids(Valores(C.Empresa)) is { } empresas)
            q = q.Where(c => empresas.Contains(c.TenantId));
        if (Bool(Valores(C.Gestionado)) is { } gestionado)
            q = q.Where(c => c.Gestionado == gestionado);
        if (Bool(Valores(C.NotificadoFisicamente)) is { } fisico)
            q = q.Where(c => c.NotificadoFisicamente == fisico);
        // Igual que la fila (ConsultaBandeja.ProyectarAsync): sin dato de fotodetección cuenta como fotomulta.
        if (Bool(Valores(C.Fotomulta)) is { } foto)
            q = foto ? q.Where(c => c.EsFotomulta != false) : q.Where(c => c.EsFotomulta == false);
        // Los textos de opción se comparan como en memoria (EnMayusculas del motor: sin espacios a los lados y en mayúsculas).
        if (Mayusculas(Valores(C.CodigoInfraccion)) is { } codigos)
            q = q.Where(c => c.CodigoInfraccion != null && codigos.Contains(c.CodigoInfraccion.Trim().ToUpper()));
        if (Mayusculas(Valores(C.TipoVehiculo)) is { } tiposVehiculo)
            q = q.Where(c => c.TipoVehiculo != null && tiposVehiculo.Contains(c.TipoVehiculo.Trim().ToUpper()));
        if (Mayusculas(Valores(C.ServicioVehiculo)) is { } servicios)
            q = q.Where(c => c.ServicioVehiculo != null && servicios.Contains(c.ServicioVehiculo.Trim().ToUpper()));
        if (Enums<EstadoGestion>(Valores(C.EstadoGestion)) is { } gestiones)
            q = q.Where(c => gestiones.Contains(c.EstadoGestion));
        if (Enums<EstadoConsultaContraventor>(Valores(C.ConsultaContraventor)) is { } consultas)
            q = q.Where(c => consultas.Contains(c.EstadoConsultaContraventor));
        if (Valores(C.VentanaDescuento) is { } ventanas)
        {
            // La traducción de Plazos.Ventana que ya usa la lente de dinero (ServicioReportes.Dinero, GruposDeudaAsync).
            var hoy = h.Dia;
            var d50 = ventanas.Contains("Descuento50");
            var d25 = ventanas.Contains("Descuento25");
            var sin = ventanas.Contains("SinDescuento");
            var sinBase = ventanas.Contains("SinFechaBase");
            q = q.Where(c =>
                (sinBase && c.FechaFinDescuento50 == null)
                || (d50 && c.FechaFinDescuento50 != null && hoy <= c.FechaFinDescuento50)
                || (d25 && c.FechaFinDescuento50 != null && hoy > c.FechaFinDescuento50 && c.FechaFinDescuento25 != null && hoy <= c.FechaFinDescuento25)
                || (sin && c.FechaFinDescuento50 != null && hoy > c.FechaFinDescuento50 && (c.FechaFinDescuento25 == null || hoy > c.FechaFinDescuento25)));
        }

        if (Enums<EstadoDp>(Valores(C.DpEstado)) is { } estadosDp)
            q = q.Where(c => db.DerechosPeticion.Any(x => x.ComparendoId == c.Id && estadosDp.Contains(x.Estado)));
        if (Enums<TipoPago>(Valores(C.PagoTipo)) is { } tiposPago)
            q = q.Where(c => db.Pagos.Any(x => x.ComparendoId == c.Id && tiposPago.Contains(x.TipoPago)));
        return q;
    }

    private static Guid[]? Guids(string[]? valores) =>
        valores?.Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null).OfType<Guid>().ToArray();

    private static List<string>? Mayusculas(string[]? valores) => valores?.Select(v => v.Trim().ToUpperInvariant()).ToList();

    private static bool? Bool(string[]? valores) => valores is [var v] ? v == "true" : null;

    private static List<T>? Enums<T>(string[]? valores)
        where T : struct, Enum =>
        valores?.Select(v => Enum.TryParse<T>(v, true, out var e) ? (T?)e : null).OfType<T>().ToList();

    // ── Carga ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Una pasada: el comparendo con su DP y su pago (uno por comparendo, índices únicos). Los catálogos van aparte.</summary>
    private async Task<List<Fila>> CargarAsync(IQueryable<Comparendo> universo, ConsultaBandeja.Hoy h, CancellationToken ct)
    {
        var cargas = await (from c in universo
                            join d in db.DerechosPeticion on c.Id equals d.ComparendoId into ds
                            from d in ds.DefaultIfEmpty()
                            join p in db.Pagos on c.Id equals p.ComparendoId into ps
                            from p in ps.DefaultIfEmpty()
                            select new Carga(c, d, p))
            .Take(QueryLimits.MaxUniverso)
            .ToListAsync(ct).ConfigureAwait(false);
        if (cargas.Count == 0)
            return [];

        var organismoIds = cargas.Select(x => x.C.OrganismoTransitoId).OfType<Guid>().Distinct().ToList();
        var organismos = await db.OrganismosTransito.AsNoTracking().Where(o => organismoIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct).ConfigureAwait(false);
        var nitIds = cargas.Select(x => x.C.IdentificacionMonitoreadaId).OfType<Guid>().Distinct().ToList();
        var nits = await db.IdentificacionesMonitoreadas.AsNoTracking().Where(n => nitIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, ct).ConfigureAwait(false);
        var tipologiaIds = cargas.Where(x => x.Dp is not null).Select(x => x.Dp!.TipologiaId).Distinct().ToList();
        var tipologias = await db.Tipologias.AsNoTracking().Where(t => tipologiaIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct).ConfigureAwait(false);

        return cargas.Select(x =>
        {
            var s = h.Motor.Evaluar(Plazos.Fechas(x.C), h.Dia);
            var organismo = x.C.OrganismoTransitoId is { } oid ? organismos.GetValueOrDefault(oid) : null;
            var nit = x.C.IdentificacionMonitoreadaId is { } nid ? nits.GetValueOrDefault(nid) : null;
            return new Fila(x.C, x.Dp, x.Pago, organismo, nit,
                x.Dp is null ? null : tipologias.GetValueOrDefault(x.Dp.TipologiaId),
                s.Estado.ToString(), Plazos.Ventana(s, x.C).ToString());
        }).ToList();
    }

    /// <summary>
    /// Filas de los identificadores pedidos que no llegaron en el universo, sin filtro de fecha ni de condiciones y con
    /// tope: solo sirven para que la cobertura diga «existe, pero…» en vez de «no existe».
    /// </summary>
    private async Task<List<Fila>> ExtraCoberturaAsync(AlcanceConsulta alcance, QueryDefinition d, List<Fila> cargadas, ConsultaBandeja.Hoy h, CancellationToken ct)
    {
        var identificadores = Identificadores(d);
        if (identificadores.Count == 0)
            return [];

        var numeros = Faltantes(identificadores, C.Numero, cargadas, f => f.C.NumeroNormalizado);
        var placas = Faltantes(identificadores, C.Placa, cargadas, f => f.C.Placa);
        var radicados = Faltantes(identificadores, C.DpRadicado, cargadas, f => f.Dp?.Radicado);
        if (numeros.Count + placas.Count + radicados.Count == 0)
            return [];

        // Sin excluir los ya cargados: una fila cargada tiene sus valores «vistos», así que no coincide con ninguno faltante.
        var q = db.Comparendos.AsNoTracking().Where(c => (alcance.Tenant == null || c.TenantId == alcance.Tenant)
            && (numeros.Contains(c.NumeroNormalizado)
                || (c.Placa != null && placas.Contains(c.Placa))
                || db.DerechosPeticion.Any(x => x.ComparendoId == c.Id
                    && radicados.Contains(x.Radicado.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", "")))));
        return await CargarAsync(q.OrderBy(c => c.Id).Take(MaxExtraCobertura), h, ct).ConfigureAwait(false);
    }

    private static List<string> Faltantes(List<QueryCondition> identificadores, string campo, List<Fila> cargadas, Func<Fila, string?> valor)
    {
        var pedidos = identificadores.FirstOrDefault(c => c.FieldId == campo)?.Values.Select(QueryEngine<Fila>.SinSeparadores).ToList() ?? [];
        if (pedidos.Count == 0)
            return [];
        var vistos = cargadas.Select(valor).Where(v => v is not null).Select(v => QueryEngine<Fila>.SinSeparadores(v!)).ToHashSet(StringComparer.Ordinal);
        return pedidos.Where(p => !vistos.Contains(p)).ToList();
    }

    /// <summary>
    /// El mismo filtro en el periodo anterior de igual ancho («12 % más que antes»). Se carga aparte porque el rango se
    /// empuja a la base; si ese periodo no cabe en el tope, no se compara (null) en vez de comparar contra una porción.
    /// </summary>
    private async Task<int?> AnteriorAsync(AlcanceConsulta alcance, QueryDefinition d, ConsultaBandeja.Hoy h, QueryEngine<Fila> motor,
        DateOnly desde, DateOnly hasta, Func<Fila, bool> pasa, CancellationToken ct)
    {
        var dias = hasta.DayNumber - desde.DayNumber + 1;
        var (pDesde, pHasta) = (desde.AddDays(-dias), desde.AddDays(-1));
        var q = Universo(alcance, d, h, pDesde, pHasta);
        if (await q.CountAsync(ct).ConfigureAwait(false) > QueryLimits.MaxUniverso)
            return null;
        var (from, to) = Rango(pDesde, pHasta);
        return (await CargarAsync(q, h, ct).ConfigureAwait(false)).Count(f => motor.InRange(f, d.Fechas.Campo, from, to) && pasa(f));
    }

    // ── El motor: acceso a los campos y a las fechas ──────────────────────────────────────────

    /// <summary>Con el catálogo de quien consulta: de él salen «su empresa» / «las empresas» de los avisos de cobertura.</summary>
    private static QueryEngine<Fila> Motor(AlcanceConsulta alcance) => new(alcance.Catalogo, Accessor, DateOf);

    private static Func<Fila, IReadOnlyList<string>> Accessor(string campo) => campo switch
    {
        C.Numero => f => [f.C.NumeroNormalizado],
        C.Placa => f => Uno(f.C.Placa),
        C.Nit => f => Uno(f.C.IdentificacionMonitoreadaId?.ToString()),
        C.Empresa => f => [f.C.TenantId.ToString()],
        C.CodigoInfraccion => f => Uno(f.C.CodigoInfraccion),
        C.Fotomulta => f => [Si(f.C.EsFotomulta != false)],
        C.TipoVehiculo => f => Uno(f.C.TipoVehiculo),
        C.ServicioVehiculo => f => Uno(f.C.ServicioVehiculo),
        C.Organismo => f => Uno(f.C.OrganismoTransitoId?.ToString()),
        C.Departamento => f => Uno(f.Organismo?.Departamento),
        C.Municipio => f => Uno(f.Organismo?.Municipio),
        C.Activo => f => [Si(f.C.Activo)],
        C.EstadoLegal => f => [f.EstadoLegal],
        C.VentanaDescuento => f => [f.Ventana],
        C.CobroCoactivo => f => [Si(f.C.EnCobroCoactivo)],
        C.Gestionado => f => [Si(f.C.Gestionado)],
        C.Pagado => f => [Si(f.C.Pagado)],
        C.EstadoGestion => f => [f.C.EstadoGestion.ToString()],
        C.TieneDp => f => [Si(f.C.TieneDp)],
        C.DpEstado => f => Uno(f.Dp?.Estado.ToString()),
        C.DpSentido => f => Uno(f.Dp?.RespuestaSentido?.ToString()),
        C.DpTipologia => f => Uno(f.Dp?.TipologiaId.ToString()),
        C.DpAutomatico => f => f.Dp is null ? [] : [Si(f.Dp.EnvioAutomatico)],
        C.DpRadicado => f => Uno(f.Dp?.Radicado),
        C.NotificacionConductor => f => [f.C.EstadoNotificacionConductor.ToString()],
        C.CambioContraventor => f => [f.C.EstadoCambioContraventor.ToString()],
        C.ConsultaContraventor => f => [f.C.EstadoConsultaContraventor.ToString()],
        C.NotificadoFisicamente => f => [Si(f.C.NotificadoFisicamente)],
        C.PagoTipo => f => Uno(f.Pago?.TipoPago.ToString()),
        _ => _ => [],
    };

    private static DateTimeOffset? DateOf(Fila f, string campo) => campo switch
    {
        C.FechaNotificacion => Instante(f.C.FechaNotificacion),
        C.FechaIngreso => f.C.CreadoEn,
        C.FechaDpRadicacion => Instante(f.Dp?.FechaRadicacion),
        C.FechaDpRespuesta => Instante(f.Dp?.RespuestaFecha),
        C.FechaPago => Instante(f.Pago?.FechaPago),
        _ => Instante(f.C.FechaImposicion),
    };

    private static DateTimeOffset? Instante(DateOnly? d) => d is { } dia ? Alcance.Utc(dia) : null;

    private static IReadOnlyList<string> Uno(string? v) => string.IsNullOrWhiteSpace(v) ? [] : [v];

    private static string Si(bool v) => v ? "true" : "false";

    private static IEnumerable<Fila> Ordenar(List<Fila> filas, QueryDefinition d)
    {
        // El desempate por número e id no es cosmético: el export encadena páginas y sin él se repetirían o perderían filas.
        Func<Fila, IComparable?> clave = d.SortBy switch
        {
            C.OrdenValorTotal => f => f.C.ValorTotal,
            C.OrdenFechaLimiteGestion => f => f.C.FechaLimiteGestion,
            C.OrdenNumero => f => f.C.NumeroNormalizado,
            _ => f => DateOf(f, d.Fechas.Campo),
        };
        var ordenadas = d.Descending ? filas.OrderByDescending(clave) : filas.OrderBy(clave);
        return ordenadas.ThenBy(f => f.C.NumeroNormalizado, StringComparer.Ordinal).ThenBy(f => f.C.Id);
    }

    // ── Proyección ────────────────────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, string>> NombresAsync(List<Fila> pagina, AlcanceConsulta alcance, CancellationToken ct)
    {
        if (!alcance.TodasLasEmpresas || pagina.Count == 0)
            return [];
        var ids = pagina.Select(f => f.C.TenantId).Distinct().ToList();
        return await db.ConfiguracionesEmpresa.AsNoTracking().Where(e => ids.Contains(e.TenantId))
            .ToDictionaryAsync(e => e.TenantId, e => e.EmpresaNombre ?? e.TenantId.ToString(), ct).ConfigureAwait(false);
    }

    private static string? NitTexto(IdentificacionMonitoreada? n, string? consultado) =>
        n is null ? consultado : n.DigitoVerificacion is { } dv ? $"{n.NumeroBase}-{dv}" : n.NumeroBase;

    private static ComparendoQueryRow ARow(Fila f, AlcanceConsulta alcance, Dictionary<Guid, string> empresas)
    {
        var c = f.C;
        return new ComparendoQueryRow(
            c.Id, c.Numero, c.Placa, NitTexto(f.Nit, c.NitConsultado), c.NombreInfractor ?? f.Nit?.NombreTitular,
            alcance.TodasLasEmpresas ? c.TenantId : null,
            alcance.TodasLasEmpresas ? empresas.GetValueOrDefault(c.TenantId, c.TenantId.ToString()) : null,
            c.CodigoInfraccion, c.DescripcionInfraccion, c.EsFotomulta != false, c.TipoVehiculo, c.ServicioVehiculo,
            c.OrganismoTransitoId, f.Organismo?.Nombre ?? c.SecretariaTexto, f.Organismo?.Departamento, f.Organismo?.Municipio,
            c.FechaImposicion, c.FechaNotificacion, DateOnly.FromDateTime(ColombiaTime.From(c.CreadoEn).DateTime),
            c.FechaLimiteGestion, c.FechaFinDescuento50, c.FechaFinDescuento25, c.FechaCaducidad, c.FechaPrescripcion,
            c.ValorCapital, c.ValorAdicional, c.ValorTotal,
            c.Activo, f.EstadoLegal, f.Ventana, c.EnCobroCoactivo, c.EstadoGestion.ToString(), c.Gestionado, c.Pagado,
            c.TieneDp, f.Dp?.Estado.ToString(), f.Dp?.Radicado, f.Tipologia, f.Dp?.EnvioAutomatico, f.Dp?.FechaRadicacion,
            f.Dp?.RespuestaSentido?.ToString(), f.Dp?.RespuestaFecha,
            c.EstadoNotificacionConductor.ToString(), c.NotificadoFisicamente, c.EstadoCambioContraventor.ToString(),
            c.EstadoConsultaContraventor.ToString(),
            f.Pago?.TipoPago.ToString(), f.Pago?.ValorPagado, f.Pago?.FechaPago);
    }

    // ── Catálogo con las opciones de la empresa ───────────────────────────────────────────────

    /// <summary>El catálogo con las opciones que dependen de los datos sacadas de lo que la empresa (o las empresas) tiene.</summary>
    public async Task<IReadOnlyList<QueryFieldDto>> CamposAsync(AlcanceConsulta alcance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var t = alcance.Tenant;
        var propios = db.Comparendos.AsNoTracking().Where(c => t == null || c.TenantId == t);

        var nits = (await db.IdentificacionesMonitoreadas.AsNoTracking().Where(n => t == null || n.TenantId == t)
                .Select(n => new { n.Id, n.NumeroBase, n.DigitoVerificacion, n.NombreTitular }).ToListAsync(ct).ConfigureAwait(false))
            .Select(n => new QueryFieldOptionDto(n.Id.ToString(),
                $"{(n.DigitoVerificacion is { } dv ? $"{n.NumeroBase}-{dv}" : n.NumeroBase)}{(n.NombreTitular is { Length: > 0 } nombre ? $" — {nombre}" : string.Empty)}"))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();

        var codigos = await Distintos(propios.Select(c => c.CodigoInfraccion), ct).ConfigureAwait(false);
        var tipos = await Distintos(propios.Select(c => c.TipoVehiculo), ct).ConfigureAwait(false);
        var servicios = await Distintos(propios.Select(c => c.ServicioVehiculo), ct).ConfigureAwait(false);

        var organismoIds = propios.Where(c => c.OrganismoTransitoId != null).Select(c => c.OrganismoTransitoId!.Value).Distinct();
        var organismos = await db.OrganismosTransito.AsNoTracking().Where(o => organismoIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Nombre, o.Departamento, o.Municipio }).ToListAsync(ct).ConfigureAwait(false);
        var tipologiaIds = db.DerechosPeticion.AsNoTracking().Where(d => t == null || d.TenantId == t).Select(d => d.TipologiaId).Distinct();
        var tipologias = await db.Tipologias.AsNoTracking().Where(x => tipologiaIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Nombre }).ToListAsync(ct).ConfigureAwait(false);

        var empresas = new List<QueryFieldOptionDto>();
        if (alcance.TodasLasEmpresas)
        {
            var conComparendos = db.Comparendos.Select(c => c.TenantId).Distinct();
            empresas = (await db.ConfiguracionesEmpresa.AsNoTracking().Where(e => conComparendos.Contains(e.TenantId))
                    .Select(e => new { e.TenantId, e.EmpresaNombre }).ToListAsync(ct).ConfigureAwait(false))
                .Select(e => new QueryFieldOptionDto(e.TenantId.ToString(), e.EmpresaNombre ?? e.TenantId.ToString()))
                .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static List<QueryFieldOptionDto> Textos(IEnumerable<string?> valores) => valores.OfType<string>()
            .Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(v => new QueryFieldOptionDto(v, v)).ToList();

        var porCampo = new Dictionary<string, IReadOnlyList<QueryFieldOptionDto>>
        {
            [C.Nit] = nits,
            [C.Empresa] = empresas,
            [C.CodigoInfraccion] = Textos(codigos),
            [C.TipoVehiculo] = Textos(tipos),
            [C.ServicioVehiculo] = Textos(servicios),
            [C.Organismo] = organismos.Select(o => new QueryFieldOptionDto(o.Id.ToString(), o.Nombre))
                .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList(),
            [C.Departamento] = Textos(organismos.Select(o => o.Departamento)),
            [C.Municipio] = Textos(organismos.Select(o => o.Municipio)),
            [C.DpTipologia] = tipologias.Select(x => new QueryFieldOptionDto(x.Id.ToString(), x.Nombre))
                .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        return alcance.Catalogo.Fields.Select(f => porCampo.TryGetValue(f.Id, out var opciones) ? f with { Options = opciones } : f).ToList();
    }

    private static Task<List<string?>> Distintos(IQueryable<string?> q, CancellationToken ct) =>
        q.Where(v => v != null && v != "").Distinct().Take(1000).ToListAsync(ct);

    // ── Formas internas ───────────────────────────────────────────────────────────────────────

    private sealed record Carga(Comparendo C, DerechoPeticion? Dp, Pago? Pago);

    /// <summary>Un comparendo con todo lo que una condición pueda preguntarle, ya calculado (estado legal y descuento de hoy).</summary>
    internal sealed record Fila(
        Comparendo C,
        DerechoPeticion? Dp,
        Pago? Pago,
        OrganismoTransito? Organismo,
        IdentificacionMonitoreada? Nit,
        string? Tipologia,
        string EstadoLegal,
        string Ventana);
}
