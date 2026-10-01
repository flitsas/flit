using Flit.Tramites.Application.BulkTramites.Processing;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Resultado de completar los representantes legales: actores listos y avisos no fatales.</summary>
public sealed record RepresentanteLegalCompletado(IReadOnlyList<ActorInput> Actores, IReadOnlyList<string> Avisos);

/// <summary>
/// Bug #13194 (punto 4) — completa el representante legal de los actores PERSONA JURÍDICA que llegan por
/// integración (ICT) sin su documento o su correo, usando el directorio de representantes de la compañía
/// del MISMO tenant (llave tenant + NIT). Es la misma precarga por NIT del asistente
/// (<c>GET /tramites/legal-representatives/lookup</c>) y de la carga masiva, vía
/// <see cref="IBulkTramitesLegalRepresentativeDirectory"/>.
///
/// <para>Sin esto, un comprador PJ del ICT quedaba sin sujeto de identidad usable: con el RL sin documento
/// <see cref="EnsureIdentityHandler"/> no podía ver su firma del baúl, y con el RL sin correo el inicio de
/// la validación respondía <c>datos_incompletos</c> sin que nadie lo supiera.</para>
///
/// <para>Reglas: lo que trae el actor manda y el directorio solo rellena lo que falta. Si el actor declara
/// el documento del representante, se busca a ESA persona en el directorio (empate por dígitos, igual que
/// el asistente) y, si aparece, se adoptan su tipo y número canónicos para que la búsqueda del baúl
/// (tenant + tipo + documento) encuentre su firma. Nunca se cambia la persona declarada por otra. Sin
/// representante declarado se toma el principal del directorio (el primero, mismo orden que el selector).
/// El mecanismo de firma se deja sin elegir: aplica la precedencia del baúl (HU #11031).</para>
///
/// <para>Avisos (códigos sin PII, el llamador los prefija): <c>rl_no_registrado:&lt;rol&gt;</c> si el
/// tenant no tiene a ese representante para el NIT; <c>rl_sin_correo:&lt;rol&gt;</c> si tras completar el
/// representante sigue sin correo (no se le podrá enviar la validación).</para>
/// </summary>
public sealed class RepresentanteLegalDesdeDirectorio(IBulkTramitesLegalRepresentativeDirectory directory)
{
    public const string AvisoNoRegistrado = "rl_no_registrado";
    public const string AvisoSinCorreo = "rl_sin_correo";

    public async Task<RepresentanteLegalCompletado> CompletarAsync(
        Guid tenantId, IReadOnlyList<ActorInput> actores, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actores);

        var resultado = new List<ActorInput>(actores.Count);
        var avisos = new List<string>();
        foreach (var actor in actores)
        {
            if (!EsJuridica(actor))
            {
                resultado.Add(actor);
                continue;
            }

            var completado = await CompletarActorAsync(tenantId, actor, avisos, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(completado.RepresentanteLegal?.Email))
                avisos.Add($"{AvisoSinCorreo}:{actor.Rol}");
            resultado.Add(completado);
        }

        return new RepresentanteLegalCompletado(resultado, avisos);
    }

    private async Task<ActorInput> CompletarActorAsync(
        Guid tenantId, ActorInput actor, List<string> avisos, CancellationToken ct)
    {
        var rl = actor.RepresentanteLegal;
        var faltaAlgo = string.IsNullOrWhiteSpace(rl?.NumeroDocumento)
            || string.IsNullOrWhiteSpace(rl?.TipoDocumento)
            || string.IsNullOrWhiteSpace(rl?.Email)
            || string.IsNullOrWhiteSpace(actor.Email);
        if (!faltaAlgo)
            return actor;

        var entrada = await BuscarPorNitAsync(tenantId, actor.NumeroDocumento, ct).ConfigureAwait(false);
        if (entrada is null || entrada.Representantes.Count == 0)
        {
            avisos.Add($"{AvisoNoRegistrado}:{actor.Rol}");
            return actor;
        }

        var declarado = rl?.NumeroDocumento;
        var representante = string.IsNullOrWhiteSpace(declarado)
            ? entrada.Representantes[0]
            : entrada.Representantes.FirstOrDefault(r => MismoDocumento(r.NumeroDocumento, declarado));
        if (representante is null)
        {
            // Declaró a alguien que el directorio no tiene para ese NIT: no se sustituye por otra persona.
            avisos.Add($"{AvisoNoRegistrado}:{actor.Rol}");
            return actor with { Email = Primero(actor.Email, entrada.Email) ?? string.Empty };
        }

        return actor with
        {
            Email = Primero(actor.Email, entrada.Email) ?? string.Empty,
            Telefono = Primero(actor.Telefono, entrada.Telefono),
            Ciudad = Primero(actor.Ciudad, entrada.Ciudad),
            Direccion = Primero(actor.Direccion, entrada.Direccion),
            RepresentanteLegal = new ActorRepresentanteLegal(
                representante.TipoDocumento,
                representante.NumeroDocumento,
                Primero(rl?.NombreCompleto, representante.NombreCompleto),
                Primero(rl?.Email, representante.Email),
                Primero(rl?.Telefono, representante.Telefono),
                MecanismoFirma: rl?.MecanismoFirma),
        };
    }

    /// <summary>
    /// Busca la compañía por el NIT tal como llegó y, si trae dígito de verificación o separadores
    /// («900.123.456-7»), por su forma base (solo dígitos antes del guion), que es como la guarda el directorio.
    /// </summary>
    private async Task<BulkTramitesCompanyDirectoryEntry?> BuscarPorNitAsync(
        Guid tenantId, string? nit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nit))
            return null;

        var crudo = nit.Trim();
        var entrada = await directory.FindByNitAsync(tenantId, crudo, ct).ConfigureAwait(false);
        if (entrada is not null)
            return entrada;

        var guion = crudo.IndexOf('-', StringComparison.Ordinal);
        var basePart = guion > 0 ? crudo[..guion] : crudo;
        var soloDigitos = new string(basePart.Where(char.IsDigit).ToArray());
        return soloDigitos.Length > 0 && !string.Equals(soloDigitos, crudo, StringComparison.Ordinal)
            ? await directory.FindByNitAsync(tenantId, soloDigitos, ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Persona jurídica por documento (NIT ⇒ jurídica, <see cref="ActorPersonTypes.ResolveForDocument"/>) o
    /// por lo declarado. Tolera variantes del tipo con puntos o espacios («N.I.T.», « nit »).
    /// </summary>
    public static bool EsJuridica(ActorInput actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tipo = new string((actor.TipoDocumento ?? string.Empty).Where(char.IsLetter).ToArray());
        return ActorPersonTypes.IsJuridical(ActorPersonTypes.ResolveForDocument(tipo, actor.PersonType));
    }

    /// <summary>Empata cédulas aunque vengan con puntos o ceros a la izquierda, como el asistente.</summary>
    private static bool MismoDocumento(string a, string b) => SoloDigitos(a) == SoloDigitos(b);

    private static string SoloDigitos(string valor) =>
        new string(valor.Where(char.IsDigit).ToArray()).TrimStart('0');

    private static string? Primero(string? propio, string? directorio) =>
        string.IsNullOrWhiteSpace(propio)
            ? (string.IsNullOrWhiteSpace(directorio) ? null : directorio.Trim())
            : propio;
}
