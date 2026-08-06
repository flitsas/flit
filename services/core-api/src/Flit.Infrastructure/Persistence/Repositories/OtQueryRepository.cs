using System.Text.Json;
using Flit.Admin.Domain.OtQueries;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Motor de las consultas del organismo.
///
/// <para><b>Ninguna condición se arma con texto del cliente.</b> Lo que llega son ids del catálogo
/// (<see cref="OtQueryFieldCatalog"/>) y este repositorio decide qué significa cada uno. Es lo que
/// separa un constructor de consultas de un intérprete de SQL ajeno.</para>
///
/// <para><b>Una fila = un trámite.</b> Las condiciones que miran tablas hijas (prenda, adjuntos,
/// transformaciones, documentos de los actores) se resuelven como «existe» y nunca como cruce: un
/// join directo duplicaría la fila del padre por cada hija que coincida y todos los conteos
/// quedarían inflados sin que nada avisara.</para>
///
/// <para><b>Por qué se evalúa en memoria.</b> Mismo intercambio que el resto de reportes OT (ver
/// <see cref="OtMetricsReadRepository"/>): los conjuntos están acotados por organismo y las
/// condiciones interesantes —el estado leído desde el organismo, las devoluciones acumuladas— se
/// derivan del historial, que en SQL exigiría laterales que EF InMemory no soporta y que es donde
/// se cubren estos reportes con pruebas. Aquí además hay una razón propia: el aviso de cobertura
/// necesita saber QUÉ condición dejó fuera cada placa, y eso solo se puede responder evaluándolas
/// una a una. Si el volumen lo pidiera, el salto natural es una vista materializada.</para>
/// </summary>
internal sealed class OtQueryRepository : IOtQueryRepository
{
    /// <summary>Trámites que se traen cuando la consulta no acota por identificador. Tope de cordura.</summary>
    private const int MaxUniverso = 20_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FlitDbContext _context;
    private readonly OtTenantScope _scope;

    public OtQueryRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _scope = new OtTenantScope(_context);
    }

    // ── Ejecución ─────────────────────────────────────────────────────────────────────────────

    public Task<OtQueryResultDto?> ExecuteAsync(
        Guid otTenantId,
        OtQueryRequest request,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _scope.ExecuteAsync<OtQueryResultDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var definition = OtQueryFieldCatalog.Normalize(request.Definition);
                var today = OtTenantScope.TodayInBogota();
                var (desde, hasta) = OtQueryRangePreset.Resolve(definition.Fechas, today);

                var rows = await LoadRowsAsync(
                    transitOfficeId, tenantIds, definition, cancellationToken).ConfigureAwait(false);

                var condiciones = definition.Condiciones
                    .Select(c => (Condition: c, Predicate: BuildPredicate(c)))
                    .ToList();

                bool PasaCondiciones(QueryRow row) => condiciones.All(c => c.Predicate(row));

                var (from, to) = OtTenantScope.DayRange(desde, hasta);
                var matched = rows
                    .Where(r => InRange(r, definition.Fechas.Campo, from, to) && PasaCondiciones(r))
                    .ToList();

                // Periodo anterior de IGUAL ancho, pegado al inicio del actual. Sirve para decir «12 %
                // más que antes» sin que el usuario tenga que ejecutar la consulta dos veces.
                var dias = hasta.DayNumber - desde.DayNumber + 1;
                var (prevFrom, prevTo) = OtTenantScope.DayRange(desde.AddDays(-dias), desde.AddDays(-1));
                var anterior = rows
                    .Count(r => InRange(r, definition.Fechas.Campo, prevFrom, prevTo) && PasaCondiciones(r));

                var page = Math.Max(1, request.Page);
                var pageSize = Math.Clamp(request.PageSize, 1, OtQueryLimits.MaxPageSize);

                var ordered = Sort(matched, definition.SortBy, definition.Descending)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Nombres y causales solo para la página visible: son consultas extra por fila y
                // resolverlas para el universo entero para mostrar cincuenta es trabajo tirado.
                var empresas = await ResolveTenantNamesAsync(
                    ordered.Select(r => r.Instance.TenantId).Distinct().ToList(), cancellationToken)
                    .ConfigureAwait(false);

                var revisores = await ResolveUserNamesAsync(
                    ordered.Select(r => r.DecididoPor).Where(id => id is not null)
                        .Select(id => id!.Value).Distinct().ToList(),
                    cancellationToken).ConfigureAwait(false);

                var causales = await ResolveLastRejectionReasonsAsync(ordered, cancellationToken)
                    .ConfigureAwait(false);

                var cobertura = BuildCoverage(
                    definition, rows, matched, condiciones, from, to);

                return new OtQueryResultDto(
                    Total: matched.Count,
                    Page: page,
                    PageSize: pageSize,
                    Desde: desde,
                    Hasta: hasta,
                    TotalPeriodoAnterior: anterior,
                    Filas: ordered.Select(r => ToDto(r, empresas, revisores, causales)).ToList(),
                    Cobertura: cobertura);
            },
            cancellationToken);
    }

    // ── Cobertura ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Qué pasó con cada placa, VIN o radicado que el usuario pidió por nombre.
    ///
    /// <para>Es el requisito que hace que el resultado se pueda leer sin desconfiar. Si alguien pega
    /// dos placas, marca «tiene LT» y le sale una fila, la pregunta inmediata es si se perdió un
    /// dato. Aquí la respuesta viene con el resultado: o la otra no existe en este organismo, o
    /// existe y la dejó fuera exactamente esta condición.</para>
    ///
    /// <para>Se evalúan las condiciones una a una y en el orden en que el usuario las escribió, y se
    /// reporta la PRIMERA que falla. Reportar todas las que fallan sería más completo y menos útil:
    /// lo accionable es el filtro que hay que aflojar primero.</para>
    /// </summary>
    private static List<OtQueryCoverageItemDto> BuildCoverage(
        OtQueryDefinition definition,
        List<QueryRow> universo,
        List<QueryRow> matched,
        List<(OtQueryCondition Condition, Func<QueryRow, bool> Predicate)> condiciones,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var listados = definition.Condiciones
            .Where(c => c.Operator == OtQueryOperator.EsAlguno && IsIdentifier(c.FieldId))
            .ToList();

        if (listados.Count == 0)
        {
            return [];
        }

        var items = new List<OtQueryCoverageItemDto>();

        foreach (var condicion in listados)
        {
            var accessor = Accessor(condicion.FieldId);
            var normalizer = Normalizer(condicion.FieldId);

            foreach (var pedido in condicion.Values)
            {
                var objetivo = normalizer(pedido);

                bool Coincide(QueryRow row) =>
                    accessor(row).Any(v => normalizer(v) == objetivo);

                if (matched.Any(Coincide))
                {
                    items.Add(new OtQueryCoverageItemDto(
                        condicion.FieldId, pedido, OtQueryCoverageResult.Encontrado, null, null));
                    continue;
                }

                var candidato = universo.FirstOrDefault(Coincide);
                if (candidato is null)
                {
                    items.Add(new OtQueryCoverageItemDto(
                        condicion.FieldId,
                        pedido,
                        OtQueryCoverageResult.NoExiste,
                        null,
                        "No hay ningún trámite con este valor en el organismo."));
                    continue;
                }

                // Existe: alguna condición lo dejó fuera. La fecha se mira primero porque es la que
                // el usuario tiene menos presente — está en la barra de arriba, no entre los chips.
                if (!InRange(candidato, definition.Fechas.Campo, from, to))
                {
                    var etiqueta = OtQueryDateField.Options
                        .FirstOrDefault(o => o.Value == definition.Fechas.Campo)?.Label ?? "la fecha";

                    items.Add(new OtQueryCoverageItemDto(
                        condicion.FieldId,
                        pedido,
                        OtQueryCoverageResult.Excluido,
                        definition.Fechas.Campo,
                        $"Existe, pero su {etiqueta.ToLowerInvariant()} queda fuera del rango."));
                    continue;
                }

                var culpable = condiciones
                    .Where(c => c.Condition.FieldId != condicion.FieldId)
                    .FirstOrDefault(c => !c.Predicate(candidato));

                items.Add(new OtQueryCoverageItemDto(
                    condicion.FieldId,
                    pedido,
                    OtQueryCoverageResult.Excluido,
                    culpable.Condition?.FieldId,
                    culpable.Condition is null
                        // Puede pasar si el mismo valor aparece en varios trámites y ninguno pasa por
                        // razones distintas; decir «otro filtro» es más honesto que señalar uno al azar.
                        ? "Existe, pero ningún trámite con este valor cumple todos los filtros."
                        : $"Existe, pero lo dejó fuera el filtro «{OtQueryFieldCatalog.LabelOf(culpable.Condition.FieldId)}»."));
            }
        }

        return items;
    }

    /// <summary>
    /// Campos por los que tiene sentido rendir cuentas uno a uno. Son los que el usuario escribe o
    /// pega esperando ver cada valor de vuelta; avisar de que «traspaso» no salió en un filtro por
    /// tipo de trámite sería ruido, no información.
    /// </summary>
    private static bool IsIdentifier(string fieldId) => fieldId is
        OtQueryFieldCatalog.Placa or OtQueryFieldCatalog.Vin or OtQueryFieldCatalog.Radicado;

    // ── Carga ─────────────────────────────────────────────────────────────────────────────────

    private async Task<List<QueryRow>> LoadRowsAsync(
        Guid transitOfficeId,
        IReadOnlyList<Guid> tenantIds,
        OtQueryDefinition definition,
        CancellationToken cancellationToken)
    {
        var query = _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.DeletedAt == null
                && p.TransitOfficeId == transitOfficeId
                && tenantIds.Contains(p.TenantId));

        // Solo se empuja a SQL el filtro por identificador, y a propósito. Empujar además el resto
        // (empresa, estado, tipo) sería más rápido y rompería la cobertura: una placa descartada por
        // la empresa nunca llegaría a memoria y se reportaría como «no existe» en vez de «la dejó
        // fuera este filtro», que es justo el error que este informe existe para no cometer.
        var identificador = definition.Condiciones.FirstOrDefault(c =>
            c.Operator == OtQueryOperator.EsAlguno && IsIdentifier(c.FieldId));

        if (identificador is not null)
        {
            // La misma normalización que en memoria, escrita también del lado del motor: si aquí se
            // comparara en crudo, pedir «ABC-123» no encontraría «ABC123» y la cobertura lo
            // reportaría como «no existe» — un falso negativo que enseña a desconfiar del aviso.
            // Renuncia al índice de placa a cambio de que las dos comparaciones digan lo mismo.
            var valores = identificador.Values.Select(Separadores).ToList();

            query = identificador.FieldId switch
            {
                OtQueryFieldCatalog.Placa => query.Where(p => p.Plate != null
                    && valores.Contains(p.Plate.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
                OtQueryFieldCatalog.Vin => query.Where(p => p.Vin != null
                    && valores.Contains(p.Vin.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
                _ => query.Where(p => valores.Contains(
                    p.ReferenceNumber.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
            };
        }

        var instances = await query
            .Select(p => new InstanceRow(
                p.Id,
                p.ReferenceNumber,
                p.Plate,
                p.Vin,
                p.TenantId,
                p.ModalidadEntrada,
                p.Status,
                p.PlateFlowStatus,
                p.Prioritario,
                p.SubsanacionActiva,
                p.IsPaused,
                p.CompradorNombre,
                p.VendedorNombre,
                p.CreatedAt,
                p.UpdatedAt))
            .Take(MaxUniverso)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = instances.Select(i => i.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var history = await _context.ProcedureInstanceStatusHistories
            .AsNoTracking()
            .Where(h => ids.Contains(h.ProcedureInstanceId))
            .Select(h => new EventRow(h.Id, h.ProcedureInstanceId, h.ToStatus, h.ChangedAt, h.ChangedBy))
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

        var conLicencia = await _context.ProcedureInstanceAttachments
            .AsNoTracking()
            .Where(a => ids.Contains(a.ProcedureInstanceId) && a.Tipo == LicenciaTransitoTipo)
            .Select(a => a.ProcedureInstanceId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var licencias = conLicencia.ToHashSet();

        // Las transformaciones no tienen entidad propia: son valores del formulario con el texto
        // "true" (HU #11206, las mismas claves que componen el objeto del mandato).
        var clavesTransformacion = OtQueryFieldCatalog.TransformacionOptions
            .Select(o => o.Value).ToList();

        var transformaciones = await _context.ProcedureInstanceFieldValues
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProcedureInstanceId)
                && clavesTransformacion.Contains(f.FieldKey)
                && f.ValueText != null)
            .Select(f => new { f.ProcedureInstanceId, f.FieldKey, Value = f.ValueText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var transformacionPorInstancia = transformaciones
            .Where(t => string.Equals(t.Value!.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.ProcedureInstanceId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(t => t.FieldKey).Distinct().ToList());

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

        var ahora = DateTimeOffset.UtcNow;
        var rows = new List<QueryRow>(instances.Count);

        foreach (var instance in instances)
        {
            var eventos = porInstancia.GetValueOrDefault(instance.Id) ?? [];

            var radicacion = eventos.FirstOrDefault(e => e.ToStatus == TramiteEstado.Entregado);
            var posteriores = radicacion is null
                ? []
                : eventos.Where(e => e.ChangedAt >= radicacion.ChangedAt).ToList();

            var decision = posteriores.LastOrDefault(e => IsDecision(e.ToStatus));
            var ultimaRadicacion = posteriores
                .LastOrDefault(e => e.ToStatus == TramiteEstado.Entregado
                    && (decision is null || e.ChangedAt <= decision.ChangedAt));

            var horas = decision is not null && ultimaRadicacion is not null
                ? Math.Round((decision.ChangedAt - ultimaRadicacion.ChangedAt).TotalHours, 2)
                : (double?)null;

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

            var tienePrenda = prendaPorInstancia.ContainsKey(instance.Id);

            rows.Add(new QueryRow(
                Instance: instance,
                EstadoOt: OtEstadoResolver.Resolve(
                    instance.Status, instance.SubsanacionActiva, instance.IsPaused, instance.PlateFlowStatus),
                RadicadoEn: radicacion?.ChangedAt,
                UltimaRadicacionEn: ultimaRadicacion?.ChangedAt,
                DecididoEn: decision?.ChangedAt,
                DecididoPor: decision?.ChangedBy,
                UltimoRechazoEventId: posteriores
                    .LastOrDefault(e => e.ToStatus == TramiteEstado.Rechazado)?.Id,
                HorasHastaDecision: horas,
                DiasEnOrganismo: radicacion is null
                    ? null
                    : Math.Round(((decision?.ChangedAt ?? ahora) - radicacion.ChangedAt).TotalDays, 2),
                Devoluciones: posteriores.Count(e => e.ToStatus == TramiteEstado.Rechazado),
                TienePrenda: tienePrenda,
                AcreedorPrenda: tienePrenda ? prendaPorInstancia[instance.Id] : null,
                TieneLicencia: licencias.Contains(instance.Id),
                Transformaciones: transformacionPorInstancia.GetValueOrDefault(instance.Id) ?? [],
                Comprador: comprador,
                Vendedor: vendedor));
        }

        return rows;
    }

    private const string LicenciaTransitoTipo = "licencia_transito";

    // ── Predicados ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cada campo se reduce a la MISMA forma: la lista de textos por los que ese trámite puede
    /// coincidir. Un booleano devuelve <c>["true"]</c>, un comprador devuelve su nombre y su
    /// documento, un trámite sin transformaciones devuelve la lista vacía.
    ///
    /// <para>Reducirlo todo a esa forma es lo que deja los operadores en cuatro y hace que «no es
    /// ninguna» funcione igual sobre una empresa que sobre las transformaciones — sin un caso
    /// especial por campo, que es donde estos motores acumulan incoherencias.</para>
    /// </summary>
    private static Func<QueryRow, IReadOnlyList<string>> Accessor(string fieldId) => fieldId switch
    {
        OtQueryFieldCatalog.Placa => r => Single(r.Instance.Plate),
        OtQueryFieldCatalog.Vin => r => Single(r.Instance.Vin),
        OtQueryFieldCatalog.Radicado => r => Single(r.Instance.ReferenceNumber),
        OtQueryFieldCatalog.Comprador => r => r.Comprador,
        OtQueryFieldCatalog.Vendedor => r => r.Vendedor,
        OtQueryFieldCatalog.Empresa => r => [r.Instance.TenantId.ToString()],
        OtQueryFieldCatalog.TipoTramite => r => [r.Instance.Modalidad],
        OtQueryFieldCatalog.Estado => r => [r.EstadoOt],
        OtQueryFieldCatalog.Revisor => r => r.DecididoPor is Guid id ? [id.ToString()] : [],
        OtQueryFieldCatalog.Prioritario => r => [Bool(r.Instance.Prioritario)],
        OtQueryFieldCatalog.Prenda => r => [Bool(r.TienePrenda)],
        OtQueryFieldCatalog.LicenciaTransito => r => [Bool(r.TieneLicencia)],
        OtQueryFieldCatalog.Transformaciones => r => r.Transformaciones,
        _ => _ => [],
    };

    /// <summary>
    /// Cómo se compara cada campo. Los identificadores ignoran guiones, puntos y espacios porque una
    /// lista pegada desde Excel trae «ABC-123» tan a menudo como «ABC123», y que la consulta dependa
    /// de eso convertiría el aviso de cobertura en una lista de falsos «no existe».
    ///
    /// <para>Esta regla está escrita DOS veces —aquí y como expresión sobre el motor en
    /// <see cref="LoadRowsAsync"/>— y las dos tienen que decir lo mismo. Si se cambia una hay que
    /// cambiar la otra: una consulta que filtra distinto según dónde se evalúe pierde filas en
    /// silencio.</para>
    /// </summary>
    private static Func<string, string> Normalizer(string fieldId) => fieldId switch
    {
        OtQueryFieldCatalog.Placa or OtQueryFieldCatalog.Vin or OtQueryFieldCatalog.Radicado =>
            Separadores,
        _ => v => v.Trim().ToUpperInvariant(),
    };

    private static Func<QueryRow, bool> BuildPredicate(OtQueryCondition condition)
    {
        var accessor = Accessor(condition.FieldId);
        var normalize = Normalizer(condition.FieldId);
        var objetivos = condition.Values.Select(normalize).ToHashSet(StringComparer.Ordinal);

        return condition.Operator switch
        {
            OtQueryOperator.EsAlguno => row =>
                accessor(row).Any(v => objetivos.Contains(normalize(v))),
            OtQueryOperator.NoEsNinguno => row =>
                !accessor(row).Any(v => objetivos.Contains(normalize(v))),
            OtQueryOperator.Contiene => row =>
                accessor(row).Any(v => normalize(v).Contains(objetivos.First(), StringComparison.Ordinal)),
            OtQueryOperator.EstaVacio => row =>
                accessor(row).All(string.IsNullOrWhiteSpace),
            OtQueryOperator.NoEstaVacio => row =>
                accessor(row).Any(v => !string.IsNullOrWhiteSpace(v)),
            _ => _ => true,
        };
    }

    /// <summary>
    /// ¿Cae el trámite en el rango, según la fecha elegida?
    ///
    /// <para>Sin esa fecha, NO cae. Un trámite sin decidir no aparece en una consulta filtrada por
    /// fecha de decisión, y es lo correcto: la pregunta era qué se decidió en esas fechas. La
    /// alternativa —colarlos— haría que el mismo filtro significara cosas distintas según la fila.</para>
    /// </summary>
    private static bool InRange(QueryRow row, string campo, DateTimeOffset from, DateTimeOffset to)
    {
        var fecha = campo switch
        {
            OtQueryDateField.Decision => row.DecididoEn,
            OtQueryDateField.Actualizacion => row.Instance.UpdatedAt ?? row.Instance.CreatedAt,
            _ => row.RadicadoEn,
        };

        return fecha is not null && fecha >= from && fecha <= to;
    }

    private static IReadOnlyList<string> Single(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Quita los separadores con los que la gente escribe placas y radicados, y sube a mayúsculas.</summary>
    private static string Separadores(string value) => value
        .ToUpperInvariant()
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace(".", string.Empty, StringComparison.Ordinal);

    private static bool IsDecision(string status) =>
        status == TramiteEstado.Aprobado || status == TramiteEstado.Rechazado;

    // ── Orden ─────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<QueryRow> Sort(List<QueryRow> rows, string? sortBy, bool descending)
    {
        // El desempate por referencia no es cosmético: sin él dos filas con la misma fecha pueden
        // cambiar de sitio entre página y página, y el export encadena páginas — se perderían filas
        // y se repetirían otras sin que nada avisara.
        Func<QueryRow, IComparable?> key = sortBy switch
        {
            OtQuerySort.Creado => r => r.Instance.CreatedAt,
            OtQuerySort.Decidido => r => r.DecididoEn,
            OtQuerySort.Actualizado => r => r.Instance.UpdatedAt ?? r.Instance.CreatedAt,
            OtQuerySort.Placa => r => r.Instance.Plate,
            OtQuerySort.Empresa => r => r.Instance.TenantId,
            OtQuerySort.Referencia => r => r.Instance.ReferenceNumber,
            OtQuerySort.Estado => r => r.EstadoOt,
            OtQuerySort.Dias => r => r.DiasEnOrganismo,
            _ => r => r.RadicadoEn,
        };

        var ordered = descending
            ? rows.OrderByDescending(key)
            : rows.OrderBy(key);

        return ordered.ThenBy(r => r.Instance.ReferenceNumber, StringComparer.Ordinal);
    }

    // ── Proyección ────────────────────────────────────────────────────────────────────────────

    private static OtQueryRowDto ToDto(
        QueryRow row,
        Dictionary<Guid, string> empresas,
        Dictionary<Guid, string> revisores,
        Dictionary<Guid, List<string>> causales) =>
        new(
            ProcedureInstanceId: row.Instance.Id,
            ReferenceNumber: row.Instance.ReferenceNumber,
            Placa: row.Instance.Plate,
            Vin: row.Instance.Vin,
            ClientTenantId: row.Instance.TenantId,
            ClientTenantName: empresas.GetValueOrDefault(row.Instance.TenantId, "(empresa retirada)"),
            Modalidad: row.Instance.Modalidad,
            Status: row.Instance.Status,
            EstadoOt: row.EstadoOt,
            Prioritario: row.Instance.Prioritario,
            SubsanacionActiva: row.Instance.SubsanacionActiva,
            Comprador: row.Instance.CompradorNombre,
            Vendedor: row.Instance.VendedorNombre,
            TienePrenda: row.TienePrenda,
            AcreedorPrenda: row.AcreedorPrenda,
            TieneLicenciaTransito: row.TieneLicencia,
            Transformaciones: row.Transformaciones,
            CreadoEn: row.Instance.CreatedAt,
            RadicadoEn: row.RadicadoEn,
            UltimaRadicacionEn: row.UltimaRadicacionEn,
            DecididoEn: row.DecididoEn,
            ActualizadoEn: row.Instance.UpdatedAt,
            DecididoPor: row.DecididoPor is Guid id ? revisores.GetValueOrDefault(id) : null,
            HorasHastaDecision: row.HorasHastaDecision,
            DiasEnOrganismo: row.DiasEnOrganismo,
            Devoluciones: row.Devoluciones,
            CausalesUltimoRechazo: row.UltimoRechazoEventId is Guid ev
                ? causales.GetValueOrDefault(ev, [])
                : []);

    // ── Catálogo de campos ────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<OtQueryFieldDto>?> GetFieldsAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        _scope.ExecuteAsync<IReadOnlyList<OtQueryFieldDto>>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var empresas = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => tenantIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.LegalName })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var empresaOptions = empresas
                    .OrderBy(t => t.LegalName, StringComparer.OrdinalIgnoreCase)
                    .Select(t => new OtQueryFieldOptionDto(t.Id.ToString(), t.LegalName))
                    .ToList();

                var instanceIds = _context.ProcedureInstances
                    .AsNoTracking()
                    .Where(p => p.DeletedAt == null
                        && p.TransitOfficeId == transitOfficeId
                        && tenantIds.Contains(p.TenantId))
                    .Select(p => p.Id);

                var revisorIds = await _context.ProcedureInstanceStatusHistories
                    .AsNoTracking()
                    .Where(h => instanceIds.Contains(h.ProcedureInstanceId)
                        && h.ChangedBy != null
                        && (h.ToStatus == TramiteEstado.Aprobado || h.ToStatus == TramiteEstado.Rechazado))
                    .Select(h => h.ChangedBy!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var nombres = await ResolveUserNamesAsync(revisorIds, cancellationToken)
                    .ConfigureAwait(false);

                var revisorOptions = revisorIds
                    .Select(id => new OtQueryFieldOptionDto(
                        id.ToString(), nombres.GetValueOrDefault(id, "(usuario retirado)")))
                    .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return OtQueryFieldCatalog.Fields
                    .Select(f => f.Id switch
                    {
                        OtQueryFieldCatalog.Empresa => f with { Options = empresaOptions },
                        OtQueryFieldCatalog.Revisor => f with { Options = revisorOptions },
                        _ => f,
                    })
                    .ToList();
            },
            cancellationToken);

    // ── Consultas guardadas ───────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<OtSavedQueryDto>?> ListSavedAsync(
        Guid otTenantId,
        Guid userId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        _scope.ExecuteAsync<IReadOnlyList<OtSavedQueryDto>>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, _) =>
            {
                var propias = await _context.OtSavedQueries
                    .AsNoTracking()
                    .Where(q => q.TransitOfficeId == transitOfficeId && q.UserId == userId)
                    .OrderBy(q => q.Nombre)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Las de fábrica van al final: son el punto de partida, no lo que el usuario viene a
                // buscar cuando ya tiene las suyas.
                return
                [
                    .. propias.Select(ToSavedDto),
                    .. OtFactoryQueries.Queries,
                ];
            },
            cancellationToken);

    public Task<OtSavedQueryDto?> SaveAsync(
        Guid otTenantId,
        Guid userId,
        Guid? id,
        OtSavedQueryInput input,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return _scope.ExecuteAsync<OtSavedQueryDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, _) =>
            {
                var definition = OtQueryFieldCatalog.Normalize(input.Definition);
                var nombre = input.Nombre.Trim();
                var json = JsonSerializer.Serialize(definition, JsonOptions);

                OtSavedQueryEntity? entity = null;

                // Guardar sobre una de fábrica es duplicarla, no editarla: las de fábrica no viven en
                // la base y tienen que seguir estando ahí para el siguiente que abra la consola.
                if (id is Guid existingId && !OtFactoryQueries.IsFactory(existingId))
                {
                    entity = await _context.OtSavedQueries
                        .FirstOrDefaultAsync(
                            q => q.Id == existingId
                                && q.TransitOfficeId == transitOfficeId
                                && q.UserId == userId,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (entity is null)
                {
                    var cuantas = await _context.OtSavedQueries
                        .CountAsync(
                            q => q.TransitOfficeId == transitOfficeId && q.UserId == userId,
                            cancellationToken)
                        .ConfigureAwait(false);

                    // El tope se avisa, no se traga en silencio: guardar y que no aparezca en la
                    // lista es peor que no dejar guardar.
                    if (cuantas >= OtQueryLimits.MaxConsultasGuardadas)
                    {
                        throw new OtSavedQueryLimitException(OtQueryLimits.MaxConsultasGuardadas);
                    }

                    entity = new OtSavedQueryEntity
                    {
                        Id = Guid.CreateVersion7(),
                        TransitOfficeId = transitOfficeId,
                        UserId = userId,
                        CreatedAt = DateTimeOffset.UtcNow,
                    };

                    _context.OtSavedQueries.Add(entity);
                }
                else
                {
                    entity.UpdatedAt = DateTimeOffset.UtcNow;
                }

                // El nombre único lo garantiza un índice, pero llegar hasta él devolvería un error de
                // base de datos por algo que es una decisión de producto: dos consultas iguales de
                // nombre harían la lista inservible.
                var repetido = await _context.OtSavedQueries
                    .AnyAsync(
                        q => q.TransitOfficeId == transitOfficeId
                            && q.UserId == userId
                            && q.Id != entity.Id
                            && q.Nombre.ToLower() == nombre.ToLower(),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (repetido)
                {
                    throw new OtSavedQueryNameTakenException(nombre);
                }

                entity.Nombre = nombre;
                entity.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion)
                    ? null
                    : input.Descripcion.Trim();
                entity.Definicion = json;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return ToSavedDto(entity);
            },
            cancellationToken);
    }

    public async Task<bool> DeleteSavedAsync(
        Guid otTenantId,
        Guid userId,
        Guid id,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _scope.ExecuteAsync<string>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, _) =>
            {
                var entity = await _context.OtSavedQueries
                    .FirstOrDefaultAsync(
                        q => q.Id == id && q.TransitOfficeId == transitOfficeId && q.UserId == userId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return "no";
                }

                _context.OtSavedQueries.Remove(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return "si";
            },
            cancellationToken).ConfigureAwait(false);

        return result == "si";
    }

    private static OtSavedQueryDto ToSavedDto(OtSavedQueryEntity entity)
    {
        OtQueryDefinition? definition = null;
        try
        {
            definition = JsonSerializer.Deserialize<OtQueryDefinition>(entity.Definicion, JsonOptions);
        }
        catch (JsonException)
        {
            // Una consulta guardada con un JSON que ya no encaja se abre vacía en vez de tumbar la
            // lista entera: el usuario puede volver a armarla, pero solo si llega a verla.
            definition = null;
        }

        return new OtSavedQueryDto(
            entity.Id,
            entity.Nombre,
            entity.Descripcion,
            DeFabrica: false,
            OtQueryFieldCatalog.Normalize(definition),
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    // ── Resolución de nombres ─────────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, string>> ResolveUserNamesAsync(
        List<Guid> userIds,
        CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? []
            : await _context.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
                .ConfigureAwait(false);

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

    private async Task<Dictionary<Guid, List<string>>> ResolveLastRejectionReasonsAsync(
        List<QueryRow> rows,
        CancellationToken cancellationToken)
    {
        var eventIds = rows
            .Select(r => r.UltimoRechazoEventId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (eventIds.Count == 0)
        {
            return [];
        }

        var marks = await _context.ProcedureInstanceRejectionReasons
            .AsNoTracking()
            .Where(r => r.StatusHistoryId != null && eventIds.Contains(r.StatusHistoryId!.Value))
            .Select(r => new { EventId = r.StatusHistoryId!.Value, r.RejectionReasonId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reasonIds = marks.Select(m => m.RejectionReasonId).Distinct().ToList();
        var catalog = await _context.RejectionReasons
            .AsNoTracking()
            .Where(r => reasonIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Description })
            .ToDictionaryAsync(r => r.Id, r => r.Description, cancellationToken)
            .ConfigureAwait(false);

        return marks
            .GroupBy(m => m.EventId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(m => catalog.GetValueOrDefault(m.RejectionReasonId, "(causal retirada)"))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    // ── Formas internas ───────────────────────────────────────────────────────────────────────

    private sealed record InstanceRow(
        Guid Id,
        string ReferenceNumber,
        string? Plate,
        string? Vin,
        Guid TenantId,
        string Modalidad,
        string Status,
        string? PlateFlowStatus,
        bool Prioritario,
        bool SubsanacionActiva,
        bool IsPaused,
        string? CompradorNombre,
        string? VendedorNombre,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record EventRow(
        Guid Id,
        Guid InstanceId,
        string ToStatus,
        DateTimeOffset ChangedAt,
        Guid? ChangedBy);

    /// <summary>Un trámite con TODO lo que cualquier condición pueda preguntarle, ya materializado.</summary>
    private sealed record QueryRow(
        InstanceRow Instance,
        string EstadoOt,
        DateTimeOffset? RadicadoEn,
        DateTimeOffset? UltimaRadicacionEn,
        DateTimeOffset? DecididoEn,
        Guid? DecididoPor,
        Guid? UltimoRechazoEventId,
        double? HorasHastaDecision,
        double? DiasEnOrganismo,
        int Devoluciones,
        bool TienePrenda,
        string? AcreedorPrenda,
        bool TieneLicencia,
        IReadOnlyList<string> Transformaciones,
        IReadOnlyList<string> Comprador,
        IReadOnlyList<string> Vendedor);
}
