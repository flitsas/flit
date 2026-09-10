using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Inventario de rangos de placas (HU #10650, Feature #10587). RLS permisiva a nivel BD; el scope
/// de tenant se fija por consistencia de auditoría. Patrón de <see cref="OtRequirementsRepository"/>.
/// </summary>
internal sealed class PlateRangeRepository : IPlateRangeRepository
{
    private readonly FlitDbContext _context;

    public PlateRangeRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<CreatePlateRangeResult> CreateRangeAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        string prefix,
        int rangeFrom,
        int rangeTo,
        Guid? createdBy,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var normalizedPrefix = prefix?.Trim().ToUpperInvariant() ?? string.Empty;
                var error = PlateRangeRules.Validate(normalizedPrefix, rangeFrom, rangeTo);
                if (error is not null)
                {
                    return CreatePlateRangeResult.Fail(error);
                }

                var plates = PlateRangeRules.Enumerate(normalizedPrefix, rangeFrom, rangeTo).ToList();

                // Rechaza solapamiento con placas ya registradas para el mismo OT (UNIQUE office+plate).
                var existing = await _context.PlateRangeDetails
                    .AsNoTracking()
                    .Where(d => d.TransitOfficeId == transitOfficeId && plates.Contains(d.Plate))
                    .Select(d => d.Plate)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    return CreatePlateRangeResult.Fail(
                        $"El rango se solapa con placas ya registradas para el OT (ej. {existing}).");
                }

                var now = DateTimeOffset.UtcNow;
                var range = new PlateRangeEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = companyTenantId,
                    TransitOfficeId = transitOfficeId,
                    Prefix = normalizedPrefix,
                    RangeFrom = rangeFrom,
                    RangeTo = rangeTo,
                    EditableUntil = now.Add(PlateRangeRules.EditWindow),
                    CreatedAt = now,
                    CreatedBy = createdBy,
                };
                _context.PlateRanges.Add(range);

                // HU #10797 — EF no conoce la relación plate_range_details → plate_ranges (PlateRangeId se
                // declara solo como Property, sin navegación ni HasForeignKey), así que NO ordena el INSERT
                // padre-antes-de-hijo y en PostgreSQL insertaba los detalles antes que el rango, violando
                // plate_range_details_plate_range_id_fkey. Persistimos el rango PRIMERO y luego los detalles,
                // en la MISMA transacción (ExecuteInTenantScopeAsync) para conservar la atomicidad.
                // (El proveedor InMemory de los tests no valida FKs, por eso el bug no se detectaba.)
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                foreach (var plate in plates)
                {
                    _context.PlateRangeDetails.Add(new PlateRangeDetailEntity
                    {
                        Id = Guid.NewGuid(),
                        PlateRangeId = range.Id,
                        TenantId = companyTenantId,
                        TransitOfficeId = transitOfficeId,
                        Plate = plate,
                        State = PlateState.Disponible,
                        CreatedAt = now,
                    });
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return CreatePlateRangeResult.Ok(range.Id, plates.Count);
            },
            cancellationToken);

    public Task<IReadOnlyList<PlateRangeSummary>> ListRangesAsync(
        Guid companyTenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var ranges = await _context.PlateRanges
                    .AsNoTracking()
                    .Where(r => r.TenantId == companyTenantId
                        && (transitOfficeId == null || r.TransitOfficeId == transitOfficeId))
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var result = new List<PlateRangeSummary>(ranges.Count);
                foreach (var r in ranges)
                {
                    var available = await _context.PlateRangeDetails
                        .AsNoTracking()
                        .CountAsync(
                            d => d.PlateRangeId == r.Id && d.State == PlateState.Disponible,
                            cancellationToken)
                        .ConfigureAwait(false);

                    result.Add(new PlateRangeSummary(
                        r.Id, r.TenantId, r.TransitOfficeId, r.Prefix, r.RangeFrom, r.RangeTo,
                        r.EditableUntil, r.RangeTo - r.RangeFrom + 1, available));
                }

                return (IReadOnlyList<PlateRangeSummary>)result;
            },
            cancellationToken);

    public Task<IReadOnlyList<PlateDetail>> ListDetailsAsync(
        Guid companyTenantId,
        Guid? transitOfficeId,
        string? state,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var details = await _context.PlateRangeDetails
                    .AsNoTracking()
                    .Where(d => d.TenantId == companyTenantId
                        && (transitOfficeId == null || d.TransitOfficeId == transitOfficeId)
                        && (state == null || d.State == state))
                    .OrderBy(d => d.Plate)
                    .Select(d => new PlateDetail(
                        d.Id, d.PlateRangeId, d.TenantId, d.TransitOfficeId, d.Plate, d.State, d.ProcedureInstanceId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<PlateDetail>)details;
            },
            cancellationToken);

    public async Task<Guid?> ResolveOfficeIdAsync(Guid otTenantId, CancellationToken cancellationToken = default)
    {
        var officeId = await _context.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TenantId == otTenantId)
            .Select(p => p.TransitOfficeId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return officeId == Guid.Empty ? null : officeId;
    }

    public async Task<bool> IsAssignmentAllowedAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        await EvaluateAssignmentEligibilityAsync(companyTenantId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false) == PlateAssignmentEligibility.Allowed;

    // HU #10806 — evalúa el AND de tres flags devolviendo el MOTIVO del corte. Lee bajo el guard
    // cross-tenant (row_security off), consistente con ListEligibleCompaniesAsync, porque toca filas
    // del OT (ot_requirements) y del grant que pertenecen a tenants distintos al de la compañía.
    public Task<PlateAssignmentEligibility> EvaluateAssignmentEligibilityAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(async () =>
        {
            var companyFlag = await _context.TenantOperationalPolicies
                .AsNoTracking()
                .Where(p => p.TenantId == companyTenantId)
                .Select(p => (bool?)p.PlatePreassignEnabled)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? false;

            // La compañía no usa preasignación → ruta estándar, sin fricción ni bloqueo.
            if (!companyFlag)
            {
                return PlateAssignmentEligibility.CompanyDisabled;
            }

            var grant = await _context.TenantTransitOfficeGrants
                .AsNoTracking()
                .AnyAsync(
                    g => g.TenantId == companyTenantId && g.TransitOfficeId == transitOfficeId && g.IsEnabled,
                    cancellationToken)
                .ConfigureAwait(false);

            var otAllows = await _context.OtRequirements
                .AsNoTracking()
                .Where(r => r.TransitOfficeId == transitOfficeId)
                .Select(r => (bool?)r.AllowPlatePreassign)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? false;

            // La compañía SÍ tiene el flag pero el OT no está habilitado (grant/allow) → mala config.
            return grant && otAllows
                ? PlateAssignmentEligibility.Allowed
                : PlateAssignmentEligibility.Misconfigured;
        }, cancellationToken);

    // HU #10797 — compañías elegibles para recibir un rango de este OT (alimenta el selector de la consola,
    // en vez de escribir el tenant id): OT con allow_plate_preassign + grant vigente + preasignación activa
    // de la compañía. Lectura cross-tenant (el OT lee nombres de otras compañías → row_security off).
    public Task<IReadOnlyList<EligibleCompany>> ListEligibleCompaniesAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(async () =>
        {
            var otAllows = await _context.OtRequirements
                .AsNoTracking()
                .Where(r => r.TransitOfficeId == transitOfficeId)
                .Select(r => (bool?)r.AllowPlatePreassign)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? false;
            if (!otAllows)
            {
                return (IReadOnlyList<EligibleCompany>)Array.Empty<EligibleCompany>();
            }

            var grantedTenantIds = await _context.TenantTransitOfficeGrants
                .AsNoTracking()
                .Where(g => g.TransitOfficeId == transitOfficeId && g.IsEnabled)
                .Select(g => g.TenantId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (grantedTenantIds.Count == 0)
            {
                return Array.Empty<EligibleCompany>();
            }

            var eligibleIds = await _context.TenantOperationalPolicies
                .AsNoTracking()
                .Where(p => grantedTenantIds.Contains(p.TenantId) && p.PlatePreassignEnabled)
                .Select(p => p.TenantId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (eligibleIds.Count == 0)
            {
                return Array.Empty<EligibleCompany>();
            }

            return (IReadOnlyList<EligibleCompany>)await _context.Tenants
                .AsNoTracking()
                .Where(t => eligibleIds.Contains(t.Id))
                .OrderBy(t => t.LegalName)
                .Select(t => new EligibleCompany(t.Id, t.LegalName))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

    public Task<CreatePlateRangeResult> EditRangeAsync(
        Guid rangeId,
        string prefix,
        int rangeFrom,
        int rangeTo,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = prefix?.Trim().ToUpperInvariant() ?? string.Empty;

        return ExecuteInTenantScopeAsync(
            Guid.Empty,
            async () =>
            {
                var range = await _context.PlateRanges
                    .FirstOrDefaultAsync(r => r.Id == rangeId, cancellationToken)
                    .ConfigureAwait(false);

                if (range is null)
                {
                    return CreatePlateRangeResult.Fail("El rango no existe.");
                }

                if (DateTimeOffset.UtcNow >= range.EditableUntil)
                {
                    return CreatePlateRangeResult.Fail("La ventana de edición del rango (60 min) ya venció.");
                }

                var inUse = await _context.PlateRangeDetails
                    .AsNoTracking()
                    .AnyAsync(
                        d => d.PlateRangeId == rangeId
                            && (d.State == PlateState.Preasignada || d.State == PlateState.Utilizada),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (inUse)
                {
                    return CreatePlateRangeResult.Fail("No se puede editar: el rango tiene placas preasignadas o utilizadas.");
                }

                var error = PlateRangeRules.Validate(normalizedPrefix, rangeFrom, rangeTo);
                if (error is not null)
                {
                    return CreatePlateRangeResult.Fail(error);
                }

                var plates = PlateRangeRules.Enumerate(normalizedPrefix, rangeFrom, rangeTo).ToList();

                // Solapamiento con placas de OTROS rangos del mismo OT (excluye las de este rango).
                var overlap = await _context.PlateRangeDetails
                    .AsNoTracking()
                    .Where(d => d.TransitOfficeId == range.TransitOfficeId
                        && d.PlateRangeId != rangeId
                        && plates.Contains(d.Plate))
                    .Select(d => d.Plate)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (overlap is not null)
                {
                    return CreatePlateRangeResult.Fail($"El rango se solapa con placas ya registradas para el OT (ej. {overlap}).");
                }

                var oldDetails = await _context.PlateRangeDetails
                    .Where(d => d.PlateRangeId == rangeId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                _context.PlateRangeDetails.RemoveRange(oldDetails);

                var now = DateTimeOffset.UtcNow;
                range.Prefix = normalizedPrefix;
                range.RangeFrom = rangeFrom;
                range.RangeTo = rangeTo;
                range.UpdatedAt = now;
                range.UpdatedBy = updatedBy;

                foreach (var plate in plates)
                {
                    _context.PlateRangeDetails.Add(new PlateRangeDetailEntity
                    {
                        Id = Guid.NewGuid(),
                        PlateRangeId = range.Id,
                        TenantId = range.TenantId,
                        TransitOfficeId = range.TransitOfficeId,
                        Plate = plate,
                        State = PlateState.Disponible,
                        CreatedAt = now,
                    });
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return CreatePlateRangeResult.Ok(range.Id, plates.Count);
            },
            cancellationToken);
    }

    public Task<PlateOpResult> SetPlateStateAsync(
        Guid plateDetailId,
        string targetState,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            Guid.Empty,
            async () =>
            {
                var detail = await _context.PlateRangeDetails
                    .FirstOrDefaultAsync(d => d.Id == plateDetailId, cancellationToken)
                    .ConfigureAwait(false);

                if (detail is null)
                {
                    return PlateOpResult.Fail("La placa no existe.");
                }

                if (!PlateStateMachine.IsValidTransition(detail.State, targetState))
                {
                    return PlateOpResult.Fail($"Transición de placa no permitida: {detail.State} → {targetState}.");
                }

                detail.State = targetState;
                detail.UpdatedAt = DateTimeOffset.UtcNow;

                // Al liberar/revocar, se suelta el vínculo con el trámite.
                if (targetState is PlateState.Disponible or PlateState.Revocada or PlateState.Bloqueada)
                {
                    detail.ProcedureInstanceId = null;
                    detail.ReservedAt = null;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return PlateOpResult.Ok;
            },
            cancellationToken);

    public Task<bool> TryReservePlateAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        string plate,
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var normalized = plate?.Trim().ToUpperInvariant() ?? string.Empty;
                var detail = await _context.PlateRangeDetails
                    .FirstOrDefaultAsync(
                        d => d.TenantId == companyTenantId
                            && d.TransitOfficeId == transitOfficeId
                            && d.Plate == normalized,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (detail is null)
                {
                    return false;
                }

                // Idempotencia: ya reservada para este mismo trámite.
                if (detail.State == PlateState.Preasignada && detail.ProcedureInstanceId == procedureInstanceId)
                {
                    return true;
                }

                if (detail.State != PlateState.Disponible)
                {
                    return false;
                }

                detail.State = PlateState.Preasignada;
                detail.ProcedureInstanceId = procedureInstanceId;
                detail.ReservedAt = DateTimeOffset.UtcNow;
                detail.UpdatedAt = detail.ReservedAt;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    // HU #10800 — asignación de placa FUERA DE RANGO: registra la placa como un rango ad-hoc de 1 placa
    // (prefijo + número parseados) y la reserva al trámite. Respeta el modelo (plate_range_id NOT NULL) y
    // la unicidad (uq_plate_range_details_office_plate) sin cambio de esquema.
    public Task<PlateOpResult> ReserveOutOfRangePlateAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        string plate,
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var normalized = plate?.Trim().ToUpperInvariant() ?? string.Empty;
                var parsed = PlateRangeRules.ParsePlate(normalized);
                if (parsed is null)
                {
                    return PlateOpResult.Fail("La placa debe tener el formato de matrícula (3 letras + 3 dígitos, ej. ABC123).");
                }

                var now = DateTimeOffset.UtcNow;

                // ¿Ya existe la placa en el inventario del (compañía, OT)?
                var existing = await _context.PlateRangeDetails
                    .FirstOrDefaultAsync(
                        d => d.TenantId == companyTenantId
                            && d.TransitOfficeId == transitOfficeId
                            && d.Plate == normalized,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    // Idempotente: ya reservada para este mismo trámite.
                    if (existing.State == PlateState.Preasignada && existing.ProcedureInstanceId == procedureInstanceId)
                    {
                        return PlateOpResult.Ok;
                    }
                    // Disponible → se reserva.
                    if (existing.State == PlateState.Disponible)
                    {
                        existing.State = PlateState.Preasignada;
                        existing.ProcedureInstanceId = procedureInstanceId;
                        existing.ReservedAt = now;
                        existing.UpdatedAt = now;
                        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        return PlateOpResult.Ok;
                    }
                    if (existing.ProcedureInstanceId is not null)
                    {
                        return PlateOpResult.Fail(
                            $"La placa {normalized} ya está asignada a otro trámite de este organismo de tránsito. Elija una placa diferente.");
                    }
                    // Utilizada / bloqueada / revocada sin trámite asociado.
                    return PlateOpResult.Fail(
                        $"La placa {normalized} ya está registrada para este organismo de tránsito y no está disponible. Elija una placa diferente.");
                }

                // La unicidad del inventario es (transit_office_id, plate), SIN tenant, mientras que la
                // consulta anterior corre bajo RLS: una placa registrada por otra compañía del mismo OT
                // es invisible y el INSERT moría contra uq_plate_range_details_office_plate (500 sin
                // motivo para el operador). Se verifica cross-tenant antes de insertar.
                if (await PlateExistsInOfficeAsync(transitOfficeId, normalized, cancellationToken).ConfigureAwait(false))
                {
                    return PlateOpResult.Fail(
                        $"La placa {normalized} ya está asignada en este organismo de tránsito (registrada por otra compañía). Elija una placa diferente.");
                }

                var (prefix, number) = parsed.Value;

                // Rango ad-hoc de 1 placa (contenedor para satisfacer la FK plate_range_id NOT NULL).
                // uq_plate_ranges_office_prefix_bounds es por (OT, prefijo, desde, hasta): si el rango
                // ad-hoc ya existe se reutiliza en vez de insertar un duplicado que rompería la carga.
                var range = await _context.PlateRanges
                    .FirstOrDefaultAsync(
                        r => r.TransitOfficeId == transitOfficeId
                            && r.Prefix == prefix
                            && r.RangeFrom == number
                            && r.RangeTo == number,
                        cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    if (range is null)
                    {
                        range = new PlateRangeEntity
                        {
                            Id = Guid.NewGuid(),
                            TenantId = companyTenantId,
                            TransitOfficeId = transitOfficeId,
                            Prefix = prefix,
                            RangeFrom = number,
                            RangeTo = number,
                            EditableUntil = now, // ad-hoc: sin ventana de edición útil
                            CreatedAt = now,
                        };
                        _context.PlateRanges.Add(range);
                        // FK: el padre primero (mismo criterio que CreateRangeAsync, HU #10797).
                        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    _context.PlateRangeDetails.Add(new PlateRangeDetailEntity
                    {
                        Id = Guid.NewGuid(),
                        PlateRangeId = range.Id,
                        TenantId = companyTenantId,
                        TransitOfficeId = transitOfficeId,
                        Plate = normalized,
                        State = PlateState.Preasignada,
                        ProcedureInstanceId = procedureInstanceId,
                        ReservedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    // Última defensa ante una carrera por la misma placa: los índices únicos del OT no
                    // pueden escapar como 500 al operador, que necesita saber qué corregir.
                    return PlateOpResult.Fail(
                        $"La placa {normalized} ya está asignada en este organismo de tránsito. Elija una placa diferente.");
                }

                return PlateOpResult.Ok;
            },
            cancellationToken);

    // ¿La placa ya está en el inventario del OT, sin importar la compañía dueña? Es la pregunta que
    // responde el índice uq_plate_range_details_office_plate, así que debe evaluarse igual que él:
    // sin RLS. Se reactiva row_security al terminar para que el INSERT posterior siga acotado al tenant.
    private async Task<bool> PlateExistsInOfficeAsync(
        Guid transitOfficeId,
        string plate,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await _context.PlateRangeDetails
                .AsNoTracking()
                .AnyAsync(d => d.TransitOfficeId == transitOfficeId && d.Plate == plate, cancellationToken)
                .ConfigureAwait(false);
        }

        await _context.Database
            .ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await _context.PlateRangeDetails
                .AsNoTracking()
                .AnyAsync(d => d.TransitOfficeId == transitOfficeId && d.Plate == plate, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await _context.Database
                .ExecuteSqlRawAsync("SET LOCAL row_security = on", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        // Si ya hay una transacción ambiente (p.ej. este repo se invoca DENTRO del scope de otro,
        // como OtClientProcedureRepository.AssignPlateAsync — Flujo B), NO abrir otra: abrir una
        // segunda transacción sobre la misma conexión lanza "connection is already in a transaction".
        // Nos unimos a la existente, fijamos el tenant (SET LOCAL) y ejecutamos; el commit lo hace el
        // scope externo. Tampoco se usa ExecutionStrategy aquí (ya la aporta el scope externo).
        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                cancellationToken).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                    cancellationToken).ConfigureAwait(false);

                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    // HU #10797 — lectura cross-tenant (el OT lee compañías de otros tenants): desactiva RLS localmente
    // dentro de una transacción. Mismo patrón que OtClientProcedureRepository. No-op fuera de relacional.
    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);
                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
