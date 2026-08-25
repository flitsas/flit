using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Flit.Analytics.Application.IctQueries;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Queries.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Motor de las consultas sobre pre-trámites de ICT: el gemelo de <see cref="CompanyQueryRepository"/>
/// para el pipeline de Integración con Terceros.
///
/// <para><b>Cross-schema, no cross-servicio.</b> <c>ict.*</c> es propiedad del microservicio
/// core-ict, pero vive en el MISMO Postgres que core-api. Este repositorio lo lee con SQL crudo
/// sobre la conexión de <see cref="FlitDbContext"/> — exactamente el patrón que ya usa
/// <c>AlertMetricsReadRepository</c> para las métricas de alerta <c>ict_*</c> — porque ninguna tabla
/// <c>ict.*</c> está mapeada en un <c>DbContext</c> de core-api ni debería estarlo: hacerlo sería
/// tratar el schema de otro servicio como propio. El GUC <c>app.current_tenant_id</c> se fija dentro
/// de una transacción antes de cada consulta por RLS, y además se filtra explícitamente por
/// <c>tenant_id</c> — defensa en profundidad, mismo criterio que el resto del proyecto.</para>
///
/// <para><b>Ninguna condición se arma con texto del cliente.</b> Lo que llega son ids del catálogo
/// (<see cref="IctQueryFieldCatalog"/>) y este repositorio decide qué significa cada uno.</para>
///
/// <para><b>Solo se empuja a SQL el filtro por identificador</b> (placa, VIN, radicado), igual que en
/// empresa y OT y por la misma razón: empujar además estado o tipo rompería el aviso de cobertura,
/// porque un valor descartado por otro filtro nunca llegaría a memoria y se reportaría como «no
/// existe» en vez de «lo dejó fuera ese filtro».</para>
/// </summary>
internal sealed class IctQueryRepository : IIctQueryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>El motor, amarrado al catálogo de ICT y a la forma de su fila.</summary>
    private static readonly QueryEngine<IctRow> Engine = new(IctQueryFieldCatalog.Instance, Accessor, DateOf);

    private readonly FlitDbContext _context;

    public IctQueryRepository(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    // ── Ejecución ─────────────────────────────────────────────────────────────────────────────

    public async Task<IctQueryResultDto> ExecuteAsync(
        Guid tenantId,
        QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = IctQueryFieldCatalog.Normalize(request.Definition);
        var today = BogotaDays.Today();
        var (desde, hasta) = QueryRangePreset.Resolve(definition.Fechas, today);

        var rows = await LoadRowsAsync(tenantId, definition, cancellationToken).ConfigureAwait(false);

        var condiciones = definition.Condiciones
            .Select(c => (Condition: c, Predicate: Engine.BuildPredicate(c)))
            .ToList();

        bool PasaCondiciones(IctRow row) => condiciones.All(c => c.Predicate(row));

        var (from, to) = BogotaDays.Range(desde, hasta);
        var matched = rows
            .Where(r => Engine.InRange(r, definition.Fechas.Campo, from, to) && PasaCondiciones(r))
            .ToList();

        // Periodo anterior de IGUAL ancho, pegado al inicio del actual — «12 % más que antes» sin
        // ejecutar la consulta dos veces.
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

        var tenantNombre = await ResolveTenantNameAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return new IctQueryResultDto(
            Total: matched.Count,
            Page: page,
            PageSize: pageSize,
            Desde: desde,
            Hasta: hasta,
            TotalPeriodoAnterior: anterior,
            Filas: ordered.Select(r => ToDto(r, tenantNombre)).ToList(),
            Cobertura: cobertura);
    }

    // ── Carga ─────────────────────────────────────────────────────────────────────────────────

    private const string BaseSelect = """
        SELECT m.id, m.transaction_number, m.manager_id_transaction, m.plate, m.vin,
               m.transaction_type, tm.description AS tipo_nombre, tm.family AS tipo_familia,
               m.process_status_id, m.procedure_instance_id, m.priority,
               m.traffic_secretary_code, m.business_comments_validation,
               m.external_comments_validation, m.business_date_validation,
               m.external_date_validation, m.created_at,
               m.created_by, ic.username AS cliente_username
        FROM ict.external_integration_master m
        LEFT JOIN ict.procedure_type_mapping tm ON tm.external_transaction_type = m.transaction_type
        LEFT JOIN ict.integration_clients ic ON ic.id = m.created_by
        WHERE m.tenant_id = @tenant AND m.deleted_at IS NULL
        """;

    private async Task<List<IctRow>> LoadRowsAsync(
        Guid tenantId, QueryDefinition definition, CancellationToken cancellationToken)
    {
        var identificador = definition.Condiciones.FirstOrDefault(c =>
            c.Operator == QueryOperator.EsAlguno && IctQueryFieldCatalog.IsIdentifier(c.FieldId));

        var sql = BaseSelect;
        List<string>? valores = null;

        if (identificador is not null)
        {
            // Misma normalización que en memoria, escrita también del lado de SQL: si aquí se
            // comparara en crudo, pedir «ABC-123» no encontraría «ABC123» y la cobertura lo
            // reportaría como «no existe» — un falso negativo que enseña a desconfiar del aviso.
            valores = identificador.Values.Select(QueryEngine<IctRow>.SinSeparadores).ToList();

            var columna = identificador.FieldId switch
            {
                IctQueryFieldCatalog.Placa => "m.plate",
                IctQueryFieldCatalog.Vin => "m.vin",
                _ => "m.manager_id_transaction",
            };

            sql += $" AND upper(replace(replace(replace({columna}, '-', ''), ' ', ''), '.', '')) = ANY(@valores)";
        }

        sql += " ORDER BY m.created_at DESC LIMIT @limite";

        var rows = new List<IctRow>();

        await WithTenantAsync(tenantId, async (cmd, ct) =>
        {
            cmd.CommandText = sql;
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "limite", QueryLimits.MaxUniverso);
            if (valores is not null)
            {
                AddParam(cmd, "valores", valores.ToArray());
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(ReadRow(reader, tenantId));
            }
        }, cancellationToken).ConfigureAwait(false);

        return rows;
    }

    private static IctRow ReadRow(DbDataReader reader, Guid tenantId)
    {
        var processStatusId = reader.GetInt16(reader.GetOrdinal("process_status_id"));
        var procedureInstanceId = GetGuidOrNull(reader, "procedure_instance_id");
        var businessDateValidation = GetDateTimeOrNull(reader, "business_date_validation");

        var estado = ResolveEstado(processStatusId, procedureInstanceId is not null, businessDateValidation);

        return new IctRow(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            TenantId: tenantId,
            TransactionNumber: reader.GetInt64(reader.GetOrdinal("transaction_number")),
            Radicado: GetStringOrNull(reader, "manager_id_transaction"),
            Placa: GetStringOrNull(reader, "plate"),
            Vin: GetStringOrNull(reader, "vin"),
            TipoTramiteId: reader.GetInt32(reader.GetOrdinal("transaction_type")),
            TipoTramiteNombre: GetStringOrNull(reader, "tipo_nombre"),
            TipoTramiteFamilia: GetStringOrNull(reader, "tipo_familia"),
            Estado: estado,
            TieneNovedades: processStatusId == 4,
            TieneBorrador: procedureInstanceId is not null,
            ProcedureInstanceId: procedureInstanceId,
            Prioritario: reader.GetBoolean(reader.GetOrdinal("priority")),
            Secretaria: GetStringOrNull(reader, "traffic_secretary_code"),
            ClienteIntegracionId: GetGuidOrNull(reader, "created_by"),
            ClienteIntegracionNombre: GetStringOrNull(reader, "cliente_username"),
            Comentarios: CombineComentarios(
                GetStringOrNull(reader, "business_comments_validation"),
                GetStringOrNull(reader, "external_comments_validation")),
            RegistradoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            ValidacionNegocioEn: businessDateValidation,
            ValidacionExternaEn: GetDateTimeOrNull(reader, "external_date_validation"));
    }

    /// <summary>
    /// Concatena las dos bitácoras de comentarios (negocio y externa) que llena el SP de core-ict.
    /// No hay una taxonomía de códigos de rechazo/novedad detrás: es texto libre, así que el único
    /// filtro posible sobre este campo es «contiene» — no «es alguno de estos valores».
    /// </summary>
    internal static string? CombineComentarios(string? negocio, string? externa)
    {
        var partes = new[] { negocio, externa }.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return partes.Count == 0 ? null : string.Join(" · ", partes);
    }

    /// <summary>
    /// El estado del pre-trámite en el pipeline de ICT, tal y como lo ve un usuario — reimplementa,
    /// de solo lectura, la regla de <c>IctEstado.Map</c> de core-ict (no se puede depender de
    /// core-ict desde core-api).
    ///
    /// <para><b>Regla de precedencia:</b> tener <c>procedure_instance_id</c> gana SIEMPRE, sin
    /// importar qué diga <c>process_status_id</c> — un pre-trámite puede seguir en 1/2/4 en ICT
    /// mientras el borrador ya existe en FLIT, y lo que le importa a quien consulta es que YA
    /// generó el trámite.</para>
    /// </summary>
    internal static string ResolveEstado(short processStatusId, bool tieneBorrador, DateTimeOffset? businessDateValidation)
    {
        if (tieneBorrador)
        {
            return "borrador_creado";
        }

        return processStatusId switch
        {
            1 => "recibido",
            2 => businessDateValidation is null ? "en_validacion_negocio" : "en_validacion_externa",
            4 => "con_novedades",
            _ => "anulado",
        };
    }

    // ── Acceso a los campos ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cada campo se reduce a la lista de textos por los que ese pre-trámite puede coincidir. Es lo
    /// único que el motor necesita saber de esta fila; ver <see cref="QueryEngine{TRow}"/>.
    /// </summary>
    private static Func<IctRow, IReadOnlyList<string>> Accessor(string fieldId) => fieldId switch
    {
        IctQueryFieldCatalog.Placa => r => Single(r.Placa),
        IctQueryFieldCatalog.Vin => r => Single(r.Vin),
        IctQueryFieldCatalog.Radicado => r => Single(r.Radicado),
        IctQueryFieldCatalog.NumeroTransaccion => r => [r.TransactionNumber.ToString()],
        IctQueryFieldCatalog.Comentarios => r => Single(r.Comentarios),
        // El tipo concreto y su familia: el desplegable ofrece los dos niveles en el mismo campo,
        // así que «Toda la familia: Traspasos» tiene que casar con un traspaso unilateral.
        IctQueryFieldCatalog.TipoTramite => r =>
            r.TipoTramiteFamilia is { Length: > 0 } familia
                ? [r.TipoTramiteId.ToString(CultureInfo.InvariantCulture), familia]
                : [r.TipoTramiteId.ToString(CultureInfo.InvariantCulture)],
        IctQueryFieldCatalog.Estado => r => [r.Estado],
        IctQueryFieldCatalog.Secretaria => r => Single(r.Secretaria),
        IctQueryFieldCatalog.ClienteIntegracion => r =>
            r.ClienteIntegracionId is Guid id ? [id.ToString()] : [],
        IctQueryFieldCatalog.TieneNovedades => r => [Bool(r.TieneNovedades)],
        IctQueryFieldCatalog.TieneBorrador => r => [Bool(r.TieneBorrador)],
        IctQueryFieldCatalog.Prioritario => r => [Bool(r.Prioritario)],
        IctQueryFieldCatalog.Compania => r => [r.TenantId.ToString()],
        _ => _ => [],
    };

    /// <summary>
    /// Qué instante mira el rango, según la fecha elegida. Devolver <c>null</c> deja la fila fuera:
    /// un pre-trámite que nunca pasó validación de negocio no aparece en una consulta filtrada por
    /// esa fecha.
    /// </summary>
    private static DateTimeOffset? DateOf(IctRow row, string campo) => campo switch
    {
        IctQueryDateField.ValidacionNegocio => row.ValidacionNegocioEn,
        IctQueryDateField.ValidacionExterna => row.ValidacionExternaEn,
        _ => row.RegistradoEn,
    };

    private static IReadOnlyList<string> Single(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];

    private static string Bool(bool value) => value ? "true" : "false";

    // ── Orden ─────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<IctRow> Sort(List<IctRow> rows, string? sortBy, bool descending)
    {
        // El desempate por id no es cosmético: sin él dos filas con la misma fecha pueden cambiar de
        // sitio entre página y página, y el export encadena páginas.
        Func<IctRow, IComparable?> key = sortBy switch
        {
            IctQuerySort.Radicado => r => r.Radicado,
            IctQuerySort.Placa => r => r.Placa,
            IctQuerySort.Estado => r => r.Estado,
            _ => r => r.RegistradoEn,
        };

        var ordered = descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

        return ordered.ThenBy(r => r.Id);
    }

    // ── Proyección ────────────────────────────────────────────────────────────────────────────

    private static IctQueryRowDto ToDto(IctRow row, string tenantNombre) =>
        new(
            Id: row.Id,
            TransactionNumber: row.TransactionNumber,
            Radicado: row.Radicado,
            Placa: row.Placa,
            Vin: row.Vin,
            TenantId: row.TenantId,
            TenantNombre: tenantNombre,
            TipoTramite: row.TipoTramiteNombre,
            Estado: row.Estado,
            TieneNovedades: row.TieneNovedades,
            TieneBorrador: row.TieneBorrador,
            Prioritario: row.Prioritario,
            Secretaria: row.Secretaria,
            ClienteIntegracion: row.ClienteIntegracionNombre,
            Comentarios: row.Comentarios,
            ProcedureInstanceId: row.ProcedureInstanceId,
            RegistradoEn: row.RegistradoEn,
            ValidacionNegocioEn: row.ValidacionNegocioEn,
            ValidacionExternaEn: row.ValidacionExternaEn);

    private async Task<string> ResolveTenantNameAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var nombre = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.LegalName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return nombre ?? "(compañía retirada)";
    }

    // ── Catálogo de campos ────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<QueryFieldDto>> GetFieldsAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tipos = new List<QueryFieldOptionDto>();
        var secretarias = new List<QueryFieldOptionDto>();
        var clientes = new List<QueryFieldOptionDto>();

        await WithTenantAsync(tenantId, async (cmd, ct) =>
        {
            // Los tipos de trámite realmente usados por esta compañía. Ofrecer uno con el que nunca
            // ha integrado es ofrecer un filtro que solo puede devolver cero.
            cmd.CommandText = """
                SELECT DISTINCT m.transaction_type, tm.description, tm.family
                FROM ict.external_integration_master m
                LEFT JOIN ict.procedure_type_mapping tm ON tm.external_transaction_type = m.transaction_type
                WHERE m.tenant_id = @tenant AND m.deleted_at IS NULL
                """;
            AddParam(cmd, "tenant", tenantId);
            var crudos = new List<(string Id, string Name, string? Family)>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var tipoId = reader.GetInt32(0);
                    var label = reader.IsDBNull(1) ? $"Tipo {tipoId}" : reader.GetString(1);
                    crudos.Add((
                        tipoId.ToString(CultureInfo.InvariantCulture),
                        label,
                        reader.IsDBNull(2) ? null : reader.GetString(2)));
                }
            }

            // El orden y el agrupado los pone el catálogo compartido, no un ORDER BY: los tipos
            // salen bajo el encabezado de su familia, igual que en los otros dos motores de
            // consultas. ICT tiene su PROPIO catálogo de tipos de transacción, y `family` del mapeo
            // es lo que los ata a las tres familias de FLIT.
            tipos.AddRange(TipoTramiteOptionCatalog.Build(crudos));

            cmd.Parameters.Clear();
            cmd.CommandText = """
                SELECT DISTINCT traffic_secretary_code
                FROM ict.external_integration_master
                WHERE tenant_id = @tenant AND deleted_at IS NULL
                  AND traffic_secretary_code IS NOT NULL AND traffic_secretary_code <> ''
                ORDER BY traffic_secretary_code
                """;
            AddParam(cmd, "tenant", tenantId);
            await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var codigo = reader.GetString(0);
                    secretarias.Add(new QueryFieldOptionDto(codigo, codigo));
                }
            }

            cmd.Parameters.Clear();
            cmd.CommandText = """
                SELECT DISTINCT ic.id, ic.username
                FROM ict.integration_clients ic
                WHERE ic.tenant_id = @tenant AND ic.deleted_at IS NULL
                ORDER BY ic.username
                """;
            AddParam(cmd, "tenant", tenantId);
            await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    clientes.Add(new QueryFieldOptionDto(reader.GetGuid(0).ToString(), reader.GetString(1)));
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        return IctQueryFieldCatalog.Fields
            .Where(f => f.Id != IctQueryFieldCatalog.Compania)
            .Select(f => f.Id switch
            {
                IctQueryFieldCatalog.TipoTramite => f with { Options = tipos },
                IctQueryFieldCatalog.Secretaria => f with { Options = secretarias },
                IctQueryFieldCatalog.ClienteIntegracion => f with { Options = clientes },
                _ => f,
            })
            .ToList();
    }

    // ── Acceso a la base cross-schema ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fija el GUC de tenant por RLS dentro de una transacción y ejecuta <paramref name="body"/> con
    /// un <see cref="DbCommand"/> listo para usarse contra <c>ict.*</c>. Mismo patrón que
    /// <c>AlertMetricsReadRepository.GetMetricValueAsync</c>.
    /// </summary>
    private async Task WithTenantAsync(
        Guid tenantId, Func<DbCommand, CancellationToken, Task> body, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", cancellationToken)
                    .ConfigureAwait(false);

                var conn = _context.Database.GetDbConnection();
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction.GetDbTransaction();

                await body(cmd, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string? GetStringOrNull(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetGuidOrNull(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? GetDateTimeOrNull(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    // ── Consultas guardadas ───────────────────────────────────────────────────────────────────
    // Estas SÍ son EF normal: analytics.ict_saved_queries vive en el propio Postgres de core-api,
    // no en el schema de otro servicio.

    public async Task<IReadOnlyList<SavedQueryDto>> ListSavedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var propias = await _context.IctSavedQueries
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
            .. IctFactoryQueries.Queries,
        ];
    }

    public async Task<SavedQueryDto?> GetSavedByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (IctFactoryQueries.IsFactory(id))
            return IctFactoryQueries.Queries.FirstOrDefault(q => q.Id == id);

        var entity = await _context.IctSavedQueries
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : ToSavedDto(entity);
    }

    public async Task<SavedQueryDto> SaveAsync(
        Guid tenantId,
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var definition = IctQueryFieldCatalog.Normalize(input.Definition);
        var nombre = input.Nombre.Trim();
        var json = JsonSerializer.Serialize(definition, JsonOptions);

        IctSavedQueryEntity? entity = null;

        // Guardar sobre una de fábrica es duplicarla, no editarla: las de fábrica no viven en la
        // base y tienen que seguir estando ahí para el siguiente que abra la consola.
        if (id is Guid existingId && !IctFactoryQueries.IsFactory(existingId))
        {
            entity = await _context.IctSavedQueries
                .FirstOrDefaultAsync(
                    q => q.Id == existingId && q.TenantId == tenantId && q.UserId == userId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (entity is null)
        {
            var cuantas = await _context.IctSavedQueries
                .CountAsync(q => q.TenantId == tenantId && q.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (cuantas >= QueryLimits.MaxConsultasGuardadas)
            {
                throw new SavedQueryLimitException(QueryLimits.MaxConsultasGuardadas);
            }

            entity = new IctSavedQueryEntity
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                UserId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _context.IctSavedQueries.Add(entity);
        }
        else
        {
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var repetido = await _context.IctSavedQueries
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
        entity.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim();
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
        var entity = await _context.IctSavedQueries
            .FirstOrDefaultAsync(
                q => q.Id == id && q.TenantId == tenantId && q.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        _context.IctSavedQueries.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static SavedQueryDto ToSavedDto(IctSavedQueryEntity entity)
    {
        QueryDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<QueryDefinition>(entity.Definicion, JsonOptions);
        }
        catch (JsonException)
        {
            // Una consulta guardada con un JSON que ya no encaja se abre vacía en vez de tumbar la
            // lista entera.
            definition = null;
        }

        return new SavedQueryDto(
            entity.Id,
            entity.Nombre,
            entity.Descripcion,
            DeFabrica: false,
            IctQueryFieldCatalog.Normalize(definition),
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    // ── Forma interna ─────────────────────────────────────────────────────────────────────────

    /// <summary>Un pre-trámite de ICT con TODO lo que cualquier condición pueda preguntarle, ya materializado.</summary>
    private sealed record IctRow(
        Guid Id,
        Guid TenantId,
        long TransactionNumber,
        string? Radicado,
        string? Placa,
        string? Vin,
        int TipoTramiteId,
        string? TipoTramiteNombre,
        string? TipoTramiteFamilia,
        string Estado,
        bool TieneNovedades,
        bool TieneBorrador,
        Guid? ProcedureInstanceId,
        bool Prioritario,
        string? Secretaria,
        Guid? ClienteIntegracionId,
        string? ClienteIntegracionNombre,
        string? Comentarios,
        DateTimeOffset RegistradoEn,
        DateTimeOffset? ValidacionNegocioEn,
        DateTimeOffset? ValidacionExternaEn);
}
