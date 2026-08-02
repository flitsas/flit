using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras EF Core de mandatarios (ADR-0023). Cada operación persiste el mandatario, la
/// reasignación de compañías y su fila de auditoría en <c>admin.tenant_config_audit_logs</c>
/// dentro de una única transacción (todo o nada), fijando <c>app.current_tenant_id</c> al
/// tenant del OT con <c>set_config(..., is_local := true)</c> —igual que
/// <see cref="TransitGrantRepository"/>—. La auditoría <b>no</b> registra el número de
/// documento (PII, Ley 1581): solo id, huella de integridad y compañías.
///
/// La exclusividad (OT, compañía) → un mandatario activo la valida el handler antes; el índice
/// único parcial <c>uq_mandate_signer_companies_active</c> es el guardián último en BD.
/// </summary>
internal sealed class MandateSignerRepository : IMandateSignerRepository
{
    private const string EntityName = "mandate_signer";

    private readonly FlitDbContext _context;

    public MandateSignerRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Guid> CreateAsync(
        CreateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistCreateAsync(data, cancellationToken),
            cancellationToken);
    }

    public Task<bool> UpdateAsync(
        UpdateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistUpdateAsync(data, cancellationToken),
            cancellationToken);
    }

    public Task<bool> InactivateAsync(
        InactivateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistInactivateAsync(data, cancellationToken),
            cancellationToken);
    }

    public Task<bool> ReactivateAsync(
        ReactivateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistReactivateAsync(data, cancellationToken),
            cancellationToken);
    }

    private async Task<Guid> PersistCreateAsync(
        CreateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var signerId = Guid.NewGuid();

        _context.MandateSigners.Add(new MandateSigner
        {
            Id = signerId,
            TransitOfficeId = data.TransitOfficeId,
            FullName = data.FullName,
            DocumentType = data.DocumentType,
            DocumentNumber = data.DocumentNumber,
            IntegrityHash = data.IntegrityHash,
            Email = data.Email,
            UserId = data.UserId,
            RegisteredAt = data.RegisteredAt,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = data.CreatedBy,
        });

        // HU #11201 — los organismos van al puente. Sin lista, el único organismo es el primario, que
        // es exactamente lo que manda el alta desde el perfil del organismo.
        var offices = OrganismosDe(data.TransitOfficeIds, data.TransitOfficeId);
        foreach (var officeId in offices)
        {
            _context.MandateSignerTransitOffices.Add(NewOffice(signerId, officeId, now));
        }

        // La asignación a compañías se escribe por CADA organismo: es la que consulta el trámite para
        // saber quién firma (mandate_signer_companies lleva el organismo en su llave). Escribirla solo
        // para el primario dejaría al mandatario invisible en los demás organismos.
        foreach (var officeId in offices)
        {
            foreach (var companyId in Distinct(data.CompanyTenantIds))
            {
                _context.MandateSignerCompanies.Add(NewAssignment(signerId, officeId, companyId, now));
            }
        }

        AddAudit(
            data.OtTenantId,
            fieldName: "created",
            oldValue: null,
            newValue: AuditPayload(signerId, data.IntegrityHash, data.CompanyTenantIds),
            changedAt: now,
            changedBy: data.CreatedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return signerId;
    }

    private async Task<bool> PersistUpdateAsync(
        UpdateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        if (signer is null || !signer.IsActive)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        // Se cargan también las inactivas: si una pareja (organismo, compañía) vuelve, se REACTIVA su
        // fila en vez de insertar otra, y así el histórico no se llena de duplicados.
        var currentAssignments = await _context.MandateSignerCompanies
            .Where(c => c.MandateSignerId == signer.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var oldHash = signer.IntegrityHash;
        var oldCompanyIds = currentAssignments
            .Where(c => c.IsActive)
            .Select(c => c.CompanyTenantId)
            .Distinct()
            .ToList();

        // El primario solo se mueve si el nuevo está entre los organismos que quedan. Repuntarlo a uno
        // que la edición acaba de retirar dejaría la fila apuntando fuera de su propia lista, y la
        // reactivación —que restaura el primario— resucitaría un organismo que el gestor quitó.
        if (data.NuevoOrganismoPrimario is { } nuevoPrimario
            && nuevoPrimario != Guid.Empty
            && nuevoPrimario != signer.TransitOfficeId
            && data.TransitOfficeIds is not null
            && data.TransitOfficeIds.Contains(nuevoPrimario))
        {
            signer.TransitOfficeId = nuevoPrimario;
        }

        signer.FullName = data.FullName;
        signer.DocumentType = data.DocumentType;
        signer.DocumentNumber = data.DocumentNumber;
        signer.IntegrityHash = data.IntegrityHash;
        signer.Email = data.Email;
        signer.UserId = data.UserId;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.UpdatedBy;

        // HU #11201 (AC2/AC3) — los datos personales se editan una sola vez y aplican a TODOS los
        // organismos, porque la persona es una sola fila. La lista de organismos, si viene, reemplaza
        // al conjunto: los que no vengan se retiran con baja lógica y dejan de estar disponibles ahí.
        var organismos = data.TransitOfficeIds is not null
            ? await ReemplazarOrganismosAsync(signer.Id, data.TransitOfficeIds, now, cancellationToken)
                .ConfigureAwait(false)
            : await OrganismosActivosAsync(signer.Id, signer.TransitOfficeId, cancellationToken)
                .ConfigureAwait(false);

        // La asignación a compañías vive por (organismo, compañía): el conjunto deseado es el producto
        // de los organismos vigentes por las compañías pedidas. Retirar un organismo (AC3) arrastra sus
        // asignaciones, que es lo que hace que el mandatario deje de estar disponible allí.
        var companiasDeseadas = Distinct(data.CompanyTenantIds).ToHashSet();
        var deseadas = organismos
            .SelectMany(officeId => companiasDeseadas.Select(companyId => (officeId, companyId)))
            .ToHashSet();

        foreach (var assignment in currentAssignments)
        {
            assignment.IsActive = deseadas.Contains((assignment.TransitOfficeId, assignment.CompanyTenantId));
        }

        var yaExistentes = currentAssignments
            .Select(c => (c.TransitOfficeId, c.CompanyTenantId))
            .ToHashSet();

        foreach (var (officeId, companyId) in deseadas.Where(p => !yaExistentes.Contains(p)))
        {
            _context.MandateSignerCompanies.Add(NewAssignment(signer.Id, officeId, companyId, now));
        }

        AddAudit(
            data.OtTenantId,
            fieldName: "updated",
            oldValue: AuditPayload(signer.Id, oldHash, oldCompanyIds),
            newValue: AuditPayload(signer.Id, data.IntegrityHash, data.CompanyTenantIds),
            changedAt: now,
            changedBy: data.UpdatedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PersistInactivateAsync(
        InactivateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: 404 si no existe o ya estaba inactivo.
        if (signer is null || !signer.IsActive)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        signer.IsActive = false;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.ChangedBy;

        // Libera las compañías: sus filas dejan de contar para el índice de exclusividad.
        var assignments = await _context.MandateSignerCompanies
            .Where(c => c.MandateSignerId == signer.Id && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var assignment in assignments)
        {
            assignment.IsActive = false;
        }

        // HU #11201 — los organismos siguen la suerte del mandatario: uno inactivo no puede seguir
        // apareciendo como disponible en ninguno de ellos.
        var offices = await _context.MandateSignerTransitOffices
            .Where(o => o.MandateSignerId == signer.Id && o.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var office in offices)
        {
            office.IsActive = false;
        }

        AddAudit(
            data.OtTenantId,
            fieldName: "is_active",
            oldValue: JsonSerializer.Serialize(true),
            newValue: JsonSerializer.Serialize(false),
            changedAt: now,
            changedBy: data.ChangedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PersistReactivateAsync(
        ReactivateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: 404 si no existe o ya estaba activo.
        if (signer is null || signer.IsActive)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        // Vuelve activo SIN restaurar compañías: las liberadas al inactivar se reasignan a mano.
        signer.IsActive = true;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.ChangedBy;

        // HU #11201 — los organismos SÍ se recuperan, pero solo el primario. Dejarlo sin ninguno lo
        // volvería invisible en todas las consolas (que listan por organismo) y no habría forma de
        // editarlo para devolvérselos: quedaría activo e inalcanzable. Con el primario reaparece donde
        // se dio de alta y desde ahí se le vuelven a asignar los demás.
        await RestaurarOrganismoPrimarioAsync(signer, now, cancellationToken).ConfigureAwait(false);

        AddAudit(
            data.OtTenantId,
            fieldName: "is_active",
            oldValue: JsonSerializer.Serialize(false),
            newValue: JsonSerializer.Serialize(true),
            changedAt: now,
            changedBy: data.ChangedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void AddAudit(
        Guid otTenantId,
        string fieldName,
        string? oldValue,
        string? newValue,
        DateTimeOffset changedAt,
        Guid? changedBy,
        Guid? correlationId)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = otTenantId,
            EntityName = EntityName,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = changedAt,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
        });
    }

    /// <summary>
    /// HU #11201 (AC3) — deja el puente con exactamente los organismos pedidos: baja lógica de los que
    /// salen, reactivación de los que vuelven (evita chocar con el histórico) y alta de los nuevos.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ReemplazarOrganismosAsync(
        Guid signerId,
        IReadOnlyList<Guid> deseados,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var objetivo = Distinct(deseados).ToHashSet();

        var existentes = await _context.MandateSignerTransitOffices
            .Where(o => o.MandateSignerId == signerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var fila in existentes)
        {
            fila.IsActive = objetivo.Contains(fila.TransitOfficeId);
        }

        var yaRepresentados = existentes.Select(o => o.TransitOfficeId).ToHashSet();
        foreach (var officeId in objetivo.Where(id => !yaRepresentados.Contains(id)))
        {
            _context.MandateSignerTransitOffices.Add(NewOffice(signerId, officeId, now));
        }

        return [.. objetivo];
    }

    /// <summary>
    /// Organismos vigentes del mandatario cuando la edición no los toca. Si el puente aún no tiene
    /// filas (dato anterior al backfill), cae al organismo primario para no dejarlo sin ninguno.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> OrganismosActivosAsync(
        Guid signerId,
        Guid primario,
        CancellationToken cancellationToken)
    {
        var activos = await _context.MandateSignerTransitOffices
            .Where(o => o.MandateSignerId == signerId && o.IsActive)
            .Select(o => o.TransitOfficeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return activos.Count == 0 ? [primario] : activos;
    }

    /// <summary>Deja activo el vínculo con el organismo primario, creándolo si no existía.</summary>
    private async Task RestaurarOrganismoPrimarioAsync(
        MandateSigner signer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var primario = await _context.MandateSignerTransitOffices
            .FirstOrDefaultAsync(
                o => o.MandateSignerId == signer.Id && o.TransitOfficeId == signer.TransitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (primario is null)
        {
            _context.MandateSignerTransitOffices.Add(NewOffice(signer.Id, signer.TransitOfficeId, now));
            return;
        }

        primario.IsActive = true;
    }

    /// <summary>
    /// Organismos efectivos de un alta: la lista si vino, y si no el primario. Nunca vacío, para que un
    /// mandatario recién creado no nazca sin ningún organismo donde firmar.
    /// </summary>
    private static IReadOnlyList<Guid> OrganismosDe(IReadOnlyList<Guid>? ids, Guid primario)
    {
        var lista = Distinct(ids ?? []);
        return lista.Count == 0 ? [primario] : lista;
    }

    private static MandateSignerTransitOffice NewOffice(
        Guid signerId, Guid transitOfficeId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = transitOfficeId,
            IsActive = true,
            CreatedAt = now,
        };

    private static MandateSignerCompany NewAssignment(
        Guid signerId, Guid transitOfficeId, Guid companyTenantId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = transitOfficeId,
            CompanyTenantId = companyTenantId,
            IsActive = true,
            CreatedAt = now,
        };

    /// <summary>Payload de auditoría sin PII: id, huella de integridad y compañías.</summary>
    private static string AuditPayload(Guid signerId, string integrityHash, IReadOnlyList<Guid> companyTenantIds) =>
        JsonSerializer.Serialize(new
        {
            mandateSignerId = signerId,
            integrityHash,
            companyTenantIds = Distinct(companyTenantIds),
        });

    private static IReadOnlyList<Guid> Distinct(IReadOnlyList<Guid> ids) =>
        ids is null ? [] : [.. ids.Distinct()];

    /// <summary>
    /// Ejecuta <paramref name="persist"/> bajo el contexto RLS del tenant OT. En proveedor
    /// relacional abre transacción + <c>set_config</c>; en InMemory delega directo (un único
    /// <c>SaveChanges</c> atómico).
    /// </summary>
    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid otTenantId,
        Func<Task<T>> persist,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {otTenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await persist().ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await persist().ConfigureAwait(false);
    }
}
