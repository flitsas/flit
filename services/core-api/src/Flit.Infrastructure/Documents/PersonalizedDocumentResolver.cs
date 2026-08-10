using System.Security.Cryptography;
using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Tramites.Application.Documents;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Implementación de <see cref="IPersonalizedDocumentResolver"/> (HU #11316, Feature #11309,
/// ADR-0042). Cruza la custodia de versiones de Admin (<see cref="ICompanyPersonalizedDocumentRepository"/>,
/// HU #11313) con el storage de la compañía (<see cref="ICompanyPersonalizedDocumentStorage"/>) para
/// bajar los bytes de la versión activa, sin acoplar el módulo de Trámites al de Admin. Vive en
/// Infrastructure por el mismo motivo que <c>ProcedureDeedResolver</c>.
///
/// <para><b>Lista de tipos habilitados.</b> El mecanismo de sustitución quedó completo en la HU #11316;
/// <c>mandato</c> se habilitó en la HU #11317 y <c>tramite_virtual</c> se habilita en ESTA HU (#11318),
/// que cierra el Feature #11309: es la sustitución de mayor radio de impacto porque la solicitud de
/// trámite virtual se genera SIEMPRE (persona natural y jurídica, matrícula y traspaso), a diferencia
/// del mandato que es condicional. Ningún otro tipo documental es personalizable — <see
/// cref="Flit.Admin.Application.Companies.PersonalizedDocuments.Create.CreatePersonalizedDocumentVersionHandler"/>
/// solo acepta <c>mandato</c> | <c>tramite_virtual</c> al dar de alta una versión, así que <see
/// cref="EnabledTypes"/> es, a partir de esta HU, el vocabulario COMPLETO y definitivo del Feature. Para
/// cualquier otro tipo (fur, compraventa, certificados, escrituras, licencia de tránsito, consolidados),
/// <see cref="ResolveAsync"/> sigue devolviendo vacío SIEMPRE, sin tocar repositorio ni storage — es la
/// garantía de "invisibilidad" del oráculo CF-01 para lo que el Feature nunca sustituye.</para>
/// </summary>
internal sealed partial class PersonalizedDocumentResolver(
    ICompanyPersonalizedDocumentRepository repository,
    ICompanyPersonalizedDocumentStorage storage,
    IPdfDocumentInspector inspector,
    ILogger<PersonalizedDocumentResolver> logger)
    : IPersonalizedDocumentResolver
{
    /// <summary>
    /// Vocabulario COMPLETO de tipos que el pipeline puede sustituir en producción (Feature #11309,
    /// ADR-0042). <c>mandato</c> se habilitó en la HU #11317; <c>tramite_virtual</c> se habilita en
    /// ESTA HU (#11318). No hay un tercer tipo pendiente: cualquier otro valor devuelto por
    /// <c>generated.Select(d =&gt; d.Tipo)</c> en <c>FurCommand</c> sigue la rama <c>continue</c> de
    /// <see cref="ResolveAsync"/> sin excepción.
    /// </summary>
    private static readonly HashSet<string> EnabledTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "mandato",
        "tramite_virtual",
    };

    public async Task<PersonalizedDocumentResolution> ResolveAsync(
        Guid tenantId,
        IEnumerable<string> tipos,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tipos);

        List<ResolvedPersonalizedDocument>? resolved = null;
        List<PersonalizedDocumentUnavailable>? unavailable = null;

        foreach (var tipo in tipos.Where(t => !string.IsNullOrWhiteSpace(t))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!EnabledTypes.Contains(tipo))
                continue;

            var active = await repository.GetActiveAsync(tenantId, tipo, ct).ConfigureAwait(false);
            if (active is null)
                continue; // Sin versión activa: el pipeline conserva el documento del sistema.

            try
            {
                var stream = await storage.OpenReadAsync(active.StoragePath, ct).ConfigureAwait(false);
                if (stream is null)
                {
                    (unavailable ??= []).Add(new PersonalizedDocumentUnavailable(tipo, active.Id, "storage_no_disponible"));
                    LogNoDisponible(logger, tenantId, tipo, "storage_no_disponible");
                    continue;
                }

                byte[] content;
                await using (stream.ConfigureAwait(false))
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                    content = ms.ToArray();
                }

                if (content.Length == 0)
                {
                    (unavailable ??= []).Add(new PersonalizedDocumentUnavailable(tipo, active.Id, "archivo_vacio"));
                    LogNoDisponible(logger, tenantId, tipo, "archivo_vacio");
                    continue;
                }

                // DT-6 — la versión ya se validó al confirmarla (HU #11313), pero el pipeline vuelve a
                // recalcular el SHA-256 del objeto que hoy vive en storage y lo compara contra el hash
                // custodiado en la fila (active.StorageSha256) ANTES de inspeccionarlo: es la única
                // forma de garantizar que lo que se adjunta es EL PDF validado, no cualquier PDF que
                // haya quedado en esa ruta (mutación post-confirmación, ventana de política presignada,
                // compromiso del gestor de archivos), sin confiar en un estado de hace semanas.
                var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(content));
                if (!string.Equals(actualSha256, active.StorageSha256, StringComparison.OrdinalIgnoreCase))
                {
                    (unavailable ??= []).Add(new PersonalizedDocumentUnavailable(tipo, active.Id, "hash_mismatch"));
                    LogNoDisponible(logger, tenantId, tipo, "hash_mismatch");
                    continue;
                }

                var inspection = inspector.Inspect(content);
                if (!inspection.IsParseable)
                {
                    (unavailable ??= []).Add(new PersonalizedDocumentUnavailable(tipo, active.Id, "pdf_ilegible"));
                    LogNoDisponible(logger, tenantId, tipo, "pdf_ilegible");
                    continue;
                }

                if (inspection.PageCount > PdfIntegrityValidator.MaxPages)
                {
                    (unavailable ??= []).Add(new PersonalizedDocumentUnavailable(tipo, active.Id, "excede_paginas"));
                    LogNoDisponible(logger, tenantId, tipo, "excede_paginas");
                    continue;
                }

                (resolved ??= []).Add(new ResolvedPersonalizedDocument(
                    tipo,
                    $"{tipo}.pdf",
                    content,
                    active.Id,
                    active.Version,
                    active.StorageSha256,
                    inspection.PageCount));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort (DT-6): cualquier fallo de lectura cae al documento del sistema, nunca
                // rompe el expediente. Sin URL ni contenido en el log — solo tenant + tipo + id de versión.
                LogFallo(logger, ex, tenantId, tipo, active.Id);
                (unavailable ??= []).Add(new PersonalizedDocumentUnavailable(tipo, active.Id, "error_lectura"));
            }
        }

        return resolved is null && unavailable is null
            ? PersonalizedDocumentResolution.Empty
            : new PersonalizedDocumentResolution(resolved ?? [], unavailable ?? []);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Documento personalizado no disponible (tenant {TenantId}, tipo {Tipo}): {Motivo}. Se emite el documento del sistema.")]
    private static partial void LogNoDisponible(ILogger logger, Guid tenantId, string tipo, string motivo);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo leer el documento personalizado (tenant {TenantId}, tipo {Tipo}, versión {VersionId}); se emite el documento del sistema.")]
    private static partial void LogFallo(ILogger logger, Exception ex, Guid tenantId, string tipo, Guid versionId);
}
