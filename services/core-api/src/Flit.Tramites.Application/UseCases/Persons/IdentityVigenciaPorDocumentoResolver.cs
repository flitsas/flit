using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Persons;

/// <summary>
/// Resultado de resolver la vigencia de identidad de UNA persona por documento (HU #11751, ADR-0050):
/// el estado clasificado (<see cref="IdentityVigenciaEstados"/>) más los datos que un consumidor (el
/// endpoint admin o <c>MandateSignerDirectory</c>) necesita para actuar — cuándo se aprobó, hasta
/// cuándo vale, y el certificado del sello (HU #11030) si lo hay.
/// </summary>
public sealed record IdentityVigenciaResult(
    string Status,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? ValidUntil,
    string? CertificateHash)
{
    /// <summary>Sin ninguna validación para ese documento en el tenant.</summary>
    public static readonly IdentityVigenciaResult SinValidacion =
        new(IdentityVigenciaEstados.SinValidacion, null, null, null);
}

/// <summary>
/// HU #11751 (ADR-0050) — punto ÚNICO de lectura de la vigencia de identidad de una persona por
/// documento, contra el almacén de Identidad (<c>tramites.procedure_instance_biometric_validations</c>).
/// Es la fuente que reemplaza a <c>admin.admin_identity_validations</c> en toda la cascada de
/// resolución de identidad administrativa (HU #11752): el endpoint admin de consulta y
/// <c>MandateSignerDirectory</c> consumen ESTA clase en vez de tener cada uno su propia consulta, para
/// no duplicar ni la normalización del documento (ADR-0039) ni la regla de vigencia
/// (<see cref="BiometricRules.EsAprobadaVigente"/>).
/// </summary>
public sealed class IdentityVigenciaPorDocumentoResolver(IProcedureInstanceRepository repo)
{
    /// <summary>
    /// Resuelve la vigencia de UN documento. Normaliza con <see cref="DocumentCanonicalNormalization"/>
    /// (Trim + mayúsculas invariantes, ADR-0039) antes de consultar. Documento vacío/nulo →
    /// <see cref="IdentityVigenciaResult.SinValidacion"/> sin tocar el repositorio.
    /// </summary>
    public async Task<IdentityVigenciaResult> ResolveAsync(
        Guid tenantId,
        string? documentType,
        string? documentNumber,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var (tipo, numero) = DocumentCanonicalNormalization.Normalize(documentType, documentNumber);
        if (tipo.Length == 0 || numero.Length == 0)
            return IdentityVigenciaResult.SinValidacion;

        // La más reciente (skip=0, take=1) es la única que importa para clasificar: mismo criterio de
        // orden que ListPersonBiometricValidationsHandler (CreatedAt desc, Id desc).
        var (rows, _, _) = await repo
            .ListBiometricValidationsByPersonAsync(tenantId, tipo, numero, 0, 1, ct)
            .ConfigureAwait(false);

        return Classify(rows.Count > 0 ? rows[0] : null, now);
    }

    /// <summary>
    /// Resolución en LOTE para varios documentos del mismo tenant (HU #11752 —
    /// <c>MandateSignerDirectory</c> resuelve la vigencia de cada mandatario candidato). Clave del mapa:
    /// <see cref="DocumentCanonicalNormalization.IdentidadKey"/>.
    ///
    /// <para><b>Nota de diseño — sin consulta SQL en lote.</b> Esta implementación hace UNA lectura por
    /// documento distinto (reutilizando <see cref="ResolveAsync"/>), no una única consulta <c>WHERE ...
    /// IN (...)</c> como hacía la versión anterior de <c>MandateSignerDirectory</c> contra
    /// <c>admin.admin_identity_validations</c>. Se acepta el N+1 porque el volumen esperado —mandatarios
    /// de una compañía en un organismo de tránsito— es de unas pocas filas (no un listado paginado), y
    /// añadir aquí una consulta por lote habría exigido una PROYECCIÓN nueva de "fila más reciente por
    /// documento" en <see cref="IProcedureInstanceRepository"/> que ningún otro consumidor necesita
    /// todavía. Si el volumen crece, migrar a una consulta por lote es un cambio aislado a este método.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IdentityVigenciaResult>> ResolveManyAsync(
        Guid tenantId,
        IReadOnlyCollection<(string DocumentType, string DocumentNumber)> documents,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, IdentityVigenciaResult>(StringComparer.Ordinal);
        foreach (var (documentType, documentNumber) in documents)
        {
            var key = DocumentCanonicalNormalization.IdentidadKey(tenantId, documentType, documentNumber);
            if (result.ContainsKey(key))
                continue;

            result[key] = await ResolveAsync(tenantId, documentType, documentNumber, now, ct)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Resolución en LOTE con UNA sola consulta SQL (HU #11765, ADR-0050) — para los lectores admin de
    /// representantes legales y mandatarios, cuyos listados pueden ser largos y no admiten el N+1 de
    /// <see cref="ResolveManyAsync"/> (aceptable ahí porque el volumen es de unas pocas filas). Normaliza
    /// y dedupe cada documento antes de ir al repositorio; documentos vacíos quedan fuera de la consulta
    /// y no aparecen en el resultado (el llamador debe tratar la ausencia como
    /// <see cref="IdentityVigenciaResult.SinValidacion"/>, igual que <see cref="ResolveManyAsync"/>).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IdentityVigenciaResult>> ResolveManyBatchedAsync(
        Guid tenantId,
        IReadOnlyCollection<(string DocumentType, string DocumentNumber)> documents,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalizados = documents
            .Select(d => DocumentCanonicalNormalization.Normalize(d.DocumentType, d.DocumentNumber))
            .Where(p => p.DocumentType.Length > 0 && p.DocumentNumber.Length > 0)
            .Distinct()
            .ToList();

        var result = new Dictionary<string, IdentityVigenciaResult>(StringComparer.Ordinal);
        if (normalizados.Count == 0)
            return result;

        var rows = await repo
            .ListLatestBiometricValidationsByPersonsAsync(
                tenantId,
                [.. normalizados.Select(p => (DocumentTypeNorm: p.DocumentType, DocumentNumberNorm: p.DocumentNumber))],
                ct)
            .ConfigureAwait(false);

        var latestByKey = rows.ToDictionary(
            r => DocumentCanonicalNormalization.IdentidadKey(tenantId, r.DocumentType, r.DocumentNumber),
            r => r);

        foreach (var (tipo, numero) in normalizados)
        {
            var key = DocumentCanonicalNormalization.IdentidadKey(tenantId, tipo, numero);
            result[key] = Classify(latestByKey.GetValueOrDefault(key), now);
        }

        return result;
    }

    /// <summary>Clasifica la fila más reciente (o su ausencia) con <see cref="IdentityVigenciaClassifier"/>.</summary>
    private static IdentityVigenciaResult Classify(ProcedureInstanceBiometricValidation? latest, DateTimeOffset now)
    {
        var status = IdentityVigenciaClassifier.Classify(latest, now);
        return latest is null
            ? IdentityVigenciaResult.SinValidacion
            : new IdentityVigenciaResult(status, latest.ValidatedAt, latest.ValidUntil, latest.CertificateHash);
    }
}
