using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #11203 — un mandatario que el gestor puede elegir para que firme el mandato del trámite. Lleva
/// los datos que necesita para decidir: quién es, con qué documento y con QUÉ puede firmar.
///
/// <para>Son dos vías alternativas, no acumulativas: firma del baúl vigente <b>o</b> validación de
/// identidad vigente. Antes solo se informaba la identidad, así que un mandatario perfectamente capaz
/// de firmar con su firma del baúl se anunciaba como si le faltara algo.</para>
/// </summary>
public sealed record MandateSignerOptionDto(
    Guid Id,
    string Nombre,
    string TipoDocumento,
    string Documento,
    bool IdentidadVigente,
    DateTimeOffset? IdentidadHasta,
    bool FirmaBaulVigente = false,
    /// <summary>
    /// Firma A MANO ante el organismo del trámite. Quien firma a mano no necesita ninguna de las dos
    /// vías anteriores: el documento le deja la línea.
    /// </summary>
    bool FirmaFisica = false);

/// <summary>
/// Mandatarios disponibles para el trámite y cuál está elegido. <see cref="Editable"/> es falso fuera
/// de borrador: entonces el mandatario se muestra pero no se cambia (AC5).
/// </summary>
public sealed record MandateSignerSelectionDto(
    IReadOnlyList<MandateSignerOptionDto> Opciones,
    Guid? ElegidoId,
    bool Editable);

/// <summary>
/// HU #11203 — mandatarios habilitados para el organismo del trámite en la compañía gestora.
/// Se muestra en el wizard (paso FUR / radicación) para que el gestor pueda elegir quién firma
/// el mandato junto a los documentos del expediente. No es obligatorio elegir.
/// Institucional / abierto (regla compañía×OT): no se ofrecen candidatos persona.
/// </summary>
public sealed class ListMandateSignerOptionsHandler(
    IProcedureInstanceRepository repo,
    IMandateSignerDirectory directory,
    ISignatureVaultPolicy? vaultPolicy = null,
    IMandateRequirementPolicy? mandatePolicy = null)
{
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;
    private readonly IMandateRequirementPolicy _mandatePolicy =
        mandatePolicy ?? NullMandateRequirementPolicy.Instance;

    public async Task<(MandateSignerSelectionDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var editable = TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva);
        var transitOfficeId = MandateSignerSelectionResolver.ResolveTransitOfficeId(instance);

        // Sin organismo todavía no hay a quién ofrecer: en traspaso lo fija el RUNT al consultar el
        // vehículo y en matrícula lo elige el gestor en el primer paso (HU #11199/#11200).
        if (transitOfficeId is not { } officeId)
            return (new MandateSignerSelectionDto([], instance.MandateSignerId, editable), null);

        // Tipo institucional / abierto: el mandato no lleva firmante persona — no mostrar selector.
        var officeCode = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_code", StringComparison.OrdinalIgnoreCase))?.ValueText;
        MandateOtConfig? mandateConfig = null;
        if (!string.IsNullOrWhiteSpace(officeCode))
        {
            mandateConfig = await _mandatePolicy
                .ResolveAsync(officeCode, tenantId, ct)
                .ConfigureAwait(false);
            if (MandatoAssignmentModeCodes.SkipsPersonSigner(mandateConfig?.AssignmentMode))
                return (new MandateSignerSelectionDto([], null, editable), null);
        }

        var candidatos = await directory
            .GetCandidatesAsync(officeId, tenantId, MandateSignerSelectionResolver.ResolveNitMandante(instance), ct)
            .ConfigureAwait(false);
        candidatos = await MandateSignerSelectionResolver
            .WithOtDefaultAsync(candidatos, mandateConfig?.OtDefaultMandateSignerId, directory, ct)
            .ConfigureAwait(false);

        // La firma del baúl se resuelve por documento y contra el tenant de la gestora, igual que hace el
        // generador del mandato (HU #11030). Son un puñado de mandatarios por organismo, así que se
        // resuelven uno a uno en vez de montar una consulta en lote que duplicaría la política del baúl
        // (incluido el flag signature_vault_enabled del tenant).
        var opciones = new List<MandateSignerOptionDto>(candidatos.Count);
        foreach (var c in candidatos)
        {
            var tipoDoc = string.IsNullOrWhiteSpace(c.TipoDocumento) ? "CC" : c.TipoDocumento.Trim();
            // Quien firma a mano no necesita el baúl: ni se consulta.
            var conBaul = !c.FirmaFisica
                && !string.IsNullOrWhiteSpace(c.Documento)
                && await _vaultPolicy
                    .ResolveAsync(tenantId, tipoDoc, c.Documento.Trim(), ct)
                    .ConfigureAwait(false) is not null;

            opciones.Add(new MandateSignerOptionDto(
                c.Id, c.Nombre, tipoDoc, c.Documento, c.IdentityVigente, c.IdentityValidUntil, conBaul,
                c.FirmaFisica));
        }

        // Prioridad: ya elegido en el trámite (solo fuera de borrador) → default cliente×OT →
        // default OT → vacío. En borrador se ignora MandateSignerId auto-congelado para que un
        // cambio en Mandatos se vea al reimprimir.
        var eleccionCongelada = editable ? null : instance.MandateSignerId;
        var elegido = MandateSignerDefaultResolver.Resolve(
            opciones.ConvertAll(o => o.Id),
            eleccionCongelada,
            mandateConfig?.OtDefaultMandateSignerId,
            mandateConfig?.DefaultMandateSignerId);

        return (new MandateSignerSelectionDto(opciones, elegido, editable), null);
    }
}

/// <summary>
/// HU #11203 (AC4/AC5) — fija el mandatario que firma. Solo en borrador (o subsanación): una vez el
/// trámite salió de allí, cambiar quién firma alteraría un documento ya generado y radicado.
/// </summary>
public sealed class SetMandateSignerHandler(
    IProcedureInstanceRepository repo,
    IMandateSignerDirectory directory)
{
    public async Task<string?> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        Guid mandateSignerId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return "not_found";

        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return "not_draft";

        var transitOfficeId = MandateSignerSelectionResolver.ResolveTransitOfficeId(instance);
        if (transitOfficeId is not { } officeId)
            return "sin_organismo";

        // El elegido tiene que ser uno de los habilitados para ese organismo y esa compañía: aceptar
        // cualquier id dejaría el trámite apuntando a un mandatario que no puede firmarlo, y eso solo
        // se descubriría al aprobar.
        var candidatos = await directory
            .GetCandidatesAsync(officeId, tenantId, MandateSignerSelectionResolver.ResolveNitMandante(instance), ct)
            .ConfigureAwait(false);
        if (candidatos.All(c => c.Id != mandateSignerId))
            return "mandatario_no_habilitado";

        instance.MandateSignerId = mandateSignerId;
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }
}

/// <summary>Resolución compartida del organismo del trámite, para no duplicarla entre los dos casos.</summary>
internal static class MandateSignerSelectionResolver
{
    /// <summary>
    /// NIT de la empresa que OTORGA el mandato en <b>traspaso</b>: el vendedor (su vehículo).
    /// En matrícula inicial no hay vendedor: no se usa la cédula del comprador como NIT de empresa
    /// representada, porque eso filtraba a los mandatarios de la gestora y dejaba firmando a uno
    /// “aplica a todas” distinto del default de Mandatos.
    /// </summary>
    public static string? ResolveNitMandante(Flit.Tramites.Domain.Entities.ProcedureInstance instance)
    {
        var vendedor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, "vendedor", StringComparison.OrdinalIgnoreCase));

        var nit = vendedor?.DocumentNumber?.Trim();
        return string.IsNullOrEmpty(nit) ? null : nit;
    }

    /// <summary>
    /// Organismo del trámite. Durante el wizard vive en <c>field_values.transit_office_id</c>; la columna
    /// solo se fija al radicar (<c>TramiteLifecycleService</c>), así que mirar solo la columna dejaría la
    /// elección sin candidatos justo cuando hace falta.
    /// </summary>
    public static Guid? ResolveTransitOfficeId(Flit.Tramites.Domain.Entities.ProcedureInstance instance)
    {
        if (instance.TransitOfficeId is { } columna && columna != Guid.Empty)
            return columna;

        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// HU-L8 — el default del OT entra al conjunto aunque no esté en mandate_signer_companies de la gestora.
    /// </summary>
    public static async Task<IReadOnlyList<MandateSignerCandidate>> WithOtDefaultAsync(
        IReadOnlyList<MandateSignerCandidate> candidates,
        Guid? otDefaultId,
        IMandateSignerDirectory directory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(directory);

        if (otDefaultId is not { } id || id == Guid.Empty)
            return candidates;
        if (candidates.Any(c => c.Id == id))
            return candidates;

        var extra = await directory.GetByIdAsync(id, ct).ConfigureAwait(false);
        return extra is null ? candidates : [.. candidates, extra];
    }
}
