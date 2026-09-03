using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Implementación de <see cref="IProcedureDeedResolver"/> (HU #10926, ADR-0033). Por cada actor
/// persona jurídica (NIT) del trámite, cruza su compañía representada del directorio del tenant
/// (<see cref="ILegalRepresentativeReader.FindRepresentedCompanyByNitAsync"/>) con su escritura activa
/// y vigente MÁS PRÓXIMA A VENCER (menor VigenciaHasta, HU #10936; <see cref="IDeedReader.ListActiveVigentesAsync"/>) y baja los bytes del
/// PDF vía <see cref="IAttachmentStorage.OpenReadAsync"/>. Vive en Infrastructure porque cruza el
/// módulo Admin (directorio de escrituras) con el almacenamiento de adjuntos de Trámites, sin acoplar
/// los módulos entre sí. Tipo por rol (D2): vendedor/propietario ⇒ 'escritura'; comprador ⇒
/// 'escritura_comprador'. Un solo documento por tipo. <c>DocumentNumber</c> (NIT) es PII: no loguear.
/// </summary>
internal sealed class ProcedureDeedResolver : IProcedureDeedResolver
{
    // Hora de Colombia (UTC-5, sin DST): la vigencia se cuenta por día calendario local (ADR-0025 §3).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly IDeedReader _deedReader;
    private readonly ILegalRepresentativeReader _representativeReader;
    private readonly IAttachmentStorage _storage;
    private readonly TimeProvider _timeProvider;

    public ProcedureDeedResolver(
        IDeedReader deedReader,
        ILegalRepresentativeReader representativeReader,
        IAttachmentStorage storage,
        TimeProvider timeProvider)
    {
        _deedReader = deedReader ?? throw new ArgumentNullException(nameof(deedReader));
        _representativeReader = representativeReader ?? throw new ArgumentNullException(nameof(representativeReader));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IReadOnlyList<ResolvedDeedDocument>> ResolveForActorsAsync(
        Guid tenantId,
        IEnumerable<ProcedureInstanceActor> actors,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actors);

        var nitActors = actors
            .Where(a => string.Equals(a.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(a.DocumentNumber))
            .ToList();
        if (nitActors.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);
        var deeds = await _deedReader.ListActiveVigentesAsync(tenantId, today, ct).ConfigureAwait(false);
        if (deeds.Count == 0)
        {
            return [];
        }

        var result = new List<ResolvedDeedDocument>(nitActors.Count);
        var seenTipos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in nitActors)
        {
            var tipo = string.Equals(actor.ActorType, "comprador", StringComparison.OrdinalIgnoreCase)
                ? "escritura_comprador"
                : "escritura";
            if (!seenTipos.Add(tipo))
            {
                continue; // un solo documento por tipo (rol)
            }

            var subject = IdentitySubjectResolver.For(actor);
            var subjectHasDocument = !string.IsNullOrWhiteSpace(subject.TipoDocumento)
                && !string.IsNullOrWhiteSpace(subject.NumeroDocumento);
            Guid? representativeId = null;
            if (subjectHasDocument)
            {
                var representative = await _representativeReader
                    .FindActiveByDocumentAsync(
                        tenantId, subject.TipoDocumento!.Trim(), subject.NumeroDocumento!.Trim(), ct)
                    .ConfigureAwait(false);
                representativeId = representative?.Id;
            }

            RepresentedCompanyItem? company;
            if (representativeId is Guid rid)
            {
                company = await _representativeReader
                    .FindActiveCompanyForRepresentativeAsync(tenantId, rid, actor.DocumentNumber.Trim(), ct)
                    .ConfigureAwait(false);
            }
            else if (subjectHasDocument)
            {
                // El RL SÍ tiene documento capturado pero no es un representante activo de la
                // compañía (el gestor lo cambió por otro que no está en el directorio). Caer al NIT
                // aquí adjuntaba la escritura de OTRO representante de la misma empresa —cualquier
                // escritura de la compañía autoriza a la persona que ahí figura, no a la que se
                // acaba de capturar—, y el consolidado terminaba con dos escrituras: la de sistema
                // (de quien ya no es el RL) y la que el gestor sube a mano para el RL vigente
                // (`EscrituraRepresentanteUpload`). Sin representante resuelto y con documento
                // capturado, no hay escritura de sistema que le corresponda: se omite.
                continue;
            }
            else
            {
                // Sin RL capturado todavía no hay con qué decidir a quién pertenece la escritura,
                // así que se cae al NIT (comportamiento previo — el trámite apenas está abriendo el
                // paso de actores).
                company = await _representativeReader
                    .FindRepresentedCompanyByNitAsync(tenantId, actor.DocumentNumber.Trim(), ct)
                    .ConfigureAwait(false);
            }
            if (company is null || !company.IsActive)
            {
                continue;
            }

            var deed = deeds
                .Where(d => d.RepresentedCompanyIds.Contains(company.Id)
                    && (representativeId is null || d.RepresentativeId == representativeId))
                .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
                .ThenByDescending(d => d.Id)
                .FirstOrDefault();
            if (deed is null)
            {
                continue;
            }

            var stream = await _storage.OpenReadAsync(deed.StoragePath, ct).ConfigureAwait(false);
            if (stream is null)
            {
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
                continue;
            }

            result.Add(new ResolvedDeedDocument(
                tipo,
                $"{tipo}.pdf",
                content,
                company.DocumentNumber,
                actor.ActorType ?? string.Empty,
                deed.Id));
        }

        return result;
    }
}
