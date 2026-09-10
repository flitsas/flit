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
    ISignatureVaultPolicy? vaultPolicy = null,
    IMandateSignerDirectory? mandateDirectory = null)
{
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    // El mandatario tambien puede quedarse sin con que firmar, y entonces necesita la misma salida que
    // el representante legal en vez de un bloqueo. Default inerte: sin directorio la opcion no se ofrece.
    private readonly IMandateSignerDirectory _mandateDirectory =
        mandateDirectory ?? NullMandateSignerDirectory.Instance;

    /// <summary>Parte sintetica del mandatario: no es un actor del tramite, pero si un firmante.</summary>
    public const string ParteMandatario = "mandatario";

    /// <param name="documento">
    /// ADR-0053 (Múltiple Propietario) — documento del sujeto (representante legal) al que se refiere
    /// esta llamada, cuando el rol tiene 2+ actores jurídicos. Opcional/aditivo: con 1 solo actor en el
    /// rol (caso mayoritario) se ignora — cero regresión.
    /// </param>
    public async Task<(FirmaPosteriorEstadoDto? Result, string? Error)> HandleAsync(
        Guid id, Guid tenantId, string? parte, string? documento = null, CancellationToken ct = default)
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

        var (actor, subject, identidadAdmin, error) = await ResolverSujetoAsync(instance, normalized, documento, ct);
        if (error is not null)
            return (null, error);

        // Si ya hay con qué firmar, la opción no aplica (AC2 de la HU #11197): ofrecerla invitaría a
        // demorar un trámite que puede cerrarse hoy.
        if (identidadAdmin || await TieneFirmaDisponibleAsync(tenantId, instance, subject!, ct))
            return (null, "firma_disponible");

        // ADR-0053 — la marca pendiente se busca por (tenant, trámite, rol, DOCUMENTO DEL REPRESENTANTE):
        // con 2+ actores jurídicos del mismo rol, cada copropietario tiene su propia marca independiente.
        var existente = await marks.FindPendienteAsync(tenantId, id, normalized, subject!.NumeroDocumento!, ct);
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
            // El mandatario no representa a ninguna parte del trámite: no hay NIT representado que anotar.
            CompanyDocumentNumber = actor?.DocumentNumber,
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
        Guid id, Guid tenantId, string? parte, string? documento = null, CancellationToken ct = default)
    {
        var normalized = NormalizeParte(parte);
        if (normalized is null)
            return (null, "parte_invalida");

        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var (_, subject, identidadAdmin, error) = await ResolverSujetoAsync(instance, normalized, documento, ct);
        if (error is not null)
            // Persona natural, sin representante o sin mandatario elegido: la opción sencillamente no
            // existe, no es un error que deba romperle la pantalla al gestor.
            return (new FirmaPosteriorEstadoDto(Aplica: false, Marcado: false), null);

        var existente = await marks.FindPendienteAsync(tenantId, id, normalized, subject!.NumeroDocumento!, ct);
        var editable = TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva);
        var aplica = editable
            && !identidadAdmin
            && !await TieneFirmaDisponibleAsync(tenantId, instance, subject!, ct);

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

    /// <summary>
    /// Quién firma esta parte y con qué documento.
    ///
    /// <para>Hay dos formas distintas de firmante. Las partes del trámite (comprador/vendedor) firman por
    /// su representante legal cuando son personas jurídicas. El <b>mandatario</b> no es un actor del
    /// trámite sino el firmante del mandato elegido para él, y su identidad vive en las validaciones de
    /// Admin —no en las biométricas del trámite—, así que su vigencia se devuelve aparte
    /// (<c>IdentidadAdmin</c>) en vez de buscarla donde no está.</para>
    /// </summary>
    private async Task<(ProcedureInstanceActor? Actor, IdentitySubject? Subject, bool IdentidadAdmin, string? Error)>
        ResolverSujetoAsync(ProcedureInstance instance, string parte, string? documento, CancellationToken ct)
    {
        if (string.Equals(parte, ParteMandatario, StringComparison.Ordinal))
        {
            if (instance.MandateSignerId is not { } signerId)
                return (null, null, false, "sin_mandatario");

            var signer = await _mandateDirectory.GetByIdAsync(signerId, ct).ConfigureAwait(false);
            if (signer is null || string.IsNullOrWhiteSpace(signer.Documento))
                return (null, null, false, "sin_mandatario");

            var sujetoMandatario = new IdentitySubject(
                Nombre: signer.Nombre,
                TipoDocumento: string.IsNullOrWhiteSpace(signer.TipoDocumento) ? "CC" : signer.TipoDocumento!.Trim(),
                NumeroDocumento: signer.Documento.Trim(),
                // No hay correo del mandatario en el directorio de trámites, y aquí no hace falta: la
                // marca solo necesita a quién esperar, no a quién escribirle.
                Email: null,
                // No firma como representante legal de una parte: firma el mandato por la gestora.
                EsRepresentanteLegal: false);

            // Quien firma A MANO no tiene nada que esperar: el documento le deja la línea y la suscribe
            // en papel. Ofrecerle diferir la firma sería ofrecerle aplazar algo que no depende de nadie.
            return (null, sujetoMandatario, signer.IdentityVigente || signer.FirmaFisica, null);
        }

        // ADR-0053 (Múltiple Propietario) — el actor se resuelve por el documento DECLARADO (con 1 solo
        // actor jurídico en el rol, caso mayoritario, cae siempre a ese único actor: cero regresión).
        var actor = IdentitySubjectResolver.ActorPorDocumento(instance, parte, documento);
        if (actor is null)
            return (null, null, false, "sin_actor");

        if (!ActorPersonTypes.IsJuridical(actor.PersonType))
            return (null, null, false, "no_aplica");

        var subject = IdentitySubjectResolver.For(actor);
        if (!subject.EsRepresentanteLegal
            || string.IsNullOrWhiteSpace(subject.TipoDocumento)
            || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return (null, null, false, "sin_representante");

        return (actor, subject, false, null);
    }

    private static FirmaPosteriorEstadoDto Estado(bool aplica, DeferredSignatureMark? mark, string? nombre) =>
        new(aplica, mark is not null, nombre, mark?.CreatedAt);

    private static string? NormalizeParte(string? parte)
    {
        var p = parte?.Trim().ToLowerInvariant();
        return p is BiometricRules.ParteComprador or BiometricRules.ParteVendedor or ParteMandatario
            ? p
            : null;
    }
}
