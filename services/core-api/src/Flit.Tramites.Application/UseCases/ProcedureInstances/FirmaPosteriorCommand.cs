using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Estado de la firma a posteriori de una parte (HU #11196 / #11197). <see cref="Aplica"/> dice si la
/// opción debe ofrecerse (el representante tiene identidad Y firma del baúl vencidas);
/// <see cref="Marcado"/>, si el gestor ya la eligió.
/// </summary>
public sealed record FirmaPosteriorEstadoDto(
    bool Aplica,
    bool Marcado,
    string? RepresentanteNombre = null,
    DateTimeOffset? MarcadoAt = null);

/// <summary>
/// HU #11196 (AC1) — marca un trámite para firmarse a posteriori. Solo aplica cuando la parte es una
/// empresa cuyo representante legal tiene la identidad <b>y</b> la firma del baúl vencidas: si tuviera
/// cualquiera de las dos, el trámite puede firmarse ya y diferirlo solo lo retrasaría.
///
/// <para>La marca guarda el documento del representante, que es la llave del lote: cuando esa persona
/// valide su identidad, <see cref="Identity.DeferredSignatureBatchConsumer"/> firma todos sus trámites
/// marcados del tenant.</para>
/// </summary>
public sealed class MarcarFirmaPosteriorHandler(
    IProcedureInstanceRepository repo,
    IDeferredSignatureMarkRepository marks,
    ISignatureVaultPolicy? vaultPolicy = null)
{
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    public async Task<(FirmaPosteriorEstadoDto? Result, string? Error)> HandleAsync(
        Guid id, Guid tenantId, string? parte, CancellationToken ct = default)
    {
        var normalized = NormalizeParte(parte);
        if (normalized is null)
            return (null, "parte_invalida");

        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // D1/D2: solo borrador y subsanación. Un trámite que ya salió hacia el organismo no se difiere.
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, "not_draft");

        var (actor, subject, error) = ResolverSujeto(instance, normalized);
        if (error is not null)
            return (null, error);

        // Si ya hay con qué firmar, la opción no aplica (AC2 de la HU #11197): ofrecerla invitaría a
        // demorar un trámite que puede cerrarse hoy.
        if (await TieneFirmaDisponibleAsync(tenantId, instance, subject!, ct))
            return (null, "firma_disponible");

        var existente = await marks.FindPendienteAsync(tenantId, id, normalized, ct);
        if (existente is not null)
        {
            // Idempotente: volver a marcar no crea una segunda marca ni mueve la fecha original.
            return (Estado(aplica: true, existente, subject!.Nombre), null);
        }

        var mark = new DeferredSignatureMark
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            PartyRole = normalized,
            CompanyDocumentNumber = actor!.DocumentNumber,
            RepresentativeDocumentType = subject!.TipoDocumento!,
            RepresentativeDocumentNumber = subject.NumeroDocumento!,
            Estado = DeferredSignatureEstados.Pendiente,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        marks.Add(mark);
        await marks.SaveChangesAsync(ct);

        return (Estado(aplica: true, mark, subject.Nombre), null);
    }

    /// <summary>
    /// HU #11197 (AC1/AC2/AC4) — ¿aplica la opción para esta parte y ya está marcada? Consulta pura: no
    /// crea ni modifica nada.
    /// </summary>
    public async Task<(FirmaPosteriorEstadoDto? Result, string? Error)> ConsultarAsync(
        Guid id, Guid tenantId, string? parte, CancellationToken ct = default)
    {
        var normalized = NormalizeParte(parte);
        if (normalized is null)
            return (null, "parte_invalida");

        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var (_, subject, error) = ResolverSujeto(instance, normalized);
        if (error is not null)
            // Persona natural o sin representante: la opción sencillamente no existe, no es un error
            // que deba romperle la pantalla al gestor.
            return (new FirmaPosteriorEstadoDto(Aplica: false, Marcado: false), null);

        var existente = await marks.FindPendienteAsync(tenantId, id, normalized, ct);
        var editable = TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva);
        var aplica = editable && !await TieneFirmaDisponibleAsync(tenantId, instance, subject!, ct);

        return (Estado(aplica || existente is not null, existente, subject!.Nombre), null);
    }

    /// <summary>
    /// ¿La parte tiene YA con qué firmar? Firma del baúl vigente del representante o identidad aprobada y
    /// vigente de esa persona en el tenant. Cualquiera de las dos basta: son alternativas.
    /// </summary>
    private async Task<bool> TieneFirmaDisponibleAsync(
        Guid tenantId, ProcedureInstance instance, IdentitySubject subject, CancellationToken ct)
    {
        var tipo = subject.TipoDocumento!.Trim();
        var documento = subject.NumeroDocumento!.Trim();

        if (await _vaultPolicy.ResolveAsync(tenantId, tipo, documento, ct) is not null)
            return true;

        var now = DateTimeOffset.UtcNow;
        if (instance.BiometricValidations.Any(v =>
                string.Equals(v.DocumentType?.Trim(), tipo, StringComparison.OrdinalIgnoreCase)
                && string.Equals(v.DocumentNumber?.Trim(), documento, StringComparison.OrdinalIgnoreCase)
                && BiometricRules.EsAprobadaVigente(v, now)))
            return true;

        return await repo.FindVigenteApprovedByDocumentAsync(tenantId, tipo, documento, now, ct) is not null;
    }

    /// <summary>Actor de la parte y su sujeto de identidad. La firma a posteriori solo existe en persona jurídica.</summary>
    private static (ProcedureInstanceActor? Actor, IdentitySubject? Subject, string? Error) ResolverSujeto(
        ProcedureInstance instance, string parte)
    {
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
        if (actor is null)
            return (null, null, "sin_actor");

        if (!ActorPersonTypes.IsJuridical(actor.PersonType))
            return (null, null, "no_aplica");

        var subject = IdentitySubjectResolver.For(actor);
        if (!subject.EsRepresentanteLegal
            || string.IsNullOrWhiteSpace(subject.TipoDocumento)
            || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return (null, null, "sin_representante");

        return (actor, subject, null);
    }

    private static FirmaPosteriorEstadoDto Estado(bool aplica, DeferredSignatureMark? mark, string? nombre) =>
        new(aplica, mark is not null, nombre, mark?.CreatedAt);

    private static string? NormalizeParte(string? parte)
    {
        var p = parte?.Trim().ToLowerInvariant();
        return p is BiometricRules.ParteComprador or BiometricRules.ParteVendedor ? p : null;
    }
}
