using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12775 — qué exime al actor de cargar el certificado de Cámara de Comercio, cuando algo lo exime.
/// </summary>
public enum CamaraComercioExencion
{
    /// <summary>Nada lo exime: el certificado es obligatorio.</summary>
    Ninguna = 0,

    /// <summary>Firma precargada activa y vigente en el baúl de la persona que representa al actor.</summary>
    FirmaPrecargada = 1,

    /// <summary>Escritura activa y vigente en el directorio de la compañía del actor.</summary>
    EscrituraVigente = 2,
}

/// <summary>
/// HU #12775 — obligatoriedad del certificado de Cámara de Comercio para UN actor persona jurídica.
/// <para><see cref="EsObligatorio"/> y <see cref="Exencion"/> son dos caras de lo mismo, y se
/// devuelven las dos a propósito: el paso del actor necesita la primera para bloquear o no, y la
/// segunda para decirle al gestor POR QUÉ el buzón quedó opcional. Un mensaje genérico —«ya está
/// acreditado»— obligaría al gestor a ir a buscar cuál de las dos cosas tiene la compañía.</para>
/// </summary>
public sealed record CamaraComercioRequirement(
    string Rol,
    string Tipo,
    bool EsObligatorio,
    CamaraComercioExencion Exencion);

/// <summary>
/// HU #12775 (Feature #12773) — decide, por cada actor persona jurídica del trámite, si el
/// certificado de Cámara de Comercio es obligatorio u opcional, y por qué.
///
/// <para><b>La escalera, en orden:</b></para>
/// <list type="number">
///   <item>Firma precargada vigente en el baúl → OPCIONAL (<see cref="CamaraComercioExencion.FirmaPrecargada"/>).</item>
///   <item>Sin firma, pero con escritura vigente → OPCIONAL (<see cref="CamaraComercioExencion.EscrituraVigente"/>).</item>
///   <item>Ninguna de las dos → OBLIGATORIO.</item>
/// </list>
///
/// <para><b>Por qué la firma va primero y no es un empate.</b> Un actor puede tener las dos cosas, y
/// entonces hay que elegir cuál se le nombra. Se nombra la firma porque es la que además cambia el
/// modelo de firmado del trámite: es el hecho más específico de los dos. El orden es fijo para que
/// dos renders del mismo paso no alternen el mensaje.</para>
///
/// <para><b>El certificado es de la familia de las escrituras, no de la del baúl.</b> Cámara de
/// Comercio y escritura acreditan lo mismo —quién representa a la sociedad— y por eso son sustitutos.
/// La firma del baúl resuelve cómo se firma, no quién representa, así que técnicamente no sustituye a
/// ninguna de las dos; exime por decisión de negocio (Épica #12754), no por equivalencia documental.
/// Queda anotado aquí porque es justo lo que invita a «simplificar» este resolutor más adelante.</para>
///
/// <para><b>La obligatoriedad es automática.</b> No hay parámetro de administración que la altere:
/// se deriva por completo de esta escalera.</para>
/// </summary>
public sealed class CamaraComercioRequirementResolver(
    ISignatureVaultPolicy? vaultPolicy = null,
    IProcedureDeedResolver? deedResolver = null)
{
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;
    private readonly IProcedureDeedResolver _deedResolver = deedResolver ?? NullProcedureDeedResolver.Instance;

    /// <summary>
    /// HU #12775 AC3 — primer rol cuyo certificado es OBLIGATORIO y no está cargado en el trámite, o
    /// <c>null</c> si ninguno falta. Es la misma escalera del paso del actor, aplicada al radicar: el
    /// bloqueo del asistente es solo del cliente, y un borrador que ya había pasado ese paso (o una
    /// llamada directa a la API) llegaría a preparación sin el documento.
    /// </summary>
    public async Task<string?> RolSinCertificadoAsync(
        Guid tenantId,
        ProcedureInstance instance,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var requisitos = await ResolveAsync(tenantId, instance.Actors, ct).ConfigureAwait(false);
        var cargados = new HashSet<string>(
            instance.Attachments.Select(a => a.Tipo), StringComparer.OrdinalIgnoreCase);

        return requisitos
            .Where(r => r.EsObligatorio && !cargados.Contains(r.Tipo))
            .Select(r => r.Rol)
            .FirstOrDefault();
    }

    /// <summary>
    /// Un requisito por actor persona jurídica con rol conocido. Los actores persona natural no
    /// aparecen: el certificado no les aplica.
    /// </summary>
    public async Task<IReadOnlyList<CamaraComercioRequirement>> ResolveAsync(
        Guid tenantId,
        IEnumerable<ProcedureInstanceActor> actors,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actors);

        var juridicos = actors
            .Where(a => string.Equals(a.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(a.DocumentNumber))
            .ToList();
        if (juridicos.Count == 0)
        {
            return [];
        }

        // Una sola lectura de escrituras para todo el trámite: el emparejamiento es por actor, pero la
        // consulta al directorio es la misma. Sin bytes (HU #12775): esto se recalcula en cada render
        // del paso y bajarse los PDF para contestar un booleano sería pagar una descarga por tecla.
        var escrituras = await _deedResolver
            .ResolvePresenceForActorsAsync(tenantId, juridicos, ct)
            .ConfigureAwait(false);

        var result = new List<CamaraComercioRequirement>(juridicos.Count);
        foreach (var actor in juridicos)
        {
            var rol = actor.ActorType?.Trim() ?? string.Empty;
            var tipo = CamaraComercioAttachmentTipo.For(rol);
            if (tipo is null)
            {
                // Rol que el modelo no conoce. Se omite en vez de caer a un código por defecto: darle
                // el buzón de otro rol dejaría que el certificado de una parte satisficiera el de otra.
                continue;
            }

            var exencion = await ResolverExencionAsync(tenantId, actor, rol, escrituras, ct)
                .ConfigureAwait(false);

            result.Add(new CamaraComercioRequirement(
                rol,
                tipo,
                EsObligatorio: exencion == CamaraComercioExencion.Ninguna,
                exencion));
        }

        return result;
    }

    private async Task<CamaraComercioExencion> ResolverExencionAsync(
        Guid tenantId,
        ProcedureInstanceActor actor,
        string rol,
        IReadOnlyList<ActorDeedPresence> escrituras,
        CancellationToken ct)
    {
        // (1) Firma del baúl de la PERSONA que representa al actor (HU #10930/#10932: el baúl se
        // llavea por documento, no por NIT). El puerto ya aplica activa + vigente + flag del tenant,
        // así que una firma revocada o fuera de vigencia llega aquí como null y no exime.
        //
        // Solo se consulta cuando el sujeto es DE VERDAD un representante legal capturado: sin RL,
        // IdentitySubjectResolver cae al propio actor, y preguntarle al baúl por el NIT de la empresa
        // devolvería la firma de cualquiera de sus representantes — incluido uno que no es el de este
        // trámite. Mismo criterio que ProcedureDeedResolver con el directorio.
        var sujeto = IdentitySubjectResolver.For(actor);
        if (sujeto.EsRepresentanteLegal
            && !string.IsNullOrWhiteSpace(sujeto.TipoDocumento)
            && !string.IsNullOrWhiteSpace(sujeto.NumeroDocumento))
        {
            var firma = await _vaultPolicy
                .ResolveAsync(tenantId, sujeto.TipoDocumento!.Trim(), sujeto.NumeroDocumento!.Trim(), ct)
                .ConfigureAwait(false);
            if (firma is not null)
            {
                return CamaraComercioExencion.FirmaPrecargada;
            }
        }

        // (2) Escritura vigente del actor, emparejada por su rol. Se compara por rol y no por NIT
        // porque en un traspaso entre dos sociedades del mismo grupo el NIT puede repetirse, y lo que
        // exime a una parte no tiene por qué eximir a la otra.
        var tieneEscritura = escrituras.Any(e =>
            string.Equals(e.Rol, rol, StringComparison.OrdinalIgnoreCase));

        return tieneEscritura
            ? CamaraComercioExencion.EscrituraVigente
            : CamaraComercioExencion.Ninguna;
    }
}
