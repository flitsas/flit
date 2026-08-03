using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas EF Core del directorio de representantes legales por compañía (HU #10900, ADR-0033).
/// Tenant-scoped: corre bajo el contexto RLS del tenant (<see cref="TenantRlsScope"/>). Proyecta la
/// compañía representada (join) y los tipos de trámite del puente. <c>DocumentNumber</c> es PII (Ley
/// 1581): no loguear.
/// </summary>
internal sealed class DbLegalRepresentativeReader : ILegalRepresentativeReader
{
    // Hora de Colombia (UTC-5, sin DST): el estado de vigencia se cuenta por día calendario local,
    // coherente con el resto del cálculo de vigencia de escrituras (ADR-0033).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly FlitDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DbLegalRepresentativeReader(FlitDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // TimeProvider es opcional para no romper los ~15 sitios que construyen el reader directamente
        // (tests) ni la resolución por DI (TimeProvider.System está registrado como singleton).
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<PagedResult<LegalRepresentativeItem>> ListPagedAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var baseQuery = _context.CompanyLegalRepresentatives
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId);

                var totalCount = await baseQuery.LongCountAsync(cancellationToken).ConfigureAwait(false);
                if (totalCount == 0)
                {
                    return PagedResult<LegalRepresentativeItem>.Empty;
                }

                var pageRows = await baseQuery
                    .OrderByDescending(r => r.CreatedAt)
                    .ThenByDescending(r => r.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var items = await ProjectAsync(pageRows, cancellationToken).ConfigureAwait(false);
                return new PagedResult<LegalRepresentativeItem>(items, totalCount);
            },
            cancellationToken);

    public Task<LegalRepresentativeItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var row = await _context.CompanyLegalRepresentatives
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                // Detalle: incluye el historial de escrituras por compañía (HU #10933).
                var items = await ProjectAsync([row], cancellationToken, includeDeeds: true)
                    .ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);

    public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAndDocumentAsync(
        Guid tenantId,
        string companyNit,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyNit);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        var nit = companyNit.Trim();
        var docType = documentType.Trim();
        var doc = documentNumber.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var reps = await ActiveRepsByNitAsync(tenantId, nit, cancellationToken).ConfigureAwait(false);
                var row = reps.FirstOrDefault(r => r.DocumentType == docType && r.DocumentNumber == doc);
                if (row is null)
                {
                    return null;
                }

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);
    }

    public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAsync(
        Guid tenantId,
        string companyNit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyNit);
        var nit = companyNit.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var reps = await ActiveRepsByNitAsync(tenantId, nit, cancellationToken).ConfigureAwait(false);
                var row = reps.FirstOrDefault();
                if (row is null)
                {
                    return null;
                }

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<LegalRepresentativeItem>> ListActiveByCompanyNitAsync(
        Guid tenantId,
        string companyNit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyNit);
        var nit = companyNit.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var reps = await ActiveRepsByNitAsync(tenantId, nit, cancellationToken).ConfigureAwait(false);
                return await ProjectAsync(reps, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<LegalRepresentativeItem?> FindActiveByDocumentAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        var docType = documentType.Trim();
        var doc = documentNumber.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var row = await _context.CompanyLegalRepresentatives
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId
                        && r.IsActive
                        && r.DocumentType == docType
                        && r.DocumentNumber == doc)
                    .OrderByDescending(r => r.CreatedAt)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);
    }

    /// <summary>
    /// Representantes ACTIVOS del tenant asociados a un NIT, por el puente multiempresa (HU #10932) o
    /// por la compañía primaria (compatibilidad). Ordenados por creación descendente.
    /// </summary>
    private async Task<List<CompanyLegalRepresentativeEntity>> ActiveRepsByNitAsync(
        Guid tenantId,
        string nit,
        CancellationToken cancellationToken)
    {
        var companyIds = await _context.RepresentedCompanies
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DocumentNumber == nit)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyIds.Count == 0)
        {
            return [];
        }

        var bridgeRepIds = await _context.LegalRepresentativeCompanies
            .AsNoTracking()
            .Where(l => companyIds.Contains(l.RepresentedCompanyId))
            .Select(l => l.RepresentativeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _context.CompanyLegalRepresentatives
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId
                && r.IsActive
                && (bridgeRepIds.Contains(r.Id)
                    || (r.RepresentedCompanyId != null && companyIds.Contains(r.RepresentedCompanyId.Value))))
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<RepresentedCompanyItem>> ListRepresentedCompaniesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var rows = await _context.RepresentedCompanies
                    .AsNoTracking()
                    .Where(c => c.TenantId == tenantId)
                    .OrderBy(c => c.Name)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<RepresentedCompanyItem> items = [.. rows.Select(ToCompanyItem)];
                return items;
            },
            cancellationToken);

    public Task<RepresentedCompanyItem?> FindRepresentedCompanyByNitAsync(
        Guid tenantId,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        var nit = documentNumber.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var row = await _context.RepresentedCompanies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        c => c.TenantId == tenantId && c.DocumentNumber == nit, cancellationToken)
                    .ConfigureAwait(false);

                return row is null ? null : ToCompanyItem(row);
            },
            cancellationToken);
    }

    /// <summary>
    /// Proyecta las filas de representante a su read model resolviendo, en consultas en lote (sin N+1),
    /// la compañía representada y los tipos de trámite del puente. Con <paramref name="includeDeeds"/>
    /// (detalle) también carga el HISTORIAL de escrituras de cada compañía del representante cruzando
    /// <c>company_deed_companies</c> → <c>company_deeds</c> (HU #10933), en un lote adicional.
    /// </summary>
    private async Task<IReadOnlyList<LegalRepresentativeItem>> ProjectAsync(
        List<CompanyLegalRepresentativeEntity> rows,
        CancellationToken cancellationToken,
        bool includeDeeds = false)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var repIds = rows.Select(r => r.Id).ToList();

        // Puente representante ↔ compañía (HU #10932): todas las compañías de cada representante.
        // HU #11177: se incluye CreatedAt para el orden estable secundario (principal primero, luego
        // por fecha de asociación ascendente). Sin CreatedAt el orden depende del hash del GUID.
        var bridgeRows = await _context.LegalRepresentativeCompanies
            .AsNoTracking()
            .Where(l => repIds.Contains(l.RepresentativeId))
            .Select(l => new { l.RepresentativeId, l.RepresentedCompanyId, l.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bridgeByRep = bridgeRows
            .GroupBy(b => b.RepresentativeId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => (CompanyId: x.RepresentedCompanyId, CreatedAt: x.CreatedAt)).ToList());

        var companyIds = rows.Select(r => r.RepresentedCompanyId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Concat(bridgeRows.Select(b => b.RepresentedCompanyId))
            .Distinct()
            .ToList();

        var companies = await _context.RepresentedCompanies
            .AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);

        var procedureTypeRows = await _context.CompanyLegalRepresentativeProcedureTypes
            .AsNoTracking()
            .Where(p => repIds.Contains(p.RepresentativeId))
            .Select(p => new { p.RepresentativeId, p.ProcedureTypeId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var procedureTypesByRep = procedureTypeRows
            .GroupBy(p => p.RepresentativeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(x => x.ProcedureTypeId)]);

        // HU #11059 — vigencia de identidad y de firma del baúl por representante, en lote. La consola
        // necesita distinguir "vigente" de "vencida" para poder ofrecer la renovación; el booleano
        // HasSignatureOrIdentity no daba para eso. Dos consultas para TODAS las filas (sin N+1).
        var vigenciaIdentidad = await LoadIdentityVigenciaAsync(rows, cancellationToken)
            .ConfigureAwait(false);
        var vigenciaFirma = await LoadFirmaBaulVigenciaAsync(rows, cancellationToken).ConfigureAwait(false);

        // Historial de escrituras por compañía (solo en el detalle, que proyecta un ÚNICO representante:
        // rows[0]). Feature #10929: la escritura es DEL representante, así que se filtran a las que ÉL
        // asoció (RepresentativeId == rows[0].Id); las legadas (RepresentativeId null) NO aparecen en el
        // detalle de ningún representante. Una escritura puede cubrir varias compañías (M:N) → aparece en
        // cada una. Se ordena por VigenciaHasta desc y se anota el estado contra "hoy" en Colombia.
        IReadOnlyDictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>> deedsByCompany = includeDeeds
            ? await LoadDeedsByCompanyAsync(rows[0].TenantId, rows[0].Id, companyIds, cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>();

        return [.. rows.Select(r =>
        {
            RepresentedCompanyEntity? company = null;
            if (r.RepresentedCompanyId is { } primaryCompanyId)
            {
                companies.TryGetValue(primaryCompanyId, out company);
            }
            procedureTypesByRep.TryGetValue(r.Id, out var procedureTypeIds);

            // HU #11177 — compañías del representante con bandera explícita de principal y orden
            // estable: principal primero, luego el resto por fecha de asociación ascendente.
            // Persona sin NITs: linkedEntries vacío.
            var linkedEntries = bridgeByRep.TryGetValue(r.Id, out var linked) && linked.Count > 0
                ? linked
                : r.RepresentedCompanyId is { } pid
                    ? [(CompanyId: pid, CreatedAt: DateTimeOffset.MinValue)]
                    : [];

            IReadOnlyList<LegalRepresentativeCompanySummary> companySummaries =
            [
                .. linkedEntries
                    .OrderBy(x => x.CompanyId == r.RepresentedCompanyId ? 0 : 1)
                    .ThenBy(x => x.CreatedAt)
                    .Where(x => companies.ContainsKey(x.CompanyId))
                    .Select(x =>
                    {
                        var c = companies[x.CompanyId];
                        // HU #11058 — el contacto viaja en el resumen para que la edición lo precargue.
                        // Sin él el formulario reenviaba estos campos en blanco y el upsert los borraba.
                        var summary = new LegalRepresentativeCompanySummary(c.Id, c.DocumentNumber, c.Name)
                        {
                            IsPrimary = x.CompanyId == r.RepresentedCompanyId,
                            Email = c.Email,
                            Address = c.Address,
                            City = c.City,
                            Phone = c.Phone,
                        };
                        return deedsByCompany.TryGetValue(x.CompanyId, out var deeds)
                            ? summary with { Deeds = deeds }
                            : summary;
                    }),
            ];

            var identidad = vigenciaIdentidad.GetValueOrDefault(
                PersonaKey(r), new AdminIdentityVigencia.Resultado(AdminIdentityVigencia.None, null));
            var firma = vigenciaFirma.GetValueOrDefault(PersonaKey(r));

            return new LegalRepresentativeItem
            {
                Id = r.Id,
                TenantId = r.TenantId,
                IdentityStatus = identidad.Status,
                IdentityValidUntil = identidad.ValidUntil,
                FirmaBaulVigente = firma is not null,
                FirmaBaulVigenteHasta = firma,
                RepresentedCompanyId = r.RepresentedCompanyId,
                CompanyDocumentNumber = company?.DocumentNumber ?? string.Empty,
                CompanyName = company?.Name ?? string.Empty,
                DocumentType = r.DocumentType,
                DocumentNumber = r.DocumentNumber,
                FirstLastName = r.FirstLastName,
                SecondLastName = r.SecondLastName,
                Name = r.Name,
                Email = r.Email,
                Address = r.Address,
                City = r.City,
                Phone = r.Phone,
                SignatureVaultId = r.SignatureVaultId,
                IdentityValidationRef = r.IdentityValidationRef,
                ProcedureTypeIds = procedureTypeIds ?? [],
                Companies = companySummaries,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            };
        })];
    }

    /// <summary>
    /// HU #11059 — vigencia de la identidad por representante, con el mismo cálculo que el mandatario
    /// del OT (<see cref="AdminIdentityVigencia"/>). Una consulta para todos los ids.
    /// </summary>
    /// <summary>
    /// HU #11192 — vigencia de identidad de la PERSONA, no del sujeto. Antes se resolvía filtrando por
    /// <c>subject_type = 'legal_representative'</c> y por el id del representante, mientras la firma del
    /// baúl se resolvía por documento. Esa asimetría hacía que una validación aprobada y vigente de la
    /// misma persona creada bajo OTRO sujeto —un mandatario, un actor de trámite, u otra fila del mismo
    /// representante en otra compañía— fuera invisible, y el panel dijera «Identidad sin validar»
    /// teniéndola.
    /// <para>
    /// Se llavea por <c>(tenant, tipo de documento, documento)</c>, igual que
    /// <see cref="LoadFirmaBaulVigenciaAsync"/>. El filtro por tenant es obligatorio: la misma persona
    /// puede ser representante en varios tenants y la validación es tenant-scoped (DDL 40-HU10907).
    /// </para>
    /// <para>
    /// Esto NO vincula la validación al representante: <c>identity_validation_ref</c> solo lo escribe el
    /// resolutor al guardar o el endpoint de asociación (HU #11176). Un representante puede quedar con
    /// identidad vigente de la persona y sin vínculo propio, y el panel debe poder distinguirlo.
    /// </para>
    /// </summary>
    private async Task<Dictionary<(Guid, string, string), AdminIdentityVigencia.Resultado>> LoadIdentityVigenciaAsync(
        List<CompanyLegalRepresentativeEntity> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
        var documentos = rows.Select(r => r.DocumentNumber).Distinct().ToList();
        var now = DateTimeOffset.UtcNow;

        // El filtro por tenant + documento se hace en SQL; el cotejo del tipo de documento se cierra en
        // memoria al agrupar, porque una tupla compuesta no se traduce a un IN de PostgreSQL (mismo
        // criterio que la carga de firmas del baúl).
        var validaciones = await _context.AdminIdentityValidations
            .AsNoTracking()
            .Where(v => tenantIds.Contains(v.TenantId) && documentos.Contains(v.DocumentNumber))
            .Select(v => new { v.TenantId, v.DocumentType, v.DocumentNumber, v.Status, v.ValidUntil })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return validaciones
            .GroupBy(v => (v.TenantId, v.DocumentType, v.DocumentNumber))
            .ToDictionary(
                g => g.Key,
                g => AdminIdentityVigencia.Resumir(
                    g.Select(v => new AdminIdentityVigencia.Entrada(v.Status, v.ValidUntil)), now));
    }

    /// <summary>
    /// Llave de PERSONA dentro del tenant: <c>(tenant, tipo de documento, documento)</c>. La usan tanto
    /// la firma del baúl (HU #10932, igual que <c>FindActiveByDocumentAsync</c>, que es lo que consume
    /// el guardado) como la vigencia de identidad (HU #11192). Ninguna de las dos pertenece a la
    /// compañía ni a la fila del representante, sino a la persona.
    /// </summary>
    private static (Guid TenantId, string DocumentType, string DocumentNumber) PersonaKey(
        CompanyLegalRepresentativeEntity r) => (r.TenantId, r.DocumentType, r.DocumentNumber);

    /// <summary>
    /// HU #11059 — hasta cuándo es vigente la firma del baúl de cada representante, o ausencia si no
    /// tiene ninguna vigente hoy. Mismo criterio que <c>SignatureVault.EstaVigente</c>: activa y
    /// <c>hoy</c> dentro de [VigenciaDesde, VigenciaHasta] por día calendario de Colombia (UTC-5).
    /// Una sola consulta para todos los representantes proyectados.
    /// </summary>
    private async Task<Dictionary<(Guid, string, string), DateOnly?>> LoadFirmaBaulVigenciaAsync(
        List<CompanyLegalRepresentativeEntity> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
        var documentos = rows.Select(r => r.DocumentNumber).Distinct().ToList();
        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaUtcOffset).DateTime);

        // El filtro por tenant + documento se hace en SQL; el cotejo del tipo de documento se cierra en
        // memoria, porque una tupla compuesta no se traduce a un IN de PostgreSQL.
        var firmas = await _context.SignatureVault
            .AsNoTracking()
            .Where(v => tenantIds.Contains(v.TenantId)
                && documentos.Contains(v.DocumentNumber)
                && v.Estado == SignatureVaultEstadoActiva
                && v.VigenciaDesde <= hoy
                && v.VigenciaHasta >= hoy)
            .Select(v => new { v.TenantId, v.DocumentType, v.DocumentNumber, v.VigenciaHasta })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<(Guid, string, string), DateOnly?>();
        foreach (var f in firmas)
        {
            var key = (f.TenantId, f.DocumentType, f.DocumentNumber);
            // Con varias firmas vigentes manda la que caduca más tarde.
            if (!result.TryGetValue(key, out var actual) || actual is null || f.VigenciaHasta > actual)
            {
                result[key] = f.VigenciaHasta;
            }
        }

        return result;
    }

    /// <summary>Estado "activa" del baúl (ADR-0025): las revocadas no cuentan como vigentes.</summary>
    private const string SignatureVaultEstadoActiva = "activa";


    /// <summary>
    /// Carga el historial de escrituras (activas y vencidas) de un conjunto de compañías del tenant,
    /// filtradas a las que asoció el representante <paramref name="representativeId"/> (Feature #10929), y
    /// lo agrupa por compañía. Cruza <c>company_deed_companies</c> → <c>company_deeds</c> en dos consultas
    /// en lote. Las escrituras legadas (<c>representative_id</c> null) NO se incluyen: pertenecen a la
    /// empresa, no a un representante. Cada escritura se anota con su estado contra "hoy" en Colombia
    /// (UTC-5) y las listas quedan ordenadas por <c>VigenciaHasta</c> descendente. Una escritura del
    /// representante compartida por varias compañías aparece en la lista de cada una.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>> LoadDeedsByCompanyAsync(
        Guid tenantId,
        Guid representativeId,
        List<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        if (companyIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>();
        }

        var bridgeRows = await _context.CompanyDeedCompanies
            .AsNoTracking()
            .Where(dc => companyIds.Contains(dc.RepresentedCompanyId))
            .Select(dc => new { dc.DeedId, dc.RepresentedCompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (bridgeRows.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>();
        }

        var deedIds = bridgeRows.Select(b => b.DeedId).Distinct().ToList();
        var deeds = await _context.CompanyDeeds
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId
                && deedIds.Contains(d.Id)
                && d.RepresentativeId == representativeId)
            .ToDictionaryAsync(d => d.Id, cancellationToken)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);

        return bridgeRows
            .Where(b => deeds.ContainsKey(b.DeedId))
            .GroupBy(b => b.RepresentedCompanyId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RepresentativeDeedSummary>)
                [
                    .. g.Select(b => deeds[b.DeedId])
                        .OrderByDescending(d => d.VigenciaHasta)
                        .ThenByDescending(d => d.CreatedAt)
                        .Select(d => RepresentativeDeedSummary.Create(
                            d.Id, d.Description, d.VigenciaDesde, d.VigenciaHasta, d.IsActive, today)),
                ]);
    }

    private static RepresentedCompanyItem ToCompanyItem(RepresentedCompanyEntity c) =>
        new()
        {
            Id = c.Id,
            TenantId = c.TenantId,
            DocumentType = c.DocumentType,
            DocumentNumber = c.DocumentNumber,
            Name = c.Name,
            Email = c.Email,
            Address = c.Address,
            City = c.City,
            Phone = c.Phone,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        };
}
