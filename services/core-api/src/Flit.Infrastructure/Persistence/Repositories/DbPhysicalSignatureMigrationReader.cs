using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13131 (ADR-0061) — reporte de migración de la firma física. Mandatarios Persona natural activos
/// (sin baja lógica) con <c>signs_physically</c> verdadero en un organismo activo del puente y que NO
/// tienen (a) firma del baúl (la columna <c>signature_vault_id</c> o, como en el resolver del mandato, la
/// firma activa y vigente por documento en el tenant de alguna de sus compañías) ni (b) validación
/// biométrica aprobada y vigente (misma fuente y clasificador que el resto de la épica:
/// <see cref="IdentityVigenciaPorDocumentoResolver"/>, regla de 30 días sin tocar).
/// <para>Es solo lectura: las filas no se modifican. El documento se usa únicamente para consultar y NO
/// sale en el resultado ni se escribe en logs.</para>
/// </summary>
internal sealed class DbPhysicalSignatureMigrationReader : IPhysicalSignatureMigrationReader
{
    private readonly FlitDbContext _context;
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IdentityVigenciaPorDocumentoResolver _identityResolver;
    private readonly ISignatureVaultPolicy _vaultPolicy;

    public DbPhysicalSignatureMigrationReader(
        FlitDbContext context,
        ISignatureVaultPolicy vaultPolicy,
        ITransitOfficeOperationalStatusReader? otStatus = null,
        IdentityVigenciaPorDocumentoResolver? identityResolver = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _vaultPolicy = vaultPolicy ?? throw new ArgumentNullException(nameof(vaultPolicy));
        _otStatus = otStatus ?? new DbTransitOfficeOperationalStatusReader(context);
        _identityResolver = identityResolver
            ?? new IdentityVigenciaPorDocumentoResolver(new ProcedureInstanceRepository(context));
    }

    public async Task<IReadOnlyList<PhysicalSignatureMigrationRow>> ListAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var query =
            from o in _context.MandateSignerTransitOffices.AsNoTracking()
            join s in _context.MandateSigners.AsNoTracking() on o.MandateSignerId equals s.Id
            where o.IsActive && o.SignsPhysically
                && s.IsActive && s.DeletedAt == null
                && s.SignerModel == MandateSignerModels.Natural
            select new
            {
                o.TransitOfficeId,
                s.Id,
                s.FullName,
                s.DocumentType,
                s.DocumentNumber,
                s.SignatureMethod,
                s.SignatureVaultId,
            };
        if (transitOfficeId is { } officeFilter)
        {
            query = query.Where(x => x.TransitOfficeId == officeFilter);
        }

        var candidatos = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (candidatos.Count == 0)
        {
            return [];
        }

        var signerIds = candidatos.Select(c => c.Id).Distinct().ToList();
        var officeIds = candidatos.Select(c => c.TransitOfficeId).Distinct().ToList();

        var links = await _context.MandateSignerCompanies.AsNoTracking()
            .Where(c => signerIds.Contains(c.MandateSignerId) && officeIds.Contains(c.TransitOfficeId) && c.IsActive)
            .Select(c => new { c.MandateSignerId, c.TransitOfficeId, c.CompanyTenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var offices = await _context.TransitOffices.AsNoTracking()
            .Where(o => officeIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Code, o.Name })
            .ToDictionaryAsync(o => o.Id, cancellationToken)
            .ConfigureAwait(false);

        var tenantIds = links.Select(l => l.CompanyTenantId).Distinct().ToList();
        var tenantNames = await LoadTenantNamesAsync(tenantIds, cancellationToken).ConfigureAwait(false);

        // Identidad: mejor estado entre las compañías del mandatario (regla única de HU #13121).
        var identidades = await MandateSignerIdentityTenantResolver.ResolveAsync(
            _context,
            _otStatus,
            [.. candidatos
                .Where(c => !string.IsNullOrWhiteSpace(c.DocumentNumber))
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .Select(c => new MandateSignerIdentityTenantResolver.SignerRef(
                    c.Id, c.TransitOfficeId, c.DocumentType, c.DocumentNumber!))],
            (tenantId, documentos, ct) => _identityResolver.ResolveManyBatchedAsync(
                tenantId, documentos, DateTimeOffset.UtcNow, ct),
            cancellationToken).ConfigureAwait(false);

        var rows = new List<PhysicalSignatureMigrationRow>();
        foreach (var c in candidatos)
        {
            var identidad = identidades.GetValueOrDefault(c.Id, IdentityVigenciaResult.SinValidacion);
            if (identidad.Status == IdentityVigenciaEstados.AprobadaVigente)
            {
                continue; // migrado por biometría
            }

            var companias = links
                .Where(l => l.MandateSignerId == c.Id && l.TransitOfficeId == c.TransitOfficeId)
                .Select(l => l.CompanyTenantId)
                .Distinct()
                .ToList();

            if (await TieneFirmaEnElBaulAsync(
                        c.SignatureVaultId, c.DocumentType, c.DocumentNumber, companias, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue; // migrado por baúl
            }

            var faltante = FaltaPara(c.SignatureMethod, identidad.Status);
            var oficina = offices.GetValueOrDefault(c.TransitOfficeId);
            var codigo = oficina?.Code ?? string.Empty;
            var nombreOficina = oficina?.Name ?? string.Empty;

            if (companias.Count == 0)
            {
                rows.Add(new PhysicalSignatureMigrationRow(
                    c.Id, c.FullName, null, null, c.TransitOfficeId, codigo, nombreOficina,
                    PhysicalSignatureMigrationForms.FirmaFisica, c.SignatureMethod, faltante));
                continue;
            }

            foreach (var compania in companias)
            {
                rows.Add(new PhysicalSignatureMigrationRow(
                    c.Id, c.FullName, compania, tenantNames.GetValueOrDefault(compania), c.TransitOfficeId,
                    codigo, nombreOficina, PhysicalSignatureMigrationForms.FirmaFisica, c.SignatureMethod, faltante));
            }
        }

        return rows;
    }

    private static string FaltaPara(string? signatureMethod, string identityStatus)
    {
        // Con biometría aprobada pero vencida el dato que falta es la renovación, no una validación nueva.
        if (identityStatus == IdentityVigenciaEstados.Vencida)
        {
            return PhysicalSignatureMigrationMissing.ValidacionBiometricaVigente;
        }

        return signatureMethod switch
        {
            MandateSignatureMethods.Baul => PhysicalSignatureMigrationMissing.FirmaBaul,
            MandateSignatureMethods.Biometria => PhysicalSignatureMigrationMissing.ValidacionBiometrica,
            _ => PhysicalSignatureMigrationMissing.BaulOBiometria,
        };
    }

    private async Task<bool> TieneFirmaEnElBaulAsync(
        Guid? signatureVaultId,
        string documentType,
        string? documentNumber,
        IReadOnlyList<Guid> companias,
        CancellationToken cancellationToken)
    {
        if (signatureVaultId is not null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            return false;
        }

        var tipo = string.IsNullOrWhiteSpace(documentType) ? "CC" : documentType.Trim();
        foreach (var tenantId in companias)
        {
            var match = await _vaultPolicy
                .ResolveMandatarioAsync(tenantId, tipo, documentNumber.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (match is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>identity.tenants</c> tiene RLS por tenant: se lee cross-tenant (solo el Super Admin llega aquí).</summary>
    private async Task<Dictionary<Guid, string>> LoadTenantNamesAsync(
        List<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        if (tenantIds.Count == 0)
        {
            return [];
        }

        Task<Dictionary<Guid, string>> Read() => _context.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.LegalName })
            .ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken);

        if (!_context.Database.IsRelational())
        {
            return await Read().ConfigureAwait(false);
        }

        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                .ConfigureAwait(false);
            return await Read().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                    .ConfigureAwait(false);
                var result = await Read().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
