using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #13144 (ADR-0066) — estado con que el evaluador único describe el mandatario de un trámite. Son los
/// valores que consumen el gate de radicación y el firmante previsto (#13145).
/// </summary>
public enum MandateSignerEstado
{
    /// <summary>Hay un mandatario resuelto, vigente y con firma válida.</summary>
    Valido,

    /// <summary>Ningún nivel resuelve un mandatario, o se descartaron solo por vigencia o estado.</summary>
    SinMandatario,

    /// <summary>Hay candidatos pero todos se descartaron por la firma (biometría, baúl, firma física sin migrar).</summary>
    FirmaInvalida,

    /// <summary>Mandato Persona jurídica, Mandato abierto o mandato personalizado de la compañía: no se exige mandatario.</summary>
    NoAplica,

    /// <summary>El trámite aún no tiene organismo elegido: no hay contra qué evaluar.</summary>
    PendienteOrganismo,

    /// <summary>Varios válidos sin nivel superior que los desempate: el OT elige al aprobar (el gate pasa).</summary>
    PendienteEleccionOt,
}

/// <summary>HU #13180 — vocabulario estable de los niveles de la prelación en la API (firmante previsto, indicador «Firmará»).</summary>
public static class MandateSignerLevelCodes
{
    public const string Explicita = "explicita";
    public const string OtParaCompania = "ot_para_compania";
    public const string PropioDeCompania = "propio_de_compania";
    public const string AsociadoDeOtraCompania = "asociado_de_otra_compania";
    public const string DefaultDelOt = "default_del_ot";
    public const string Ninguno = "ninguno";

    public static string ToCode(MandateSignerLevel level) => level switch
    {
        MandateSignerLevel.Explicita => Explicita,
        MandateSignerLevel.OtParaCompania => OtParaCompania,
        MandateSignerLevel.PropioDeCompania => PropioDeCompania,
        MandateSignerLevel.AsociadoDeOtraCompania => AsociadoDeOtraCompania,
        MandateSignerLevel.DefaultDelOt => DefaultDelOt,
        _ => Ninguno,
    };
}

/// <summary>Vocabulario estable de los estados del evaluador en la API (#13145) y en los metadatos del gate.</summary>
public static class MandateSignerEstados
{
    public const string Valido = "valido";
    public const string SinMandatario = "sin_mandatario";
    public const string FirmaInvalida = "firma_invalida";
    public const string NoAplica = "no_aplica";
    public const string PendienteOrganismo = "pendiente_organismo";
    public const string PendienteEleccionOt = "pendiente_eleccion_ot";

    public static string ToCode(MandateSignerEstado estado) => estado switch
    {
        MandateSignerEstado.Valido => Valido,
        MandateSignerEstado.SinMandatario => SinMandatario,
        MandateSignerEstado.FirmaInvalida => FirmaInvalida,
        MandateSignerEstado.NoAplica => NoAplica,
        MandateSignerEstado.PendienteOrganismo => PendienteOrganismo,
        _ => PendienteEleccionOt,
    };

    /// <summary>Motivo cuando no hay ningún candidato en ningún nivel.</summary>
    public const string MotivoSinCandidatos = "sin_mandatario_configurado";

    /// <summary>Motivos de <c>no_aplica</c>.</summary>
    public const string MotivoModoSinFirmante = "mandato_sin_firmante_persona";

    public const string MotivoMandatoDeCompania = "mandato_personalizado_de_compania";

    public const string MensajeSinMandatario =
        "Este organismo no tiene un mandatario activo para tu compañía. Pídele al organismo o al administrador " +
        "de tu compañía que registre uno.";

    /// <summary>
    /// HU #13137 — 409 <c>mandatario_requerido</c> sin ningún candidato válido: no es que «haya varios», es que no
    /// queda mandatario activo para elegir (p. ej. se dio de baja al asignado).
    /// </summary>
    public const string MensajeSinMandatarioAlAprobar =
        "No hay un mandatario disponible para esta compañía en este organismo: debe estar activo, vigente y " +
        "con firma válida. Registra o reactiva uno y vuelve a aprobar.";

    /// <summary>HU #13137 — 409 <c>mandatario_requerido</c> con candidatos: el OT debe elegir uno.</summary>
    public const string MensajeEleccionRequerida =
        "Falta elegir quién firma el mandato. Selecciona uno de los mandatarios disponibles y vuelve a " +
        "aprobar.";

    public const string MensajeFirmaInvalida =
        "El mandatario aún no puede firmar: necesita una firma guardada en el baúl o su validación de identidad aprobada.";
}

/// <summary>
/// Resultado del evaluador. <see cref="Candidatos"/> son los válidos sobre los que el OT puede elegir;
/// <see cref="Descartados"/> dicen quién se descartó y por qué. Nunca lleva documento de identidad ni ruta de
/// firma del mandatario (Ley 1581): <see cref="Signer"/> solo se usa dentro de Application.
/// </summary>
public sealed record MandateSignerEvaluacion(
    MandateSignerEstado Estado,
    MandateSignerLevel Nivel,
    MandateSignerCandidate? Signer,
    string? FormaFirma,
    string? Motivo,
    IReadOnlyList<MandateSignerCandidate> Candidatos,
    IReadOnlyList<MandateSignerDiscard> Descartados,
    Guid? TransitOfficeId)
{
    public static MandateSignerEvaluacion Simple(
        MandateSignerEstado estado, string? motivo = null, Guid? transitOfficeId = null) =>
        new(estado, MandateSignerLevel.Ninguno, null, null, motivo, [], [], transitOfficeId);

    /// <summary>Código de error del gate (<c>mandatario_no_configurado</c> / <c>mandatario_firma_invalida</c>), o nulo.</summary>
    public string? CodigoDeError => Estado switch
    {
        MandateSignerEstado.SinMandatario => TramiteEstadoErrores.MandatarioNoConfigurado,
        MandateSignerEstado.FirmaInvalida => TramiteEstadoErrores.MandatarioFirmaInvalida,
        _ => null,
    };

    public string? MensajeDeError => Estado switch
    {
        MandateSignerEstado.SinMandatario => MandateSignerEstados.MensajeSinMandatario,
        MandateSignerEstado.FirmaInvalida => MandateSignerEstados.MensajeFirmaInvalida,
        _ => null,
    };
}

/// <summary>
/// HU #13144 (ADR-0066) — EVALUADOR ÚNICO del mandatario de un trámite: decide si aplica, quién firma, en qué
/// nivel, con qué forma de firma y, si no hay, por qué. Lo reutilizan el gate de radicación
/// (<c>TramiteLifecycleService</c>), el firmante previsto y los candidatos del 409 (#13145). Compone la
/// prelación pura (<see cref="MandateSignerDefaultResolver"/>, #13142) con las exclusiones del ADR: mandato
/// Persona jurídica o Mandato abierto (<c>SkipsPersonSigner</c>) y mandato personalizado de la compañía
/// (ADR-0042). La plantilla propia del OT SÍ aplica: su PDF estampa las firmas (ADR-0066, duda D-5).
/// </summary>
public sealed class MandateSignerEvaluator(
    IMandateSignerDirectory directory,
    IMandateRequirementPolicy? mandatePolicy = null,
    ISignatureVaultPolicy? vaultPolicy = null,
    IPersonalizedDocumentResolver? personalizedDocumentResolver = null)
{
    private readonly IMandateRequirementPolicy _mandatePolicy = mandatePolicy ?? NullMandateRequirementPolicy.Instance;

    private readonly IPersonalizedDocumentResolver _personalized =
        personalizedDocumentResolver ?? NullPersonalizedDocumentResolver.Instance;

    /// <summary>
    /// Evalúa el mandatario del trámite. <paramref name="eleccionOt"/> (opcional) es la elección explícita del
    /// OT al aprobar; en el gate de radicación y en el firmante previsto va nula.
    /// </summary>
    public async Task<MandateSignerEvaluacion> EvaluateAsync(
        ProcedureInstance instance,
        Guid? eleccionOt = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // Sin organismo no hay contra qué evaluar (en traspaso lo fija el RUNT; en matrícula se elige después).
        if (MandateSignerSelectionResolver.ResolveTransitOfficeId(instance) is not { } officeId)
        {
            return MandateSignerEvaluacion.Simple(MandateSignerEstado.PendienteOrganismo);
        }

        // Config del organismo × compañía: por id cuando se conoce (el código de field_values no es llave
        // confiable, ver IMandateRequirementPolicy); sin fila de OT (legado) resuelve como «signer».
        var config = await _mandatePolicy.ResolveByOfficeIdAsync(officeId, instance.TenantId, ct).ConfigureAwait(false);
        if (config is null)
        {
            var code = instance.FieldValues.FirstOrDefault(f =>
                string.Equals(f.FieldKey, "transit_office_code", StringComparison.OrdinalIgnoreCase))?.ValueText;
            if (!string.IsNullOrWhiteSpace(code))
            {
                config = await _mandatePolicy.ResolveAsync(code, instance.TenantId, ct).ConfigureAwait(false);
            }
        }

        // Persona jurídica (institutional) o Mandato abierto: no hay firmante persona y no es bloqueo.
        if (MandatoAssignmentModeCodes.SkipsPersonSigner(config?.AssignmentMode))
        {
            return MandateSignerEvaluacion.Simple(
                MandateSignerEstado.NoAplica, MandateSignerEstados.MotivoModoSinFirmante, officeId);
        }

        // Mandato personalizado de la compañía (ADR-0042): PDF estático que no estampa firmante.
        var personalizado = await _personalized.ResolveAsync(instance.TenantId, ["mandato"], ct).ConfigureAwait(false);
        if (personalizado.Resolved.Any(r => string.Equals(r.Tipo, "mandato", StringComparison.OrdinalIgnoreCase)))
        {
            return MandateSignerEvaluacion.Simple(
                MandateSignerEstado.NoAplica, MandateSignerEstados.MotivoMandatoDeCompania, officeId);
        }

        // Mismo criterio que la pantalla y el PDF: en borrador/subsanación se recalcula; fuera de ahí manda
        // el firmante guardado si sigue siendo válido.
        var editable = TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva);
        var (prelacion, todos) = await MandateSignerPrelacionLoader
            .ResolveAsync(
                directory, vaultPolicy, officeId, instance.TenantId,
                MandateSignerSelectionResolver.ResolveNitMandante(instance), config,
                eleccionOt, editable ? null : instance.MandateSignerId, ct)
            .ConfigureAwait(false);

        // Trámite en estado FINAL (aprobado, revocado): el mandato ya se emitió con el firmante que quedó guardado y
        // no se puede regenerar. Si ese mandatario venció o se dio de baja DESPUÉS, el indicador no debe cambiar de
        // persona (diría una cosa y el documento firmado otra): se sigue mostrando quien firmó.
        if (!editable
            && TramiteEstado.Finales.Contains(instance.Status)
            && instance.MandateSignerId is { } guardado
            && prelacion.Signer?.Id != guardado
            && todos.FirstOrDefault(c => c.Id == guardado) is { } firmante)
        {
            return new MandateSignerEvaluacion(
                MandateSignerEstado.Valido, MandateSignerLevel.Explicita, firmante, firmante.SignatureMethod, null,
                prelacion.Validos, prelacion.Descartados, officeId);
        }

        return Clasificar(prelacion, officeId);
    }

    /// <summary>Traduce la prelación al estado del evaluador (puro, sin IO).</summary>
    public static MandateSignerEvaluacion Clasificar(MandateSignerPrelacion prelacion, Guid? transitOfficeId)
    {
        ArgumentNullException.ThrowIfNull(prelacion);

        if (prelacion.Signer is { } signer)
        {
            return new MandateSignerEvaluacion(
                MandateSignerEstado.Valido, prelacion.Level, signer, signer.SignatureMethod, null,
                prelacion.Validos, prelacion.Descartados, transitOfficeId);
        }

        // Varios válidos sin desempate (o elección del OT inválida con válidos disponibles): hay mandatarios
        // y el OT elige al aprobar. El gate de radicación pasa.
        if (prelacion.Validos.Count > 0)
        {
            return new MandateSignerEvaluacion(
                MandateSignerEstado.PendienteEleccionOt, prelacion.Level, null, null, null,
                prelacion.Validos, prelacion.Descartados, transitOfficeId);
        }

        // Sin ningún válido: la firma física sin migrar no cuenta como firma válida (P4), y entre los
        // descartes gana el de mejor nivel; si alguno es de firma, el motivo es de firma.
        var porNivel = prelacion.Descartados.OrderBy(d => d.Level).ToList();
        var deFirma = porNivel.FirstOrDefault(d => MandateSignerDiscardReasons.EsDeFirma(d.Motivo));
        if (deFirma is not null)
        {
            return new MandateSignerEvaluacion(
                MandateSignerEstado.FirmaInvalida, MandateSignerLevel.Ninguno, null, null, deFirma.Motivo,
                [], prelacion.Descartados, transitOfficeId);
        }

        var motivo = porNivel.FirstOrDefault()?.Motivo ?? MandateSignerEstados.MotivoSinCandidatos;
        return new MandateSignerEvaluacion(
            MandateSignerEstado.SinMandatario, MandateSignerLevel.Ninguno, null, null, motivo,
            [], prelacion.Descartados, transitOfficeId);
    }
}
