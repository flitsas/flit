using System.Text.Json;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Motor de las consultas de la empresa gestora: el gemelo de <see cref="OtQueryRepository"/>, del
/// otro lado del trámite.
///
/// <para><b>Ninguna condición se arma con texto del cliente.</b> Lo que llega son ids del catálogo
/// (<see cref="CompanyQueryFieldCatalog"/>) y este repositorio decide qué significa cada uno. Es lo
/// que separa un constructor de consultas de un intérprete de SQL ajeno.</para>
///
/// <para><b>Una fila = un trámite.</b> Las condiciones que miran tablas hijas (prenda, adjuntos,
/// transformaciones, actores) se resuelven como «existe» y nunca como cruce: un join directo
/// duplicaría la fila del padre por cada hija que coincida y todos los conteos quedarían inflados
/// sin que nada avisara.</para>
///
/// <para><b>Solo se empuja a SQL el filtro por identificador</b>, igual que en el organismo y por la
/// misma razón: empujar además estado, organismo o tipo sería más rápido y rompería el aviso de
/// cobertura, porque una placa descartada por otro filtro nunca llegaría a memoria y se reportaría
/// como «no existe» en vez de «la dejó fuera este filtro». Es la optimización que alguien va a
/// querer hacer, y es la que convierte este informe en uno del que hay que desconfiar.</para>
/// </summary>
internal sealed class CompanyQueryRepository : ICompanyQueryRepository
{
    private const string LicenciaTransitoTipo = "licencia_transito";
    private const string TransferenciaDominioTipo = "transferencia_dominio";
    private const string ClaveLeasing = "es_leasing";
    private const string ClaveUnilateral = "es_unilateral";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>El motor, amarrado al catálogo de la empresa y a la forma de su fila.</summary>
    private static readonly QueryEngine<CompanyRow> Engine =
        new(CompanyQueryFieldCatalog.Instance, Accessor, DateOf);

    private readonly FlitDbContext _context;
    private readonly SuperAdminTenantScope _superAdminScope;

    public CompanyQueryRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _superAdminScope = new SuperAdminTenantScope(_context);
    }

    // ── Ejecución ─────────────────────────────────────────────────────────────────────────────

    public Task<CompanyQueryResultDto> ExecuteAsync(
        Guid tenantId,
        QueryRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync([tenantId], request, exigirAcotar: false, cancellationToken);

    /// <summary>
    /// Igual que <see cref="ExecuteAsync(Guid, QueryRequest, CancellationToken)"/> pero sobre una
    /// LISTA de compañías a la vez — el motor de SuperAdmin, que corre sin tenant único.
    ///
    /// <para><c>exigirAcotar</c> en <c>true</c> primero CUENTA (en SQL, sin cargar filas) cuántos
    /// trámites habría que traer a memoria para resolver la consulta, y solo la rechaza si ese
    /// conteo real supera <see cref="QueryLimits.MaxUniverso"/> — no una regla fija de días. Si un
    /// filtro de <see cref="CompanyQueryFieldCatalog.Compania"/> ya acota a una o varias compañías,
    /// ese conteo se hace sobre esas nomás. Sin el conteo, «todas las compañías» sin más podría
    /// barrer la plataforma entera y el tope se aplicaría sin <c>ORDER BY</c> — el usuario vería una
    /// porción arbitraria del universo en vez de un error claro pidiendo acotar.</para>
    /// </summary>
    public Task<CompanyQueryResultDto> ExecuteForSuperAdminAsync(
        QueryRequest request,
        CancellationToken cancellationToken = default) =>
        _superAdminScope.ExecuteAsync(
            tenantIds => ExecuteAsync(tenantIds, request, exigirAcotar: true, cancellationToken),
            cancellationToken);

    private async Task<CompanyQueryResultDto> ExecuteAsync(
        IReadOnlyList<Guid> tenantIds,
        QueryRequest request,
        bool exigirAcotar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = CompanyQueryFieldCatalog.Normalize(request.Definition);
        var today = BogotaDays.Today();
        var (desde, hasta) = QueryRangePreset.Resolve(definition.Fechas, today);

        if (exigirAcotar)
        {
            // Un filtro de Compañía es, en el fondo, una lista más corta de tenants: empujarlo aquí
            // (y no dejarlo solo como condición en memoria) es lo que hace que el conteo de abajo —y
            // la carga real, más adelante— reflejen el tamaño de verdad de lo que se pidió.
            tenantIds = NarrowByCompania(tenantIds, definition.Condiciones);

            var universo = await UniversoQuery(tenantIds, definition)
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

            if (universo > QueryLimits.MaxUniverso)
            {
                throw new SuperAdminQueryTooBroadException(universo, QueryLimits.MaxUniverso);
            }
        }

        var rows = await LoadRowsAsync(tenantIds, definition, cancellationToken).ConfigureAwait(false);

        var condiciones = definition.Condiciones
            .Select(c => (Condition: c, Predicate: Engine.BuildPredicate(c)))
            .ToList();

        bool PasaCondiciones(CompanyRow row) => condiciones.All(c => c.Predicate(row));

        var (from, to) = BogotaDays.Range(desde, hasta);
        var matched = rows
            .Where(r => Engine.InRange(r, definition.Fechas.Campo, from, to) && PasaCondiciones(r))
            .ToList();

        // Periodo anterior de IGUAL ancho, pegado al inicio del actual. Sirve para decir «12 % más
        // que antes» sin que el usuario tenga que ejecutar la consulta dos veces.
        var dias = hasta.DayNumber - desde.DayNumber + 1;
        var (prevFrom, prevTo) = BogotaDays.Range(desde.AddDays(-dias), desde.AddDays(-1));
        var anterior = rows
            .Count(r => Engine.InRange(r, definition.Fechas.Campo, prevFrom, prevTo) && PasaCondiciones(r));

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, QueryLimits.MaxPageSize);

        var ordered = Sort(matched, definition.SortBy, definition.Descending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var cobertura = Engine.BuildCoverage(definition, rows, matched, condiciones, from, to);

        var empresas = await ResolveTenantNamesAsync(
            ordered.Select(r => r.Instance.TenantId).Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);

        return new CompanyQueryResultDto(
            Total: matched.Count,
            Page: page,
            PageSize: pageSize,
            Desde: desde,
            Hasta: hasta,
            TotalPeriodoAnterior: anterior,
            Filas: ordered.Select(r => ToDto(r, empresas)).ToList(),
            Cobertura: cobertura);
    }

    // ── Carga ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// El universo sin resolver: qué trámites entrarían a memoria para esta consulta, antes de
    /// cargar una sola fila. La usan tanto el conteo de <see cref="ExecuteAsync"/> como
    /// <see cref="LoadRowsAsync"/> — las dos tienen que estar de acuerdo en qué es «el universo», o
    /// el conteo podría decir que sí cabe y la carga real traer algo distinto.
    /// </summary>
    private IQueryable<ProcedureInstance> UniversoQuery(IReadOnlyList<Guid> tenantIds, QueryDefinition definition)
    {
        var query = _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.DeletedAt == null && tenantIds.Contains(p.TenantId));

        var identificador = definition.Condiciones.FirstOrDefault(c =>
            c.Operator == QueryOperator.EsAlguno && CompanyQueryFieldCatalog.IsIdentifier(c.FieldId));

        if (identificador is not null)
        {
            // La misma normalización que en memoria, escrita también del lado del motor: si aquí se
            // comparara en crudo, pedir «ABC-123» no encontraría «ABC123» y la cobertura lo
            // reportaría como «no existe» — un falso negativo que enseña a desconfiar del aviso.
            // Renuncia al índice de placa a cambio de que las dos comparaciones digan lo mismo.
            var valores = identificador.Values.Select(QueryEngine<CompanyRow>.SinSeparadores).ToList();

            query = identificador.FieldId switch
            {
                CompanyQueryFieldCatalog.Placa => query.Where(p => p.Plate != null
                    && valores.Contains(p.Plate.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
                CompanyQueryFieldCatalog.Vin => query.Where(p => p.Vin != null
                    && valores.Contains(p.Vin.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
                _ => query.Where(p => valores.Contains(
                    p.ReferenceNumber.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
            };
        }

        return query;
    }

    /// <summary>
    /// Reduce la lista de tenants a los que de verdad pueden salir en el resultado, según el filtro
    /// de <see cref="CompanyQueryFieldCatalog.Compania"/> si lo hay — «es alguno» se queda solo con
    /// esas, «no es ninguno» las descarta de la lista. Así el filtro deja de ser solo una condición
    /// en memoria y empieza a acotar también el universo que se cuenta y se carga.
    /// </summary>
    private static IReadOnlyList<Guid> NarrowByCompania(
        IReadOnlyList<Guid> tenantIds, IReadOnlyList<QueryCondition> condiciones)
    {
        var condicion = condiciones.FirstOrDefault(c => c.FieldId == CompanyQueryFieldCatalog.Compania);
        if (condicion is null)
        {
            return tenantIds;
        }

        var valores = condicion.Values
            .Select(v => Guid.TryParse(v, out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        return condicion.Operator switch
        {
            QueryOperator.EsAlguno => tenantIds.Where(valores.Contains).ToList(),
            QueryOperator.NoEsNinguno => tenantIds.Where(id => !valores.Contains(id)).ToList(),
            _ => tenantIds,
        };
    }

    private async Task<List<CompanyRow>> LoadRowsAsync(
        IReadOnlyList<Guid> tenantIds,
        QueryDefinition definition,
        CancellationToken cancellationToken)
    {
        var instances = await UniversoQuery(tenantIds, definition)
            .Select(p => new InstanceRow(
                p.Id,
                p.TenantId,
                p.ReferenceNumber,
                p.Plate,
                p.Vin,
                p.TransitOfficeId,
                p.ProcedureTypeId,
                p.ModalidadEntrada,
                p.Status,
                p.Prioritario,
                p.SubsanacionActiva,
                p.SubsanacionCount,
                p.CompradorNombre,
                p.VendedorNombre,
                p.CreatedByUserId,
                p.CreatedAt,
                p.SubmittedAt,
                p.CompletedAt,
                p.UpdatedAt))
            .Take(QueryLimits.MaxUniverso)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = instances.Select(i => i.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        // Organismos, tipos y personas se resuelven para el UNIVERSO y no solo para la página
        // visible, al contrario que en el organismo. Son catálogos pequeños —los organismos con los
        // que trabaja una gestora, sus tipos publicados, su propio personal—, caben en tres
        // consultas, y sin los nombres «ordenar por organismo» ordenaría por identificador, que no
        // es un orden que nadie pueda leer.
        var oficinaIds = instances.Select(i => i.TransitOfficeId).Where(id => id is not null)
            .Select(id => id!.Value).Distinct().ToList();

        var oficinas = await _context.TransitOffices
            .AsNoTracking()
            .Where(o => oficinaIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken)
            .ConfigureAwait(false);

        var tipoIds = instances.Select(i => i.ProcedureTypeId).Distinct().ToList();
        var tipos = await _context.ProcedureTypes
            .AsNoTracking()
            .Where(t => tipoIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken)
            .ConfigureAwait(false);

        var usuarioIds = instances.Select(i => i.CreatedByUserId).Distinct().ToList();
        var usuarios = await _context.Users
            .AsNoTracking()
            .Where(u => usuarioIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        var history = await _context.ProcedureInstanceStatusHistories
            .AsNoTracking()
            .Where(h => ids.Contains(h.ProcedureInstanceId))
            .Select(h => new EventRow(h.ProcedureInstanceId, h.ToStatus, h.ChangedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var porInstancia = history
            .GroupBy(h => h.InstanceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.ChangedAt).ToList());

        // Solo la prenda VIGENTE cuenta: las filas son versionadas y cada cambio deja la anterior
        // como reemplazada, así que mirarlas todas diría «tiene prenda» de un trámite al que se le
        // quitó. Y una decisión de omitir o de sin_prenda es exactamente lo contrario a tenerla.
        var prendas = await _context.ProcedureInstancePrendas
            .AsNoTracking()
            .Where(p => ids.Contains(p.ProcedureInstanceId)
                && p.Estado == PrendaEstado.Vigente
                && p.Decision != PrendaDecision.SinPrenda
                && p.Decision != PrendaDecision.Omitir)
            .Select(p => new { p.ProcedureInstanceId, p.AcreedorNombre })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var prendaPorInstancia = prendas
            .GroupBy(p => p.ProcedureInstanceId)
            .ToDictionary(g => g.Key, g => g.First().AcreedorNombre);

        var adjuntos = await _context.ProcedureInstanceAttachments
            .AsNoTracking()
            .Where(a => ids.Contains(a.ProcedureInstanceId)
                && (a.Tipo == LicenciaTransitoTipo || a.Tipo == TransferenciaDominioTipo))
            .Select(a => new { a.ProcedureInstanceId, a.Tipo })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var licencias = adjuntos.Where(a => a.Tipo == LicenciaTransitoTipo)
            .Select(a => a.ProcedureInstanceId).ToHashSet();
        var transferencias = adjuntos.Where(a => a.Tipo == TransferenciaDominioTipo)
            .Select(a => a.ProcedureInstanceId).ToHashSet();

        // Transformaciones, leasing y unilateral no tienen entidad propia: son valores del
        // formulario con el texto "true" (las transformaciones, HU #11206).
        var clavesInteresantes = CompanyQueryFieldCatalog.TransformacionOptions
            .Select(o => o.Value)
            .Concat([ClaveLeasing, ClaveUnilateral])
            .ToList();

        var camposMarcados = await _context.ProcedureInstanceFieldValues
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProcedureInstanceId)
                && clavesInteresantes.Contains(f.FieldKey)
                && f.ValueText != null)
            .Select(f => new { f.ProcedureInstanceId, f.FieldKey, Value = f.ValueText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var marcadas = camposMarcados
            .Where(v => EsAfirmativo(v.Value))
            .GroupBy(v => v.ProcedureInstanceId)
            .ToDictionary(g => g.Key, g => g.Select(v => v.FieldKey).ToHashSet(StringComparer.Ordinal));

        // Documento de comprador y vendedor: el nombre ya viene denormalizado en la fila padre, el
        // documento no. Se trae aparte para que «comprador» pueda buscar por los dos — quien tiene
        // la cédula a mano no debería tener que averiguar cómo se escribió el nombre.
        var actores = await _context.ProcedureInstanceActors
            .AsNoTracking()
            .Where(a => ids.Contains(a.ProcedureInstanceId))
            .Select(a => new { a.ProcedureInstanceId, a.ActorType, a.DocumentNumber, a.FullName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var actoresPorInstancia = actores
            .GroupBy(a => a.ProcedureInstanceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var comerciales = await _context.ProcedureInstanceCommercials
            .AsNoTracking()
            .Where(c => ids.Contains(c.ProcedureInstanceId) && c.MetodoPago != null)
            .Select(c => new { c.ProcedureInstanceId, c.MetodoPago })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pagoPorInstancia = comerciales
            .GroupBy(c => c.ProcedureInstanceId)
            .ToDictionary(g => g.Key, g => g.First().MetodoPago);

        var ahora = DateTimeOffset.UtcNow;
        var rows = new List<CompanyRow>(instances.Count);

        foreach (var instance in instances)
        {
            var eventos = porInstancia.GetValueOrDefault(instance.Id) ?? [];
            var radicacion = eventos.FirstOrDefault(e => e.ToStatus == TramiteEstado.Entregado);
            var posteriores = radicacion is null
                ? []
                : eventos.Where(e => e.ChangedAt >= radicacion.ChangedAt).ToList();

            var decision = posteriores.LastOrDefault(e => IsDecision(e.ToStatus));

            var propios = actoresPorInstancia.GetValueOrDefault(instance.Id) ?? [];

            List<string> Datos(string rol) => propios
                .Where(a => string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase))
                .SelectMany(a => new[] { a.FullName, a.DocumentNumber })
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var comprador = Datos("comprador");
            var vendedor = Datos("vendedor");

            if (!string.IsNullOrWhiteSpace(instance.CompradorNombre)
                && !comprador.Contains(instance.CompradorNombre, StringComparer.OrdinalIgnoreCase))
            {
                comprador.Add(instance.CompradorNombre);
            }

            if (!string.IsNullOrWhiteSpace(instance.VendedorNombre)
                && !vendedor.Contains(instance.VendedorNombre, StringComparer.OrdinalIgnoreCase))
            {
                vendedor.Add(instance.VendedorNombre);
            }

            var marcas = marcadas.GetValueOrDefault(instance.Id) ?? [];
            var tienePrenda = prendaPorInstancia.ContainsKey(instance.Id);

            rows.Add(new CompanyRow(
                Instance: instance,
                OrganismoNombre: instance.TransitOfficeId is Guid oid
                    ? oficinas.GetValueOrDefault(oid, "(organismo retirado)")
                    : null,
                TipoNombre: tipos.GetValueOrDefault(instance.ProcedureTypeId, "(tipo retirado)"),
                RadicadoPorNombre: usuarios.GetValueOrDefault(instance.CreatedByUserId, "(usuario retirado)"),
                TienePrenda: tienePrenda,
                AcreedorPrenda: tienePrenda ? prendaPorInstancia[instance.Id] : null,
                TieneLicencia: licencias.Contains(instance.Id),
                Transformaciones: CompanyQueryFieldCatalog.TransformacionOptions
                    .Select(o => o.Value).Where(marcas.Contains).ToList(),
                EsLeasing: marcas.Contains(ClaveLeasing),
                MetodoPago: pagoPorInstancia.GetValueOrDefault(instance.Id),
                TipoTraspaso: ResolveTipoTraspaso(
                    transferencias.Contains(instance.Id), marcas.Contains(ClaveUnilateral)),
                RadicadoEn: radicacion?.ChangedAt,
                // La misma decisión que ya se calculó abajo para «días en organismo», pero solo
                // cuenta si fue aprobación: un rechazado también decide y también cierra, pero
                // nunca tiene esta fecha.
                AprobadoEn: decision?.ToStatus == TramiteEstado.Aprobado ? decision.ChangedAt : null,
                DiasEnOrganismo: radicacion is null
                    ? null
                    : Math.Round(((decision?.ChangedAt ?? ahora) - radicacion.ChangedAt).TotalDays, 2),
                Devoluciones: posteriores.Count(e => e.ToStatus == TramiteEstado.Rechazado),
                Comprador: comprador,
                Vendedor: vendedor));
        }

        return rows;
    }

    /// <summary>
    /// Cómo se transfiere el dominio. Calca la derivación de
    /// <c>analytics.v_procedure_detail_report</c> (ver <c>Sql/Ddl/35-HU10814-…</c>) a propósito: si
    /// las dos no dicen lo mismo, «Reportes Detallados» y esta consulta clasifican el mismo trámite
    /// de forma distinta y no hay forma de saber cuál creer.
    /// </summary>
    private static string ResolveTipoTraspaso(bool tieneTransferencia, bool esUnilateral) =>
        tieneTransferencia ? CompanyQueryFieldCatalog.TraspasoTransferenciaDominio
        : esUnilateral ? CompanyQueryFieldCatalog.TraspasoUnilateral
        : CompanyQueryFieldCatalog.TraspasoBilateral;

    /// <summary>
    /// Qué cuenta como «sí» en un valor de formulario. Los mismos textos que acepta la vista BI:
    /// el formulario ha guardado <c>true</c>, <c>1</c> y <c>si</c> según la versión que lo escribió.
    /// </summary>
    private static bool EsAfirmativo(string? value) =>
        value?.Trim().ToLowerInvariant() is "true" or "1" or "si" or "sí";

    private static bool IsDecision(string status) =>
        status == TramiteEstado.Aprobado || status == TramiteEstado.Rechazado;

    // ── Acceso a los campos ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cada campo se reduce a la lista de textos por los que ese trámite puede coincidir. Es lo
    /// único que el motor necesita saber de esta fila; ver <see cref="QueryEngine{TRow}"/>.
    /// </summary>
    private static Func<CompanyRow, IReadOnlyList<string>> Accessor(string fieldId) => fieldId switch
    {
        CompanyQueryFieldCatalog.Placa => r => Single(r.Instance.Plate),
        CompanyQueryFieldCatalog.Vin => r => Single(r.Instance.Vin),
        CompanyQueryFieldCatalog.Radicado => r => Single(r.Instance.ReferenceNumber),
        CompanyQueryFieldCatalog.Comprador => r => r.Comprador,
        CompanyQueryFieldCatalog.Vendedor => r => r.Vendedor,
        CompanyQueryFieldCatalog.Organismo => r =>
            r.Instance.TransitOfficeId is Guid id ? [id.ToString()] : [],
        CompanyQueryFieldCatalog.TipoTramite => r => [r.Instance.ProcedureTypeId.ToString()],
        CompanyQueryFieldCatalog.Estado => r => [r.Instance.Status],
        CompanyQueryFieldCatalog.RadicadoPor => r => [r.Instance.CreatedByUserId.ToString()],
        CompanyQueryFieldCatalog.Compania => r => [r.Instance.TenantId.ToString()],
        CompanyQueryFieldCatalog.Prioritario => r => [Bool(r.Instance.Prioritario)],
        CompanyQueryFieldCatalog.EnSubsanacion => r => [Bool(r.Instance.SubsanacionActiva)],
        CompanyQueryFieldCatalog.Prenda => r => [Bool(r.TienePrenda)],
        CompanyQueryFieldCatalog.LicenciaTransito => r => [Bool(r.TieneLicencia)],
        CompanyQueryFieldCatalog.Transformaciones => r => r.Transformaciones,
        CompanyQueryFieldCatalog.Leasing => r => [Bool(r.EsLeasing)],
        CompanyQueryFieldCatalog.MetodoPago => r => Single(r.MetodoPago),
        // En matrícula inicial no hay traspaso que clasificar: devolver «bilateral» ahí haría que un
        // filtro por bilateral arrastrara todas las matrículas.
        CompanyQueryFieldCatalog.TipoTraspaso => r =>
            r.Instance.Modalidad == "traspaso" ? [r.TipoTraspaso] : [],
        _ => _ => [],
    };

    /// <summary>
    /// Qué instante mira el rango, según la fecha elegida.
    ///
    /// <para>Devolver <c>null</c> deja la fila fuera, y es lo correcto: un trámite sin entregar no
    /// aparece en una consulta filtrada por fecha de envío, porque la pregunta era qué se entregó en
    /// esas fechas.</para>
    /// </summary>
    private static DateTimeOffset? DateOf(CompanyRow row, string campo) => campo switch
    {
        CompanyQueryDateField.Envio => row.Instance.SubmittedAt,
        CompanyQueryDateField.Cierre => row.Instance.CompletedAt,
        CompanyQueryDateField.Aprobacion => row.AprobadoEn,
        CompanyQueryDateField.Actualizacion => row.Instance.UpdatedAt ?? row.Instance.CreatedAt,
        _ => row.Instance.CreatedAt,
    };

    private static IReadOnlyList<string> Single(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];

    private static string Bool(bool value) => value ? "true" : "false";

    // ── Orden ─────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<CompanyRow> Sort(List<CompanyRow> rows, string? sortBy, bool descending)
    {
        // El desempate por referencia no es cosmético: sin él dos filas con la misma fecha pueden
        // cambiar de sitio entre página y página, y el export encadena páginas — se perderían filas
        // y se repetirían otras sin que nada avisara.
        Func<CompanyRow, IComparable?> key = sortBy switch
        {
            CompanyQuerySort.Enviado => r => r.Instance.SubmittedAt,
            CompanyQuerySort.Cerrado => r => r.Instance.CompletedAt,
            CompanyQuerySort.Actualizado => r => r.Instance.UpdatedAt ?? r.Instance.CreatedAt,
            CompanyQuerySort.Placa => r => r.Instance.Plate,
            CompanyQuerySort.Referencia => r => r.Instance.ReferenceNumber,
            CompanyQuerySort.Estado => r => r.Instance.Status,
            CompanyQuerySort.Organismo => r => r.OrganismoNombre,
            CompanyQuerySort.Tipo => r => r.TipoNombre,
            CompanyQuerySort.Compania => r => r.Instance.TenantId,
            _ => r => r.Instance.CreatedAt,
        };

        var ordered = descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

        return ordered.ThenBy(r => r.Instance.ReferenceNumber, StringComparer.Ordinal);
    }

    // ── Proyección ────────────────────────────────────────────────────────────────────────────

    private static CompanyQueryRowDto ToDto(CompanyRow row, IReadOnlyDictionary<Guid, string> empresas) =>
        new(
            ProcedureInstanceId: row.Instance.Id,
            ReferenceNumber: row.Instance.ReferenceNumber,
            Placa: row.Instance.Plate,
            Vin: row.Instance.Vin,
            TransitOfficeId: row.Instance.TransitOfficeId,
            TransitOfficeName: row.OrganismoNombre,
            CompaniaId: row.Instance.TenantId,
            CompaniaNombre: empresas.GetValueOrDefault(row.Instance.TenantId, "(compañía retirada)"),
            ProcedureTypeId: row.Instance.ProcedureTypeId,
            ProcedureTypeName: row.TipoNombre,
            Modalidad: row.Instance.Modalidad,
            Status: row.Instance.Status,
            Prioritario: row.Instance.Prioritario,
            SubsanacionActiva: row.Instance.SubsanacionActiva,
            SubsanacionCount: row.Instance.SubsanacionCount,
            Comprador: row.Instance.CompradorNombre,
            Vendedor: row.Instance.VendedorNombre,
            TienePrenda: row.TienePrenda,
            AcreedorPrenda: row.AcreedorPrenda,
            TieneLicenciaTransito: row.TieneLicencia,
            Transformaciones: row.Transformaciones,
            EsLeasing: row.EsLeasing,
            MetodoPago: row.MetodoPago,
            TipoTraspaso: row.Instance.Modalidad == "traspaso" ? row.TipoTraspaso : string.Empty,
            RadicadoPor: row.RadicadoPorNombre,
            CreadoEn: row.Instance.CreatedAt,
            EnviadoEn: row.Instance.SubmittedAt,
            CerradoEn: row.Instance.CompletedAt,
            AprobadoEn: row.AprobadoEn,
            ActualizadoEn: row.Instance.UpdatedAt,
            DiasHastaEnvio: row.Instance.SubmittedAt is DateTimeOffset enviado
                ? Math.Round((enviado - row.Instance.CreatedAt).TotalDays, 2)
                : null,
            DiasEnOrganismo: row.DiasEnOrganismo,
            Devoluciones: row.Devoluciones);

    // ── Resolución de nombres ─────────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, string>> ResolveTenantNamesAsync(
        List<Guid> tenantIds,
        CancellationToken cancellationToken) =>
        tenantIds.Count == 0
            ? []
            : await _context.Tenants
                .AsNoTracking()
                .Where(t => tenantIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken)
                .ConfigureAwait(false);

    // ── Catálogo de campos ────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<QueryFieldDto>> GetFieldsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync([tenantId], cancellationToken).ConfigureAwait(false);

        // «Compañía» solo existe para SuperAdmin viendo varias a la vez: para una empresa normal
        // todas sus filas son de sí misma, así que el filtro no dice nada — se le quita de encima
        // del catálogo, no solo se le deja sin opciones.
        return fields.Where(f => f.Id != CompanyQueryFieldCatalog.Compania).ToList();
    }

    /// <summary>
    /// El mismo catálogo, pero con «Compañía» resuelta a las compañías activas de la plataforma
    /// (gestoras, no organismos). Solo lo usa el motor de SuperAdmin.
    /// </summary>
    public Task<IReadOnlyList<QueryFieldDto>> GetFieldsForSuperAdminAsync(
        CancellationToken cancellationToken = default) =>
        _superAdminScope.ExecuteAsync(
            tenantIds => GetFieldsAsync(tenantIds, cancellationToken),
            cancellationToken);

    private async Task<IReadOnlyList<QueryFieldDto>> GetFieldsAsync(
        IReadOnlyList<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        // Las opciones salen de lo que esta empresa TIENE, no de los catálogos completos del
        // sistema. Ofrecer un organismo con el que nunca ha tramitado, o un tipo que no usa, es
        // ofrecer un filtro que solo puede devolver cero — y el usuario lo lee como un fallo.
        var propios = _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.DeletedAt == null && tenantIds.Contains(p.TenantId));

        var oficinaIds = await propios
            .Where(p => p.TransitOfficeId != null)
            .Select(p => p.TransitOfficeId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var organismoOptions = (await _context.TransitOffices
                .AsNoTracking()
                .Where(o => oficinaIds.Contains(o.Id))
                .Select(o => new { o.Id, o.Code, o.Name })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(o => new QueryFieldOptionDto(o.Id.ToString(), $"{o.Code} — {o.Name}"))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tipoIds = await propios
            .Select(p => p.ProcedureTypeId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tipoOptions = (await _context.ProcedureTypes
                .AsNoTracking()
                .Where(t => tipoIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(t => new QueryFieldOptionDto(t.Id.ToString(), t.Name))
            .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usuarioIds = await propios
            .Select(p => p.CreatedByUserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var usuarioOptions = (await _context.Users
                .AsNoTracking()
                .Where(u => usuarioIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(u => new QueryFieldOptionDto(u.Id.ToString(), u.DisplayName))
            .OrderBy(u => u.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // El método de pago es texto libre en la base, así que la lista se saca de lo que se ha
        // escrito de verdad. Una lista fija se quedaría corta con un cliente y sobraría con otro.
        var instanceIds = propios.Select(p => p.Id);
        var pagos = await _context.ProcedureInstanceCommercials
            .AsNoTracking()
            .Where(c => instanceIds.Contains(c.ProcedureInstanceId) && c.MetodoPago != null)
            .Select(c => c.MetodoPago!)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pagoOptions = pagos
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new QueryFieldOptionDto(p, p))
            .ToList();

        // Solo tiene sentido con más de una compañía en juego (el motor de SuperAdmin); para el
        // caso normal de una empresa sola, GetFieldsAsync(Guid,...) descarta el campo después.
        var companiaOptions = tenantIds.Count <= 1
            ? []
            : (await _context.Tenants
                    .AsNoTracking()
                    .Where(t => tenantIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.LegalName })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .OrderBy(t => t.LegalName, StringComparer.OrdinalIgnoreCase)
                .Select(t => new QueryFieldOptionDto(t.Id.ToString(), t.LegalName))
                .ToList();

        return CompanyQueryFieldCatalog.Fields
            .Select(f => f.Id switch
            {
                CompanyQueryFieldCatalog.Organismo => f with { Options = organismoOptions },
                CompanyQueryFieldCatalog.TipoTramite => f with { Options = tipoOptions },
                CompanyQueryFieldCatalog.RadicadoPor => f with { Options = usuarioOptions },
                CompanyQueryFieldCatalog.MetodoPago => f with { Options = pagoOptions },
                CompanyQueryFieldCatalog.Compania => f with { Options = companiaOptions },
                _ => f,
            })
            .ToList();
    }

    // ── Consultas guardadas ───────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SavedQueryDto>> ListSavedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var propias = await _context.CompanySavedQueries
            .AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.UserId == userId)
            .OrderBy(q => q.Nombre)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Las de fábrica van al final: son el punto de partida, no lo que el usuario viene a buscar
        // cuando ya tiene las suyas.
        return
        [
            .. propias.Select(ToSavedDto),
            .. CompanyFactoryQueries.Queries,
        ];
    }

    public async Task<SavedQueryDto> SaveAsync(
        Guid tenantId,
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var definition = CompanyQueryFieldCatalog.Normalize(input.Definition);
        var nombre = input.Nombre.Trim();
        var json = JsonSerializer.Serialize(definition, JsonOptions);

        CompanySavedQueryEntity? entity = null;

        // Guardar sobre una de fábrica es duplicarla, no editarla: las de fábrica no viven en la
        // base y tienen que seguir estando ahí para el siguiente que abra la consola.
        if (id is Guid existingId && !CompanyFactoryQueries.IsFactory(existingId))
        {
            entity = await _context.CompanySavedQueries
                .FirstOrDefaultAsync(
                    q => q.Id == existingId && q.TenantId == tenantId && q.UserId == userId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (entity is null)
        {
            var cuantas = await _context.CompanySavedQueries
                .CountAsync(q => q.TenantId == tenantId && q.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            // El tope se avisa, no se traga en silencio: guardar y que no aparezca en la lista es
            // peor que no dejar guardar.
            if (cuantas >= QueryLimits.MaxConsultasGuardadas)
            {
                throw new SavedQueryLimitException(QueryLimits.MaxConsultasGuardadas);
            }

            entity = new CompanySavedQueryEntity
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                UserId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _context.CompanySavedQueries.Add(entity);
        }
        else
        {
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // El nombre único lo garantiza un índice, pero llegar hasta él devolvería un error de base
        // de datos por algo que es una decisión de producto: dos consultas iguales de nombre harían
        // la lista inservible.
        var repetido = await _context.CompanySavedQueries
            .AnyAsync(
                q => q.TenantId == tenantId
                    && q.UserId == userId
                    && q.Id != entity.Id
                    && q.Nombre.ToLower() == nombre.ToLower(),
                cancellationToken)
            .ConfigureAwait(false);

        if (repetido)
        {
            throw new SavedQueryNameTakenException(nombre);
        }

        entity.Nombre = nombre;
        entity.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion)
            ? null
            : input.Descripcion.Trim();
        entity.Definicion = json;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSavedDto(entity);
    }

    public async Task<bool> DeleteSavedAsync(
        Guid tenantId,
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.CompanySavedQueries
            .FirstOrDefaultAsync(
                q => q.Id == id && q.TenantId == tenantId && q.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        _context.CompanySavedQueries.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static SavedQueryDto ToSavedDto(CompanySavedQueryEntity entity)
    {
        QueryDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<QueryDefinition>(entity.Definicion, JsonOptions);
        }
        catch (JsonException)
        {
            // Una consulta guardada con un JSON que ya no encaja se abre vacía en vez de tumbar la
            // lista entera: el usuario puede volver a armarla, pero solo si llega a verla.
            definition = null;
        }

        return new SavedQueryDto(
            entity.Id,
            entity.Nombre,
            entity.Descripcion,
            DeFabrica: false,
            CompanyQueryFieldCatalog.Normalize(definition),
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    // ── Formas internas ───────────────────────────────────────────────────────────────────────

    private sealed record InstanceRow(
        Guid Id,
        Guid TenantId,
        string ReferenceNumber,
        string? Plate,
        string? Vin,
        Guid? TransitOfficeId,
        Guid ProcedureTypeId,
        string Modalidad,
        string Status,
        bool Prioritario,
        bool SubsanacionActiva,
        int SubsanacionCount,
        string? CompradorNombre,
        string? VendedorNombre,
        Guid CreatedByUserId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SubmittedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record EventRow(Guid InstanceId, string ToStatus, DateTimeOffset ChangedAt);

    /// <summary>Un trámite con TODO lo que cualquier condición pueda preguntarle, ya materializado.</summary>
    private sealed record CompanyRow(
        InstanceRow Instance,
        string? OrganismoNombre,
        string TipoNombre,
        string RadicadoPorNombre,
        bool TienePrenda,
        string? AcreedorPrenda,
        bool TieneLicencia,
        IReadOnlyList<string> Transformaciones,
        bool EsLeasing,
        string? MetodoPago,
        string TipoTraspaso,
        DateTimeOffset? RadicadoEn,
        DateTimeOffset? AprobadoEn,
        double? DiasEnOrganismo,
        int Devoluciones,
        IReadOnlyList<string> Comprador,
        IReadOnlyList<string> Vendedor);
}
