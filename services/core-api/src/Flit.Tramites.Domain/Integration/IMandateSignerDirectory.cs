namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Un mandatario candidato para firmar el mandato de un trámite (ADR-0036, HU #10916): el firmante
/// (<c>admin.mandate_signers</c>) activo asignado a la compañía gestora en el OT del trámite. <c>UserId</c>
/// es la cuenta de OT vinculada (§D9): la llave del cotejo automático con el usuario que aprueba.
/// <c>IdentityVigente</c> (HU #10916/#10911): el mandatario tiene una validación de identidad admin
/// APROBADA y VIGENTE (se valida una vez y se apalanca en los mandatos mientras esté vigente); un
/// mandatario sin identidad vigente NO puede firmar (se exige validar antes de aprobar).
/// </summary>
public sealed record MandateSignerCandidate(
    Guid Id, string Nombre, string Documento, Guid? UserId, bool IdentityVigente = true,
    // HU #11030 — insumos de la FIRMA del mandatario en el contrato de mandato, con la misma
    // precedencia que el resto de documentos: firma del baúl si la tiene, y si no el certificado de su
    // validación de identidad vigente. Sin ninguno, el PDF deja la línea en blanco.
    Guid? SignatureVaultId = null,
    string? TipoDocumento = null,
    string? CertificadoIdentidad = null,
    // HU #11203 — hasta cuándo vale su identidad. El gestor elige quién firma al registrar el trámite y
    // necesita ver la vigencia ahí mismo: un mandatario cuya validación caduca antes de la aprobación
    // bloquearía el trámite justo al final.
    DateTimeOffset? IdentityValidUntil = null,
    /// <summary>
    /// Firma A MANO ante el organismo consultado. Quien firma a mano NO necesita firma del baúl ni
    /// validación de identidad: el documento le deja la línea y él la suscribe. Exigirle una de las dos
    /// bloquearía un mandato que se firma en papel.
    ///
    /// <para>Es una propiedad del vínculo (mandatario × organismo), así que solo tiene sentido cuando la
    /// consulta trae organismo: <c>GetByIdAsync</c> no lo sabe y devuelve <c>false</c>.</para>
    /// </summary>
    bool FirmaFisica = false,
    /// <summary>
    /// HU #13130 (ADR-0061) — el mandatario tiene FIRMA VÁLIDA: su vigencia propia está activa Y, si firma
    /// con biometría, su validación biométrica está aprobada (sin renovación, HU #13130b; la regla de 30 días rige solo el trámite). Las
    /// dos vigencias conviven. Si es <c>false</c> no se estampa firma ni sello y
    /// <see cref="MotivoSinFirma"/> dice por qué. Por defecto <c>true</c>: sin dato, el comportamiento no cambia.
    /// </summary>
    bool FirmaValida = true,
    /// <summary>Motivo de <c>FirmaValida == false</c>: <c>mandatario_fuera_de_vigencia</c>, <c>mandatario_inactivo</c>
    /// o <c>sin_validacion_aprobada</c>.</summary>
    string? MotivoSinFirma = null,
    /// <summary>
    /// HU #13142 (ADR-0066) — origen del vínculo con la compañía gestora: <c>organismo</c> | <c>super_admin</c> |
    /// <c>compania</c> | <c>asociado</c> (el mandatario de otra compañía asociado a la del trámite, HU #13180). Decide el nivel de la prelación.
    /// El default <c>organismo</c> es el de la columna <c>configured_by_scope</c>.
    /// </summary>
    string Origen = MandateSignerOrigins.Organismo,
    /// <summary>HU #13142 — modelo del mandatario: <c>natural</c> | <c>juridica</c> | <c>formato_blanco</c>.</summary>
    string SignerModel = MandateSignerOrigins.ModeloNatural,
    /// <summary>
    /// HU #13142 — forma de firma EFECTIVA: <c>baul</c> | <c>biometria</c>; nula en <c>juridica</c> y
    /// <c>formato_blanco</c>. En un natural legado sin forma se infiere (baúl si tiene firma vinculada).
    /// Nula con natural = sin dato: no se exige baúl.
    /// </summary>
    string? SignatureMethod = null,
    /// <summary>
    /// HU #13142 — la firma del baúl del mandatario está vigente hoy. La completa la capa de aplicación con
    /// <see cref="ISignatureVaultPolicy"/> (el directorio no consulta el baúl); solo se exige con
    /// <c>SignatureMethod = baul</c>.
    /// </summary>
    bool BaulVigente = false,
    /// <summary>HU #13142 — baja lógica (<c>deleted_at</c>). Solo llega en <c>true</c> al pedir la referencia
    /// de un trámite ya firmado con <c>incluirEliminados</c>; la prelación lo descarta.</summary>
    bool Eliminado = false,
    /// <summary>
    /// HU #13180b (Feature #13119) — tenants de las compañías a las que el mandatario está vinculado (vínculos
    /// activos): ahí pueden vivir su firma del baúl y su validación biométrica, que NO siempre están en el tenant de
    /// la compañía del trámite (un asociado de otra compañía, o el default del OT). Nulo o vacío ⇒ solo se busca en
    /// el tenant del trámite. Ver <see cref="VaultTenants"/>.
    /// </summary>
    IReadOnlyList<Guid>? VaultTenantIds = null)
{
    /// <summary>
    /// Tenants donde buscar su firma del baúl, en orden: primero el de la compañía del trámite (comportamiento
    /// histórico) y luego los de sus compañías vinculadas. Sin repetidos.
    /// </summary>
    public IReadOnlyList<Guid> VaultTenants(Guid tenantDelTramite) =>
        [tenantDelTramite, .. (VaultTenantIds ?? []).Where(t => t != tenantDelTramite && t != Guid.Empty).Distinct()];
}

/// <summary>Valores del origen y del modelo del mandatario que usa la prelación (ADR-0066, ADR-0061).</summary>
public static class MandateSignerOrigins
{
    public const string Organismo = "organismo";
    public const string SuperAdmin = "super_admin";
    public const string Compania = "compania";
    public const string Asociado = "asociado";

    public const string ModeloNatural = "natural";
    public const string ModeloJuridica = "juridica";
    public const string ModeloFormatoBlanco = "formato_blanco";

    public const string FormaBaul = "baul";
    public const string FormaBiometria = "biometria";
}

/// <summary>
/// Puerto para consultar los mandatarios registrados por el OT para una compañía gestora (ADR-0036,
/// HU #10916). Desacopla el módulo de trámites del de Admin (mismo patrón que
/// <see cref="IMandateRequirementPolicy"/>). La resolución del firmante (auto/selección) NO vive aquí:
/// es la función pura <see cref="MandateSignerSelector.Resolve"/>. Este puerto solo lee.
/// </summary>
public interface IMandateSignerDirectory
{
    /// <summary>
    /// Mandatarios ACTIVOS asignados a la compañía <paramref name="companyTenantId"/> en el OT
    /// <paramref name="transitOfficeId"/> (join <c>mandate_signers ↔ mandate_signer_companies</c>).
    /// Lista vacía si el OT/compañía no tiene mandatarios configurados.
    /// </summary>
    /// <param name="nitMandante">
    /// OBSOLETO E IGNORADO (HU #13179, Feature #13119): la resolución ya no compara el NIT del mandante ni
    /// consulta <c>mandate_signer_represented_companies</c>. El candidato de la compañía propietaria se incluye
    /// siempre; la asociación entre compañías va por tenant (HU #13180). Se conserva el parámetro para no
    /// romper a los llamadores.
    /// </param>
    Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
        Guid transitOfficeId,
        Guid companyTenantId,
        string? nitMandante = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// El mandatario por su id (default del OT, relleno del PDF), o <c>null</c> si no existe, está inactivo o
    /// tiene baja lógica (HU #13142, ADR-0066: un eliminado no puede ser firmante de nadie nuevo).
    /// </summary>
    Task<MandateSignerCandidate?> GetByIdAsync(Guid mandateSignerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #13142 (AC4) — igual que <see cref="GetByIdAsync(Guid, CancellationToken)"/> pero, con
    /// <paramref name="incluirEliminados"/>, conserva la referencia a un mandatario con baja lógica marcándolo
    /// <see cref="MandateSignerCandidate.Eliminado"/>: el trámite ya firmado sigue apuntando a quien firmó.
    /// La implementación por defecto delega en la consulta sin eliminados.
    /// </summary>
    Task<MandateSignerCandidate?> GetByIdAsync(
        Guid mandateSignerId, bool incluirEliminados, CancellationToken cancellationToken = default) =>
        GetByIdAsync(mandateSignerId, cancellationToken);
}

/// <summary>Directorio seguro que NUNCA resuelve mandatarios (para dominio/tests que no lo ejercitan).</summary>
public sealed class NullMandateSignerDirectory : IMandateSignerDirectory
{
    public static NullMandateSignerDirectory Instance { get; } = new();

    public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
        Guid transitOfficeId,
        Guid companyTenantId,
        string? nitMandante = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MandateSignerCandidate>>([]);

    public Task<MandateSignerCandidate?> GetByIdAsync(
        Guid mandateSignerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<MandateSignerCandidate?>(null);
}

/// <summary>Estado de la resolución del firmante del mandato al aprobar (ADR-0036, HU #10916).</summary>
public enum MandateSignerResolutionStatus
{
    /// <summary>Firmante único determinado (uno solo, cotejo por usuario, o selección explícita válida).</summary>
    Resolved,

    /// <summary>Varios candidatos sin cotejo único: el aprobador debe elegir uno explícitamente (409).</summary>
    RequiereSeleccion,

    /// <summary>El OT/compañía no tiene mandatarios configurados: no hay firmante persona que asignar.</summary>
    NoConfigurado,
}

/// <summary>Resultado de la resolución del firmante: estado + candidato elegido (null salvo <c>Resolved</c>).</summary>
public sealed record MandateSignerResolution(MandateSignerResolutionStatus Status, MandateSignerCandidate? Signer);

/// <summary>
/// Regla PURA de resolución del firmante del mandato al aprobar (ADR-0036 §D9, HU #10916):
/// <list type="bullet">
///   <item>Selección explícita: válida solo si el id está entre los candidatos; si no, exige elegir.</item>
///   <item>Cero candidatos ⇒ <see cref="MandateSignerResolutionStatus.NoConfigurado"/>.</item>
///   <item>Un candidato ⇒ auto.</item>
///   <item>Varios ⇒ cotejo por la cuenta de usuario que aprueba; match ÚNICO ⇒ auto; si no, exige elegir.</item>
/// </list>
/// </summary>
public static class MandateSignerSelector
{
    public static MandateSignerResolution Resolve(
        IReadOnlyList<MandateSignerCandidate> candidates,
        Guid? approvingUserId,
        Guid? explicitSignerId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // Selección explícita del aprobador: debe pertenecer al conjunto de candidatos válidos.
        if (explicitSignerId is { } chosenId && chosenId != Guid.Empty)
        {
            var chosen = candidates.FirstOrDefault(c => c.Id == chosenId);
            return chosen is not null
                ? new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, chosen)
                : new MandateSignerResolution(MandateSignerResolutionStatus.RequiereSeleccion, null);
        }

        if (candidates.Count == 0)
            return new MandateSignerResolution(MandateSignerResolutionStatus.NoConfigurado, null);

        if (candidates.Count == 1)
            return new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, candidates[0]);

        // Varios mandatarios: cotejar por la cuenta de usuario que aprueba (§D9). Auto solo si el match
        // es ÚNICO (dos mandatarios con el mismo user_id sería un dato inconsistente ⇒ exige elegir).
        if (approvingUserId is { } uid && uid != Guid.Empty)
        {
            var matches = candidates.Where(c => c.UserId == uid).ToList();
            if (matches.Count == 1)
                return new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, matches[0]);
        }

        return new MandateSignerResolution(MandateSignerResolutionStatus.RequiereSeleccion, null);
    }
}
