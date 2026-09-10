using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas EF Core de mandatarios y compañías de un organismo de tránsito (ADR-0023).
/// <c>admin.mandate_signers</c> y <c>admin.mandate_signer_companies</c> se acotan en la capa
/// de aplicación por <c>transit_office_id</c>; <c>admin.tenant_transit_office_grants</c> e
/// <c>identity.tenants</c> tienen RLS por tenant y aquí se leen cross-tenant (gestión OT del
/// SuperAdmin / ot_admin) con <c>SET LOCAL row_security = off</c>, igual que
/// <see cref="DbTransitOfficeOperationalStatusReader"/>. Las fuentes se materializan por
/// separado y se combinan en memoria para comportarse igual en PostgreSQL e InMemory.
/// </summary>
internal sealed class DbMandateSignerReader : IMandateSignerReader
{
    private readonly FlitDbContext _context;
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IdentityVigenciaPorDocumentoResolver _identityResolver;

    public DbMandateSignerReader(
        FlitDbContext context,
        ITransitOfficeOperationalStatusReader? otStatus = null,
        IdentityVigenciaPorDocumentoResolver? identityResolver = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // HU #11765 (ADR-0050) — igual patrón que DbLegalRepresentativeReader: opcionales para no romper
        // los sitios que construyen el reader directamente (tests, ~5 archivos que ni siquiera aseveran
        // IdentityStatus). Por DI llegan las implementaciones reales (ambas ya registradas).
        _otStatus = otStatus ?? new DbTransitOfficeOperationalStatusReader(context);
        _identityResolver = identityResolver
            ?? new IdentityVigenciaPorDocumentoResolver(new ProcedureInstanceRepository(context));
    }

    public Task<IReadOnlyList<MandateSignerItem>> ListByOtAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                // HU #11201 — quién aplica en este organismo lo dice el puente, no la columna del
                // mandatario. Activos e inactivos (baja lógica): los inactivados siguen visibles para
                // poder reactivarlos. Se muestran primero los activos, luego por nombre.
                var idsDelOrganismo = await IdsPorOrganismoAsync(transitOfficeId, cancellationToken)
                    .ConfigureAwait(false);

                var candidatos = await _context.MandateSigners
                    .AsNoTracking()
                    .Where(s => idsDelOrganismo.Keys.Contains(s.Id))
                    .OrderByDescending(s => s.IsActive)
                    .ThenBy(s => s.FullName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Un vínculo inactivo puede significar dos cosas distintas: que se retiró el organismo
                // (AC3 ⇒ ya no aplica ahí) o que se inactivó la persona (⇒ sigue visible para poder
                // reactivarla). Se distinguen por el estado del propio mandatario.
                var signers = candidatos
                    .Where(s => idsDelOrganismo[s.Id] || !s.IsActive)
                    .ToList();

                var companiesBySigner = await LoadActiveCompanyIdsBySignerAsync(
                    transitOfficeId, cancellationToken).ConfigureAwait(false);

                var officesBySigner = await LoadOfficeIdsBySignerAsync(
                    [.. signers.Select(s => s.Id)], cancellationToken).ConfigureAwait(false);

                var vigenciaBySigner = await LoadIdentityVigenciaAsync(
                    signers, cancellationToken).ConfigureAwait(false);

                var physicalBySigner = await LoadPhysicalOfficeIdsBySignerAsync(
                    [.. signers.Select(s => s.Id)], cancellationToken).ConfigureAwait(false);

                IReadOnlyList<MandateSignerItem> items =
                [
                    .. signers.Select(s =>
                        Project(s, companiesBySigner, officesBySigner, vigenciaBySigner, physicalBySigner)),
                ];
                return items;
            },
            cancellationToken);

    public Task<MandateSignerItem?> GetByIdAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                var signer = await _context.MandateSigners
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == mandateSignerId, cancellationToken)
                    .ConfigureAwait(false);

                if (signer is null)
                {
                    return null;
                }

                var companyIds = await _context.MandateSignerCompanies
                    .AsNoTracking()
                    .Where(c => c.MandateSignerId == mandateSignerId && c.IsActive)
                    .Select(c => c.CompanyTenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var officesBySigner = await LoadOfficeIdsBySignerAsync(
                    [signer.Id], cancellationToken).ConfigureAwait(false);

                var vigenciaBySigner = await LoadIdentityVigenciaAsync(
                    [signer], cancellationToken).ConfigureAwait(false);

                var physicalBySigner = await LoadPhysicalOfficeIdsBySignerAsync(
                    [signer.Id], cancellationToken).ConfigureAwait(false);

                return new MandateSignerItem
                {
                    Id = signer.Id,
                    TransitOfficeId = signer.TransitOfficeId,
                    TransitOfficeIds = officesBySigner.GetValueOrDefault(signer.Id, []),
                    PhysicalSignatureOfficeIds = physicalBySigner.GetValueOrDefault(signer.Id, []),
                    FullName = signer.FullName,
                    DocumentType = signer.DocumentType,
                    DocumentNumber = signer.DocumentNumber,
                    IntegrityHash = signer.IntegrityHash,
                    Email = signer.Email,
                    SignatureVaultId = signer.SignatureVaultId,
                    IdentityValidationRef = signer.IdentityValidationRef,
                    IdentityStatus = vigenciaBySigner
                        .GetValueOrDefault(signer.Id, new AdminIdentityVigencia.Resultado(
                            AdminIdentityVigencia.None, null)).Status,
                    IdentityValidUntil = vigenciaBySigner
                        .GetValueOrDefault(signer.Id, new AdminIdentityVigencia.Resultado(
                            AdminIdentityVigencia.None, null)).ValidUntil,
                    UserId = signer.UserId,
                    RegisteredAt = signer.RegisteredAt,
                    IsActive = signer.IsActive,
                    CompanyTenantIds = companyIds,
                };
            },
            cancellationToken);

    public Task<IReadOnlyList<MandateSignerItem>> ListByCompanyAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                // HU #11202 — la vista inversa: los mandatarios que esta compañía registró, en todos
                // sus organismos. Se toman los ids de las asignaciones activas; el estado de la persona
                // se decide con su propia bandera (los inactivos siguen a la vista para reactivarlos).
                var signerIds = await _context.MandateSignerCompanies
                    .AsNoTracking()
                    .Where(c => c.CompanyTenantId == companyTenantId)
                    .Select(c => c.MandateSignerId)
                    .Distinct()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (signerIds.Count == 0)
                {
                    return [];
                }

                var signers = await _context.MandateSigners
                    .AsNoTracking()
                    .Where(s => signerIds.Contains(s.Id))
                    .OrderByDescending(s => s.IsActive)
                    .ThenBy(s => s.FullName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var companiesBySigner = await LoadCompanyIdsBySignerAsync(signerIds, cancellationToken)
                    .ConfigureAwait(false);
                var officesBySigner = await LoadOfficeIdsBySignerAsync(signerIds, cancellationToken)
                    .ConfigureAwait(false);
                var vigenciaBySigner = await LoadIdentityVigenciaAsync(signers, cancellationToken)
                    .ConfigureAwait(false);
                var physicalBySigner = await LoadPhysicalOfficeIdsBySignerAsync(signerIds, cancellationToken)
                    .ConfigureAwait(false);
                var companiesByOffice = await LoadOfficeCompaniesAsync(signerIds, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MandateSignerItem> items =
                [
                    .. signers.Select(s => Project(
                        s, companiesBySigner, officesBySigner, vigenciaBySigner, physicalBySigner,
                        companiesByOffice)),
                ];
                return items;
            },
            cancellationToken);

    public Task<IReadOnlyList<CompanyTransitOfficeOption>> ListCompanyTransitOfficesAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                var officeIds = await _context.TenantTransitOfficeGrants
                    .AsNoTracking()
                    .Where(g => g.TenantId == companyTenantId && g.IsEnabled)
                    .Select(g => g.TransitOfficeId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (officeIds.Count == 0)
                {
                    return [];
                }

                // Solo organismos ACTIVOS del catálogo: uno desactivado no sirve para radicar, así que
                // ofrecerlo como destino de un mandatario sería ofrecer algo inservible.
                IReadOnlyList<CompanyTransitOfficeOption> options =
                [
                    .. await _context.TransitOffices
                        .AsNoTracking()
                        .Where(o => officeIds.Contains(o.Id) && o.IsActive)
                        .OrderBy(o => o.Name)
                        .Select(o => new CompanyTransitOfficeOption
                        {
                            TransitOfficeId = o.Id,
                            Code = o.Code,
                            Name = o.Name,
                        })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false),
                ];
                return options;
            },
            cancellationToken);

    public Task<IReadOnlyList<OtCompanyOption>> ListOtCompaniesAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                var grants = await _context.TenantTransitOfficeGrants
                    .AsNoTracking()
                    .Where(g => g.TransitOfficeId == transitOfficeId)
                    .Select(g => new { g.TenantId, g.IsEnabled })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var tenantIds = grants.Select(g => g.TenantId).Distinct().ToList();
                var tenants = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => tenantIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.LegalName, t.IsActive })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var tenantById = tenants.ToDictionary(t => t.Id);

                IReadOnlyList<OtCompanyOption> options =
                [
                    .. grants
                        .Where(g => tenantById.ContainsKey(g.TenantId))
                        .Select(g =>
                        {
                            var tenant = tenantById[g.TenantId];
                            return new OtCompanyOption
                            {
                                CompanyTenantId = tenant.Id,
                                LegalName = tenant.LegalName,
                                IsActive = tenant.IsActive,
                                IsEnabled = g.IsEnabled,
                            };
                        })
                        .OrderBy(o => o.LegalName),
                ];
                return options;
            },
            cancellationToken);

    public Task<IReadOnlyList<MandateSignerCompanyResolution>> ListActiveCompanyResolutionsAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                // HU #11201 — el organismo sale del puente; aquí solo cuentan los vínculos activos,
                // porque esta lectura es "quién firma hoy por cada compañía en este organismo".
                var idsDelOrganismo = await IdsPorOrganismoAsync(transitOfficeId, cancellationToken)
                    .ConfigureAwait(false);
                var activosEnElOrganismo = idsDelOrganismo
                    .Where(p => p.Value)
                    .Select(p => p.Key)
                    .ToList();

                var signers = await _context.MandateSigners
                    .AsNoTracking()
                    .Where(s => activosEnElOrganismo.Contains(s.Id) && s.IsActive)
                    .Select(s => new { s.Id, s.FullName, s.IntegrityHash })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var signerById = signers.ToDictionary(s => s.Id);

                var assignments = await _context.MandateSignerCompanies
                    .AsNoTracking()
                    .Where(c => c.TransitOfficeId == transitOfficeId && c.IsActive)
                    .Select(c => new { c.CompanyTenantId, c.MandateSignerId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MandateSignerCompanyResolution> resolutions =
                [
                    .. assignments
                        .Where(a => signerById.ContainsKey(a.MandateSignerId))
                        .Select(a =>
                        {
                            var signer = signerById[a.MandateSignerId];
                            return new MandateSignerCompanyResolution
                            {
                                CompanyTenantId = a.CompanyTenantId,
                                MandateSignerId = signer.Id,
                                FullName = signer.FullName,
                                IntegrityHash = signer.IntegrityHash,
                            };
                        }),
                ];
                return resolutions;
            },
            cancellationToken);

    private async Task<Dictionary<Guid, List<Guid>>> LoadActiveCompanyIdsBySignerAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.MandateSignerCompanies
            .AsNoTracking()
            .Where(c => c.TransitOfficeId == transitOfficeId && c.IsActive)
            .Select(c => new { c.MandateSignerId, c.CompanyTenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.MandateSignerId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.CompanyTenantId).ToList());
    }

    /// <summary>
    /// HU #11201 — mandatarios vinculados a un organismo, con el estado del vínculo. Se devuelven
    /// también los inactivos: el llamador decide si un vínculo inactivo significa "se retiró el
    /// organismo" o "se inactivó la persona".
    /// </summary>
    private async Task<Dictionary<Guid, bool>> IdsPorOrganismoAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.MandateSignerTransitOffices
            .AsNoTracking()
            .Where(o => o.TransitOfficeId == transitOfficeId)
            .Select(o => new { o.MandateSignerId, o.IsActive })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.MandateSignerId)
            .ToDictionary(g => g.Key, g => g.Any(r => r.IsActive));
    }

    /// <summary>Compañías activas de cada mandatario, sin acotar por organismo (vista de la empresa).</summary>
    private async Task<Dictionary<Guid, List<Guid>>> LoadCompanyIdsBySignerAsync(
        List<Guid> signerIds,
        CancellationToken cancellationToken)
    {
        var rows = await _context.MandateSignerCompanies
            .AsNoTracking()
            .Where(c => signerIds.Contains(c.MandateSignerId) && c.IsActive)
            .Select(c => new { c.MandateSignerId, c.CompanyTenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.MandateSignerId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.CompanyTenantId).Distinct().ToList());
    }

    /// <summary>Organismos ACTIVOS de cada mandatario, para pintarlos en la consola de gestión.</summary>
    /// <summary>Empresas representadas por (mandatario, organismo).</summary>
    private async Task<Dictionary<Guid, List<MandateSignerOfficeCompanies>>> LoadOfficeCompaniesAsync(
        List<Guid> signerIds,
        CancellationToken cancellationToken)
    {
        if (signerIds.Count == 0)
        {
            return [];
        }

        var rows = await _context.MandateSignerRepresentedCompanies
            .AsNoTracking()
            .Where(x => signerIds.Contains(x.MandateSignerId) && x.IsActive)
            .Select(x => new { x.MandateSignerId, x.TransitOfficeId, x.RepresentedCompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.MandateSignerId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.TransitOfficeId)
                    .Select(o => new MandateSignerOfficeCompanies(
                        o.Key, [.. o.Select(r => r.RepresentedCompanyId)]))
                    .ToList());
    }

    /// <summary>Organismos donde el mandatario firma a mano, por mandatario.</summary>
    private async Task<Dictionary<Guid, List<Guid>>> LoadPhysicalOfficeIdsBySignerAsync(
        List<Guid> signerIds,
        CancellationToken cancellationToken)
    {
        if (signerIds.Count == 0)
        {
            return [];
        }

        var rows = await _context.MandateSignerTransitOffices
            .AsNoTracking()
            .Where(o => signerIds.Contains(o.MandateSignerId) && o.IsActive && o.SignsPhysically)
            .Select(o => new { o.MandateSignerId, o.TransitOfficeId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.MandateSignerId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.TransitOfficeId).ToList());
    }

    private async Task<Dictionary<Guid, List<Guid>>> LoadOfficeIdsBySignerAsync(
        List<Guid> signerIds,
        CancellationToken cancellationToken)
    {
        if (signerIds.Count == 0)
        {
            return [];
        }

        var rows = await _context.MandateSignerTransitOffices
            .AsNoTracking()
            .Where(o => signerIds.Contains(o.MandateSignerId) && o.IsActive)
            .Select(o => new { o.MandateSignerId, o.TransitOfficeId, o.SignsPhysically })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.MandateSignerId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.TransitOfficeId).ToList());
    }

    private static MandateSignerItem Project(
        Entities.Admin.MandateSigner signer,
        Dictionary<Guid, List<Guid>> companiesBySigner,
        Dictionary<Guid, List<Guid>> officesBySigner,
        Dictionary<Guid, AdminIdentityVigencia.Resultado> vigenciaBySigner,
        Dictionary<Guid, List<Guid>>? physicalBySigner = null,
        Dictionary<Guid, List<MandateSignerOfficeCompanies>>? companiesByOffice = null)
    {
        var vigencia = vigenciaBySigner.GetValueOrDefault(
            signer.Id, new AdminIdentityVigencia.Resultado(AdminIdentityVigencia.None, null));

        return new MandateSignerItem
        {
            Id = signer.Id,
            TransitOfficeId = signer.TransitOfficeId,
            FullName = signer.FullName,
            DocumentType = signer.DocumentType,
            DocumentNumber = signer.DocumentNumber,
            IntegrityHash = signer.IntegrityHash,
            Email = signer.Email,
            SignatureVaultId = signer.SignatureVaultId,
            IdentityValidationRef = signer.IdentityValidationRef,
            IdentityStatus = vigencia.Status,
            IdentityValidUntil = vigencia.ValidUntil,
            UserId = signer.UserId,
            RegisteredAt = signer.RegisteredAt,
            IsActive = signer.IsActive,
            CompanyTenantIds = companiesBySigner.GetValueOrDefault(signer.Id, []),
            TransitOfficeIds = officesBySigner.GetValueOrDefault(signer.Id, []),
            PhysicalSignatureOfficeIds = physicalBySigner?.GetValueOrDefault(signer.Id, []) ?? [],
            OfficeCompanies = companiesByOffice?.GetValueOrDefault(signer.Id, []) ?? [],
        };
    }

    /// <summary>
    /// HU #11765 (ADR-0050) — vigencia de identidad por mandatario, leída EXCLUSIVAMENTE del módulo
    /// Identidad (<c>tramites.procedure_instance_biometric_validations</c>) a través de
    /// <see cref="IdentityVigenciaPorDocumentoResolver"/>. Ya NO consulta
    /// <c>admin.admin_identity_validations</c> (tabla abandonada, ADR-0034 superada): era la otra mitad
    /// del defecto raíz de esta HU — <c>MandateSignerDirectory</c> (HU #11752, ruta de RADICACIÓN) ya
    /// había migrado, pero la ficha admin (esta clase) seguía leyendo la tabla vieja y su rótulo no se
    /// movía al prevalidar desde Identidad.
    /// <para>
    /// <b>Tenant.</b> La identidad de un mandatario vive en el tenant PROPIO del organismo donde está
    /// registrado (<c>signer.TransitOfficeId</c>), NO en la compañía gestora que consulta la ficha —
    /// mismo mecanismo que <c>MandateSignerDirectory.LoadVigentIdentitiesAsync</c> (HU #11752), resuelto
    /// con <see cref="ITransitOfficeOperationalStatusReader"/>. Se agrupa por organismo para resolver el
    /// tenant una sola vez por OT y no repetir la consulta de perfil por cada mandatario.
    /// </para>
    /// <para>
    /// <b>Lote.</b> Dentro de cada grupo por organismo, UNA sola consulta SQL para todos sus documentos
    /// vía <see cref="IdentityVigenciaPorDocumentoResolver.ResolveManyBatchedAsync"/> (sin N+1). Un OT sin
    /// tenant operativo (<c>HasTenant=false</c>) no tiene contra qué resolver identidad: sus mandatarios
    /// quedan en <see cref="AdminIdentityVigencia.None"/>, igual que antes cuando no había fila admin.
    /// </para>
    /// <para>
    /// El estado ADR-0050 se traduce al vocabulario histórico del contrato con
    /// <see cref="IdentityVigenciaLegacyMapper"/> (compartido con <c>DbLegalRepresentativeReader</c>).
    /// </para>
    /// <para>
    /// Se lee dentro del scope cross-tenant (row_security off) que abre <c>ExecuteCrossTenantReadAsync</c>;
    /// la tabla de Identidad no lo necesita (aislamiento por <c>WHERE tenant_id</c> explícito, igual que
    /// el endpoint de la HU #11751), pero tampoco estorba correr ahí dentro.
    /// </para>
    /// </summary>
    private async Task<Dictionary<Guid, AdminIdentityVigencia.Resultado>> LoadIdentityVigenciaAsync(
        List<Entities.Admin.MandateSigner> signers,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, AdminIdentityVigencia.Resultado>();
        if (signers.Count == 0)
        {
            return result;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var group in signers.GroupBy(s => s.TransitOfficeId))
        {
            var status = await _otStatus.GetByIdAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (status is null || !status.HasTenant || status.TenantId is not { } otTenantId)
            {
                // Sin tenant operativo del OT no hay contra qué resolver identidad (igual que antes:
                // sin fila ⇒ None). El resolver de Identidad NO se invoca para este grupo.
                continue;
            }

            var documentos = group
                .Select(s => (s.DocumentType, s.DocumentNumber))
                .Distinct()
                .ToList();

            var resueltos = await _identityResolver
                .ResolveManyBatchedAsync(otTenantId, documentos, now, cancellationToken)
                .ConfigureAwait(false);

            foreach (var s in group)
            {
                var key = DocumentCanonicalNormalization.IdentidadKey(otTenantId, s.DocumentType, s.DocumentNumber);
                var resultado = resueltos.GetValueOrDefault(key, IdentityVigenciaResult.SinValidacion);
                result[s.Id] = IdentityVigenciaLegacyMapper.ToLegacyResultado(resultado);
            }
        }

        return result;
    }

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        // HU #11000 — misma guarda que MandateSignerDirectory (HU #10992) y PlateRangeRepository: dentro de
        // una transacción ya abierta NO se puede anidar otra ("The connection is already in a transaction").
        // Se aplica el SET LOCAL sobre la transacción en curso; muere con su commit.
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
                    "SET LOCAL row_security = off",
                    cancellationToken).ConfigureAwait(false);

                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
