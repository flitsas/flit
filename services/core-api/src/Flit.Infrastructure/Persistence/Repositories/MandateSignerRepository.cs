using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
internal sealed partial class MandateSignerRepository : IMandateSignerRepository
{
    private const string EntityName = "mandate_signer";
    private const string OnePerOriginIndex = "uq_mandate_signer_companies_one_per_origin";

    private readonly FlitDbContext _context;
    private readonly IMandateSignerProcedureReassigner? _reassigner;

    /// <param name="reassigner">
    /// HU #13137 — reasigna los trámites radicados sin aprobar al dar de baja. Opcional para los sitios que
    /// construyen el repositorio a mano (tests): sin él la baja no toca trámites.
    /// </param>
    public MandateSignerRepository(
        FlitDbContext context,
        IMandateSignerProcedureReassigner? reassigner = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _reassigner = reassigner;
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

    public Task<MandateSignerLifecycleResult> InactivateAsync(
        InactivateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistRetireAsync(
                data.MandateSignerId, data.OtTenantId, data.ChangedBy, data.CorrelationId, data.ActorKind,
                delete: false, cancellationToken),
            cancellationToken);
    }

    public Task<MandateSignerLifecycleResult> DeleteAsync(
        DeleteMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistRetireAsync(
                data.MandateSignerId, data.OtTenantId, data.ChangedBy, data.CorrelationId, data.ActorKind,
                delete: true, cancellationToken),
            cancellationToken);
    }

    public Task<MandateSignerLifecycleResult> ReactivateAsync(
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
            SignatureVaultId = data.SignatureVaultId,
            RegisteredAt = data.RegisteredAt,
            IsActive = true,
            SignerModel = data.SignerModel,
            SignatureMethod = data.SignatureMethod,
            ValidityKind = data.ValidityKind,
            ValidFrom = data.ValidFrom,
            ValidTo = data.ValidTo,
            CreatedAt = now,
            CreatedBy = data.CreatedBy,
        });

        // HU #11201 — los organismos van al puente. Sin lista, el único organismo es el primario, que
        // es exactamente lo que manda el alta desde el perfil del organismo.
        var offices = OrganismosDe(data.TransitOfficeIds, data.TransitOfficeId);
        // HU #13131 (ADR-0061): la firma física ya no se persiste como exención; todas las altas nacen en false.
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
                _context.MandateSignerCompanies.Add(NewAssignment(signerId, officeId, companyId, now, data.ConfiguredByScope));
            }
        }

        EscribirEmpresasRepresentadas(signerId, data.OfficeCompanies, now);

        AddAudit(
            data.OtTenantId,
            fieldName: "created",
            oldValue: null,
            newValue: AuditPayload(signerId, data.IntegrityHash, data.CompanyTenantIds),
            changedAt: now,
            changedBy: data.CreatedBy,
            correlationId: data.CorrelationId);

        await SaveConTraduccionDeUnicidadAsync(cancellationToken).ConfigureAwait(false);
        return signerId;
    }

    private async Task<bool> PersistUpdateAsync(
        UpdateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        if (signer is null || !signer.IsActive || signer.DeletedAt is not null)
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
        // HU #13129 — modelo, forma de firma y vigencia propia (ya validados y normalizados).
        signer.SignerModel = data.SignerModel;
        signer.SignatureMethod = data.SignatureMethod;
        signer.ValidityKind = data.ValidityKind;
        signer.ValidFrom = data.ValidFrom;
        signer.ValidTo = data.ValidTo;
        // La columna existía desde la HU #10910 pero NADIE la escribía: el trámite resolvía la firma
        // por documento y esta referencia quedaba siempre nula. Solo se toca si el llamante la
        // gestiona: escribirla siempre haría que un guardado desde el perfil del organismo —que no
        // maneja este campo— borrara la firma que la compañía acababa de elegir.
        if (data.ActualizaFirma)
        {
            signer.SignatureVaultId = data.SignatureVaultId;
        }
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.UpdatedBy;

        // HU #11201 (AC2/AC3) — los datos personales se editan una sola vez y aplican a TODOS los
        // organismos, porque la persona es una sola fila. La lista de organismos, si viene, reemplaza
        // al conjunto: los que no vengan se retiran con baja lógica y dejan de estar disponibles ahí.
        var organismos = data.TransitOfficeIds is not null
            ? await ReemplazarOrganismosAsync(
                    signer.Id, data.TransitOfficeIds, now, cancellationToken)
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
            var quedaActivo = deseadas.Contains((assignment.TransitOfficeId, assignment.CompanyTenantId));
            // HU #13195 — un vínculo que se reactiva toma el origen de quien actúa; los que no se tocan lo conservan.
            if (quedaActivo && !assignment.IsActive)
            {
                assignment.ConfiguredByScope = data.ConfiguredByScope;
            }

            assignment.IsActive = quedaActivo;
        }

        var yaExistentes = currentAssignments
            .Select(c => (c.TransitOfficeId, c.CompanyTenantId))
            .ToHashSet();

        foreach (var (officeId, companyId) in deseadas.Where(p => !yaExistentes.Contains(p)))
        {
            _context.MandateSignerCompanies.Add(NewAssignment(signer.Id, officeId, companyId, now, data.ConfiguredByScope));
        }

        await ReemplazarEmpresasRepresentadasAsync(
            signer.Id, data.OfficeCompanies, now, cancellationToken).ConfigureAwait(false);

        AddAudit(
            data.OtTenantId,
            fieldName: "updated",
            oldValue: AuditPayload(signer.Id, oldHash, oldCompanyIds),
            newValue: AuditPayload(signer.Id, data.IntegrityHash, data.CompanyTenantIds),
            changedAt: now,
            changedBy: data.UpdatedBy,
            correlationId: data.CorrelationId);

        await SaveConTraduccionDeUnicidadAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Baja del mandatario, inactivación (<paramref name="delete"/> = false) o eliminación lógica (true), con sus
    /// efectos en UNA transacción (HU #13135, #13137, #13138): libera vínculos y organismos, retira los defaults
    /// que apuntan a él (no se limpian solos: la baja lógica no dispara el <c>ON DELETE SET NULL</c>), reasigna
    /// los trámites radicados sin aprobar con la prelación del OT y deja la bitácora. La traza de vínculos y
    /// defaults retirados queda en el evento de baja y permite restaurarlos al reactivar (HU #13136).
    /// </summary>
    private async Task<MandateSignerLifecycleResult> PersistRetireAsync(
        Guid mandateSignerId,
        Guid otTenantId,
        Guid? changedBy,
        Guid? correlationId,
        MandateSignerActorKind actor,
        bool delete,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == mandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: no existe, ya eliminado o (inactivar) ya inactivo => nada que hacer (404).
        if (signer is null || signer.DeletedAt is not null || (!delete && !signer.IsActive))
        {
            return MandateSignerLifecycleResult.NotApplied;
        }

        // Los defaults de compañía y los trámites viven en tenants ajenos al del OT: lectura y escritura
        // cross-tenant dentro de esta transacción (mismo patrón que los readers de administración OT).
        await BypassRowSecurityAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        signer.IsActive = false;
        signer.UpdatedAt = now;
        signer.UpdatedBy = changedBy;
        if (delete)
        {
            signer.DeletedAt = now;
            signer.DeletedBy = changedBy;
        }

        // Libera las compañías: sus filas dejan de contar para el índice de exclusividad. Se anotan para poder
        // restaurarlas al reactivar (las que ya estaban inactivas por una edición NO se restauran).
        var assignments = await _context.MandateSignerCompanies
            .Where(c => c.MandateSignerId == signer.Id && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var retiredLinks = assignments
            .Select(a => new MandateSignerLinkRef(a.TransitOfficeId, a.CompanyTenantId))
            .ToList();
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
        var retiredOffices = offices.Select(o => o.TransitOfficeId).ToList();
        foreach (var office in offices)
        {
            office.IsActive = false;
        }

        // HU #13135 — retira TODOS los defaults que apuntan al mandatario (compañía x organismo y general del
        // organismo). Sin esto quedarían apuntando a alguien que ya no firma.
        var retiredDefaults = await RetireDefaultsAsync(signer.Id, changedBy, now, cancellationToken)
            .ConfigureAwait(false);

        // Se guarda ANTES de reasignar: el evaluador lee el directorio de la misma conexión y debe ver al
        // mandatario ya fuera de juego.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // HU #13137 — tramites radicados sin aprobar que apuntaban al mandatario: prelación del OT o, si no
        // queda nadie, nulo para que el OT decida al aprobar. Si falla, se revierte toda la transacción.
        var reassignment = await ReassignProceduresAsync(signer.Id, cancellationToken).ConfigureAwait(false);

        if (!delete)
        {
            // Fila histórica de la baja (se conserva: la consumen la auditoría existente y sus pruebas).
            AddAudit(
                otTenantId,
                fieldName: "is_active",
                oldValue: JsonSerializer.Serialize(true),
                newValue: JsonSerializer.Serialize(false),
                changedAt: now,
                changedBy: changedBy,
                correlationId: correlationId);
        }

        WriteRetirementAudit(
            otTenantId, signer.Id, delete, now, changedBy, correlationId, actor,
            retiredOffices, retiredLinks, retiredDefaults, reassignment);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MandateSignerLifecycleResult(
            Applied: true,
            reassignment,
            RestoredLinks: [],
            ConflictLinks: [],
            RetiredDefaults: retiredDefaults.Count,
            RestoredDefaults: 0);
    }

    private async Task<List<MandateSignerDefaultRef>> RetireDefaultsAsync(
        Guid signerId,
        Guid? changedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var retired = new List<MandateSignerDefaultRef>();

        var rules = await _context.CompanyOtMandateRules
            .Where(r => r.DefaultMandateSignerId == signerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var rule in rules)
        {
            rule.DefaultMandateSignerId = null;
            rule.UpdatedAt = now;
            rule.UpdatedBy = changedBy;
            retired.Add(new MandateSignerDefaultRef(
                MandateSignerDefaultRef.CompanyRule, rule.TransitOfficeId, rule.CompanyTenantId));
        }

        var configs = await _context.TransitOfficeMandateConfigs
            .Where(c => c.DefaultMandateSignerId == signerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var config in configs)
        {
            config.DefaultMandateSignerId = null;
            config.UpdatedAt = now;
            config.UpdatedBy = changedBy;
            retired.Add(new MandateSignerDefaultRef(MandateSignerDefaultRef.Office, config.TransitOfficeId, null));
        }

        return retired;
    }

    private async Task BypassRowSecurityAsync(CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// HU #13195 (ADR-0066 D1) — guarda y traduce el rechazo del índice único parcial
    /// <c>uq_mandate_signer_companies_one_per_origin</c> (un solo vínculo activo por organismo, compañía y
    /// grupo de origen) a <see cref="MandateSignerActiveLinkConflictException"/>, que la API responde como 409.
    /// </summary>
    private async Task SaveConTraduccionDeUnicidadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: OnePerOriginIndex,
            })
        {
            throw new MandateSignerActiveLinkConflictException(
                MandateSignerActiveLinkConflictException.DefaultMessage, ex);
        }
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
            // HU #13131 (ADR-0061): la marca histórica signs_physically NO se toca al editar. La firma física
            // ya no se ofrece ni se persiste, pero las filas existentes se conservan intactas y el resolver
            // de trámites mantiene su comportamiento hasta que F4 active el bloqueo (la columna se retira en F8).
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

    /// <summary>
    /// Empresas representadas por organismo en el ALTA. Sin lista no se escribe nada, y esa ausencia
    /// significa "aplica a todas": es como se comportan los mandatarios que ya existen.
    /// </summary>
    private void EscribirEmpresasRepresentadas(
        Guid signerId, IReadOnlyList<MandateSignerOfficeCompanies>? officeCompanies, DateTimeOffset now)
    {
        foreach (var porOrganismo in officeCompanies ?? [])
        {
            foreach (var companyId in Distinct(porOrganismo.RepresentedCompanyIds))
            {
                _context.MandateSignerRepresentedCompanies.Add(new MandateSignerRepresentedCompany
                {
                    Id = Guid.NewGuid(),
                    MandateSignerId = signerId,
                    TransitOfficeId = porOrganismo.TransitOfficeId,
                    RepresentedCompanyId = companyId,
                    IsActive = true,
                    CreatedAt = now,
                });
            }
        }
    }

    /// <summary>
    /// Reemplaza las empresas representadas del mandatario. <c>null</c> ⇒ no se tocan (la edición desde
    /// el perfil del organismo no gestiona este campo, y escribir sobre él le borraría a la compañía lo
    /// que acaba de elegir). Una lista reemplaza el conjunto: lo que no venga se retira con baja lógica.
    /// </summary>
    private async Task ReemplazarEmpresasRepresentadasAsync(
        Guid signerId,
        IReadOnlyList<MandateSignerOfficeCompanies>? officeCompanies,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (officeCompanies is null)
        {
            return;
        }

        var deseadas = officeCompanies
            .SelectMany(o => Distinct(o.RepresentedCompanyIds).Select(c => (o.TransitOfficeId, Company: c)))
            .ToHashSet();

        var existentes = await _context.MandateSignerRepresentedCompanies
            .Where(x => x.MandateSignerId == signerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var fila in existentes)
        {
            fila.IsActive = deseadas.Contains((fila.TransitOfficeId, fila.RepresentedCompanyId));
        }

        var yaExistentes = existentes.Select(x => (x.TransitOfficeId, Company: x.RepresentedCompanyId)).ToHashSet();
        foreach (var (officeId, companyId) in deseadas.Where(p => !yaExistentes.Contains(p)))
        {
            _context.MandateSignerRepresentedCompanies.Add(new MandateSignerRepresentedCompany
            {
                Id = Guid.NewGuid(),
                MandateSignerId = signerId,
                TransitOfficeId = officeId,
                RepresentedCompanyId = companyId,
                IsActive = true,
                CreatedAt = now,
            });
        }
    }

    private static MandateSignerTransitOffice NewOffice(
        Guid signerId, Guid transitOfficeId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = transitOfficeId,
            IsActive = true,
            SignsPhysically = false,
            CreatedAt = now,
        };

    private static MandateSignerCompany NewAssignment(
        Guid signerId, Guid transitOfficeId, Guid companyTenantId, DateTimeOffset now, string configuredByScope) =>
        new()
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = transitOfficeId,
            CompanyTenantId = companyTenantId,
            IsActive = true,
            ConfiguredByScope = configuredByScope,
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

    private static IReadOnlyList<Guid> Distinct(IReadOnlyList<Guid>? ids) =>
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
