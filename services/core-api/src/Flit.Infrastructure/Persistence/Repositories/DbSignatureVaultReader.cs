using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Tramites.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas EF Core del baúl de firmas (HU #10642, ADR-0025). El baúl es tenant-scoped: las
/// lecturas corren bajo el contexto RLS del tenant fijando <c>app.current_tenant_id</c> con
/// <c>set_config(..., is_local := true)</c> (a diferencia del reader cross-tenant de mandatarios,
/// que corre con <c>row_security = off</c>). <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
///
/// <para><b>Bug #11659 — dos empates distintos, a propósito.</b> Acreditar a una persona como firmante
/// y resolver qué fila choca contra el índice único NO son la misma pregunta:</para>
/// <list type="bullet">
///   <item><see cref="FindActiveByDocumentAsync"/> (acreditación) empata por (tipo, número) con la
///     normalización canónica de la identidad (<see cref="DocumentCanonicalNormalization"/>).</item>
///   <item><see cref="FindActiveByNumberAsync"/> (escritura) empata solo por número, exactamente como
///     <c>uq_signature_vault_activa</c>.</item>
/// </list>
///
/// <para><b>Sobre el índice único: se deja como está, en <c>(tenant_id, document_number)</c>.</b>
/// Ampliarlo a <c>(tenant_id, document_type, document_number)</c> permitiría DOS firmas activas para el
/// mismo número con tipos distintos, que es justo la forma de dato que produce la ambigüedad que este
/// bug cierra; y la invariante vigente («una sola firma activa por número en el tenant») es más
/// estricta, así que producción no puede crear filas divergentes nuevas: solo pueden existir
/// históricas. Por eso tampoco hay migración: el DDL de esta tabla vive en SQL crudo (la entidad está
/// <c>ExcludeFromMigrations</c>) y no hay nada que cambiar.</para>
///
/// <para><b>Medición obligatoria antes de desplegar</b> (cuantifica cuántas firmas dejarían de
/// acreditar por diferir el tipo; se espera 0):</para>
/// <code>
/// SELECT tenant_id, document_number, count(DISTINCT upper(btrim(document_type))) AS tipos
/// FROM admin.signature_vault
/// WHERE estado = 'activa'
/// GROUP BY 1, 2
/// HAVING count(DISTINCT upper(btrim(document_type))) > 1;
/// </code>
/// <para>Y el contraste con el actor del trámite (firmas cuyo tipo no coincide con el del sujeto de
/// identidad que las consume) se mide con la misma llave canónica <c>tenant|TIPO|NÚMERO</c>.</para>
/// </summary>
internal sealed class DbSignatureVaultReader : ISignatureVaultReader
{
    private readonly FlitDbContext _context;

    public DbSignatureVaultReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<SignatureVault?> FindActiveByNitAsync(
        Guid tenantId,
        string nitEmpresa,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nitEmpresa);
        var nit = nitEmpresa.Trim();

        return ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.SignatureVault
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId
                        && s.NitEmpresa == nit
                        && s.Estado == SignatureVaultEstadoMapping.Activa)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : SignatureVaultEstadoMapping.Rehydrate(entity);
            },
            cancellationToken);
    }

    public Task<SignatureVault?> FindActiveByDocumentAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        // Bug #11659 — MISMA normalización canónica que la identidad biométrica
        // (DocumentCanonicalNormalization: Trim + mayúsculas invariantes), aplicada a las dos partes
        // del documento y a los dos lados de la comparación.
        var (type, number) = DocumentCanonicalNormalization.Normalize(documentType, documentNumber);

        return ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.SignatureVault
                    .AsNoTracking()
                    // El TIPO participa en el filtro, no solo en el desempate. Antes ordenaba
                    // (OrderByDescending(s => s.DocumentType == type)) sobre un conjunto que el índice
                    // único (tenant, document_number) deja en UNA fila: el tipo era decorativo y el
                    // baúl acreditaba como firmante a quien compartía número con otro tipo de
                    // documento — un falso positivo de firma frente al gate de radicación.
                    .Where(s => s.TenantId == tenantId
                        && s.DocumentNumber.Trim().ToUpper() == number
                        && s.DocumentType.Trim().ToUpper() == type
                        && s.Estado == SignatureVaultEstadoMapping.Activa)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : SignatureVaultEstadoMapping.Rehydrate(entity);
            },
            cancellationToken);
    }

    /// <summary>
    /// Lectura ALINEADA CON EL ÍNDICE (Bug #11659): igualdad exacta de número tras <c>Trim</c>, sin
    /// mirar el tipo, que es justo lo que evalúa <c>uq_signature_vault_activa</c>. Solo la consume el
    /// camino de escritura (sustitución de firma, HU #11193). Ver
    /// <see cref="ISignatureVaultReader.FindActiveByNumberAsync"/>.
    /// </summary>
    public Task<SignatureVault?> FindActiveByNumberAsync(
        Guid tenantId,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        var number = documentNumber.Trim();

        return ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.SignatureVault
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId
                        && s.DocumentNumber == number
                        && s.Estado == SignatureVaultEstadoMapping.Activa)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : SignatureVaultEstadoMapping.Rehydrate(entity);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entities = await _context.SignatureVault
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId)
                    // 'activa' < 'revocada' alfabéticamente → activas primero; luego creación desc.
                    .OrderBy(s => s.Estado)
                    .ThenByDescending(s => s.CreatedAt)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<SignatureVaultItem> items =
                    [.. entities.Select(SignatureVaultEstadoMapping.ToItem)];
                return items;
            },
            cancellationToken);

    public Task<SignatureVaultItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.SignatureVault
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : SignatureVaultEstadoMapping.ToItem(entity);
            },
            cancellationToken);

    public Task<SignatureVaultItem?> GetByIdAnyTenantAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            return GetByIdAnyTenantCoreAsync(id, cancellationToken);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);

                var item = await GetByIdAnyTenantCoreAsync(id, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return item;
            }
        });
    }

    private async Task<SignatureVaultItem?> GetByIdAnyTenantCoreAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var entity = await _context.SignatureVault
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : SignatureVaultEstadoMapping.ToItem(entity);
    }

    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await read().ConfigureAwait(false);
        }

        // HU #11000 — si YA hay una transacción abierta (la regeneración documental del expediente corre
        // dentro del scope de tenant del cliente que abren la aprobación y la asignación de placa, HU
        // #10995/#10996), abrir otra lanzaba "The connection is already in a transaction" y el best-effort
        // de esos endpoints se tragaba la excepción: los documentos NO se regeneraban. Se reutiliza la
        // transacción en curso fijando el tenant de la lectura y RESTAURÁNDOLO después, para no dejar el
        // scope apuntando a otro tenant en las consultas que sigan dentro de la misma transacción.
        if (_context.Database.CurrentTransaction is not null)
        {
            var previous = await ReadCurrentTenantAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SetCurrentTenantAsync(tenantId.ToString(), cancellationToken).ConfigureAwait(false);
                return await read().ConfigureAwait(false);
            }
            finally
            {
                await SetCurrentTenantAsync(previous, cancellationToken).ConfigureAwait(false);
            }
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

    /// <summary>Tenant fijado en la transacción en curso (cadena vacía si no hay ninguno). HU #11000.</summary>
    private async Task<string> ReadCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var values = await _context.Database
            .SqlQueryRaw<string>(
                "SELECT COALESCE(current_setting('app.current_tenant_id', true), '') AS \"Value\"")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return values.Count > 0 ? values[0] : string.Empty;
    }

    /// <summary>Fija <c>app.current_tenant_id</c> LOCAL a la transacción en curso. HU #11000.</summary>
    private Task<int> SetCurrentTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantId}, true)",
            cancellationToken);
}
