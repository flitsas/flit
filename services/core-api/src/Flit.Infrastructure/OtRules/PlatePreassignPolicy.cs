using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Decide la ruta de preasignación de placa al radicar (HU #10608, Feature #10587). Solo matrícula
/// inicial; exige preasignación activa (flag de la compañía + grant + allow_plate_preassign del OT).
/// Con placa elegida y disponible la reserva y aterriza en <c>asignado</c> (Flujo A); sin rango/placa,
/// <c>preasignado</c> (Flujo B). Patrón de <see cref="RnmcRequirementPolicy"/>.
/// </summary>
internal sealed class PlatePreassignPolicy : IPlatePreassignPolicy
{
    private const string OfficeFieldKey = "transit_office_id";
    private const string PlateFieldKey = "plate";

    private readonly FlitDbContext _context;
    private readonly IPlateRangeRepository _plateRepo;
    private readonly ILogger<PlatePreassignPolicy>? _logger;

    public PlatePreassignPolicy(
        FlitDbContext context,
        IPlateRangeRepository plateRepo,
        ILogger<PlatePreassignPolicy>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _plateRepo = plateRepo ?? throw new ArgumentNullException(nameof(plateRepo));
        _logger = logger;
    }

    public async Task<PlateRouteResult> DecideAsync(
        Guid tenantId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        // HU #10806 (Parte C) — la modalidad y los field_values del trámite viven en tablas aisladas por
        // RLS (app.current_tenant_id). Se leen bajo el scope de tenant para que la decisión sea correcta
        // también donde RLS esté forzado (QA/PDN): sin el scope, esas lecturas volverían vacías y la ruta
        // degradaría en silencio a estándar (síntoma de TRM-2026-000003).
        var snapshot = await ReadInTenantScopeAsync(tenantId, async () =>
        {
            var modalidad = await _context.ProcedureInstances
                .AsNoTracking()
                .Where(p => p.Id == instanceId && p.TenantId == tenantId)
                .Select(p => new { p.ModalidadEntrada })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (modalidad is null)
                return (Found: false, Modalidad: (string?)null, Office: (string?)null, Plate: (string?)null);

            var fields = await _context.ProcedureInstanceFieldValues
                .AsNoTracking()
                .Where(f => f.ProcedureInstanceId == instanceId
                    && (f.FieldKey == OfficeFieldKey || f.FieldKey == PlateFieldKey))
                .Select(f => new { f.FieldKey, f.ValueText })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return (
                Found: true,
                Modalidad: modalidad.ModalidadEntrada,
                Office: fields.FirstOrDefault(f => f.FieldKey == OfficeFieldKey)?.ValueText,
                Plate: fields.FirstOrDefault(f => f.FieldKey == PlateFieldKey)?.ValueText);
        }, cancellationToken).ConfigureAwait(false);

        // HU #10806 (Parte C) — distinguir "instancia no legible" (0 filas: binario/scope incorrecto) de
        // "no es matrícula inicial". Antes ambas caían en NotMatricula en silencio; ahora la primera deja
        // traza observable para que un despliegue/scope malo sea detectable de inmediato en el log.
        if (!snapshot.Found)
        {
            if (_logger is not null)
                PlatePreassignLog.InstanceNotReadable(_logger, instanceId, tenantId);
            return PlateRouteResult.NotMatricula;
        }

        if (snapshot.Modalidad is null
            || TramiteModalidadEntradaCodes.FromCode(snapshot.Modalidad) != TramiteModalidadEntrada.MatriculaInicial)
        {
            return PlateRouteResult.NotMatricula;
        }

        if (!Guid.TryParse(snapshot.Office, out var officeId) || officeId == Guid.Empty)
        {
            return PlateRouteResult.NoOffice;
        }

        // HU #10806 — distinguir "compañía sin preasignación" (estándar) de "compañía activa pero OT
        // mal configurado" (bloquear), en vez del antiguo bool que degradaba en silencio en ambos casos.
        var eligibility = await _plateRepo
            .EvaluateAssignmentEligibilityAsync(tenantId, officeId, cancellationToken)
            .ConfigureAwait(false);
        switch (eligibility)
        {
            case PlateAssignmentEligibility.CompanyDisabled:
                return PlateRouteResult.NotEnabled;
            case PlateAssignmentEligibility.Misconfigured:
                return PlateRouteResult.Misconfigured;
        }

        var plate = snapshot.Plate;
        if (!string.IsNullOrWhiteSpace(plate))
        {
            var reserved = await _plateRepo
                .TryReservePlateAsync(tenantId, officeId, plate, instanceId, cancellationToken)
                .ConfigureAwait(false);
            if (reserved)
            {
                return PlateRouteResult.Reserved;
            }
        }

        // Ruta activa pero sin placa disponible elegida → se envía al OT para que asigne (Flujo B).
        return PlateRouteResult.NoPlate;
    }

    /// <summary>
    /// Ejecuta una lectura fijando <c>app.current_tenant_id</c> (RLS) dentro de una transacción, con el
    /// mismo patrón que los repositorios (execution strategy + <c>set_config(..., true)</c>). Se une a la
    /// transacción ambiente si ya existe. No-op de scope fuera de proveedor relacional (tests InMemory).
    /// </summary>
    private async Task<T> ReadInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await read().ConfigureAwait(false);
        }

        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                cancellationToken).ConfigureAwait(false);
            return await read().ConfigureAwait(false);
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

                var result = await read().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}

/// <summary>Logging source-generated (CA1848) del enrutamiento de placa (HU #10806).</summary>
internal static partial class PlatePreassignLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Preasignación de placa: la instancia {InstanceId} (tenant {TenantId}) no es legible al "
            + "decidir la ruta (0 filas). Posible binario/scope de tenant incorrecto; se degrada a ruta estándar.")]
    public static partial void InstanceNotReadable(ILogger logger, Guid instanceId, Guid tenantId);
}
