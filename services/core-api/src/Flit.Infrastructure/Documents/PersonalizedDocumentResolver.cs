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
/// <para><b>Lista de tipos habilitados: VACÍA a propósito.</b> El mecanismo de sustitución queda
/// completo en esta HU, pero NINGÚN tipo se sustituye todavía en producción: <c>mandato</c> se habilita
/// en la HU #11317 y <c>tramite_virtual</c> en la HU #11318 (ampliando <see cref="EnabledTypes"/>, sin
/// tocar <c>FurCommand.cs</c> de nuevo). Antes de esas HUs, <see cref="ResolveAsync"/> siempre devuelve
/// vacío, aunque exista una versión activa — es la garantía de "invisibilidad" del oráculo CF-01.</para>
/// </summary>
internal sealed partial class PersonalizedDocumentResolver(
    ICompanyPersonalizedDocumentRepository repository,
    ICompanyPersonalizedDocumentStorage storage,
    IPdfDocumentInspector inspector,
    ILogger<PersonalizedDocumentResolver> logger)
    : IPersonalizedDocumentResolver
{
    /// <summary>
    /// Vocabulario de tipos que el pipeline puede sustituir HOY en producción. VACÍO en esta HU: ver
    /// nota de clase. Ampliar esta colección (no <c>FurCommand.cs</c>) es todo lo que necesitan las
    /// HUs #11317/#11318 para habilitar cada tipo.
    /// </summary>
    private static readonly HashSet<string> EnabledTypes = new(StringComparer.OrdinalIgnoreCase);

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
                // abrirla aquí: es la única forma de garantizar que el objeto que hoy vive en storage
                // sigue siendo un PDF legible en el momento exacto de generar, sin confiar en un estado
                // de hace semanas.
                var inspection = inspector.Inspect(content);
                if (!inspection.IsParseable)
                {
                    (unavailable ??= []).Add(new PersonalizedDocumentUnavailable(tipo, active.Id, "pdf_ilegible"));
                    LogNoDisponible(logger, tenantId, tipo, "pdf_ilegible");
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
