using System.Text.Json;
using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Identidad;

/// <summary>
/// Estados de la validación de identidad de una parte, tal como los guarda <c>persona_solicitante.validacion_estado</c>
/// (diseño §5). <c>aprobada</c>, <c>rechazada</c> y <c>vencida</c> vienen de FLIT 2; <c>pendiente_envio</c> es del BFF (FLIT 2
/// no respondió y el servicio reintenta por su cuenta). Sin vigencia en días: una validación vale solo para su solicitud.
/// </summary>
public static class EstadosIdentidad
{
    public const string SinSolicitar = "sin_solicitar";
    public const string PendienteEnvio = "pendiente_envio";
    public const string Pendiente = "pendiente";
    public const string Aprobada = "aprobada";
    public const string Rechazada = "rechazada";
    public const string Vencida = "vencida";

    /// <summary>Hay una validación abierta de esta solicitud: no se pide otra, se reenvía (o se espera).</summary>
    public static bool EnCurso(string estado) => estado is Pendiente or PendienteEnvio;
}

/// <summary>Un rol del tipo de trámite que exige validar la identidad de quien lo ocupa (<c>biometria</c> en la definición).</summary>
public sealed record RolConIdentidad(string Rol, int Min);

/// <summary>Lo que la definición congelada del trámite dice sobre identidad: los roles que existen y cuáles exigen validar.</summary>
public sealed record ReglasDeLaSolicitud(IReadOnlySet<string> RolesDefinidos, IReadOnlyList<RolConIdentidad> RolesConIdentidad)
{
    public bool Existe(string rol) => RolesDefinidos.Contains(rol);

    public RolConIdentidad? Exige(string rol) => RolesConIdentidad.FirstOrDefault(r => string.Equals(r.Rol, rol, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Qué roles de una solicitud exigen identidad. Salen de la definición congelada de ESA solicitud (el tipo es un dato, no
/// una lista fija en el código). Una interfaz para sustituirla en pruebas y para alinear la forma del JSON cuando el
/// integrador junte la HU D1 (que es quien escribe <c>solicitud_tramite.definicion_snapshot</c>).
/// </summary>
public interface IReglasDeIdentidad
{
    /// <summary>Las reglas de la solicitud, o <c>null</c> si la solicitud no existe.</summary>
    Task<ReglasDeLaSolicitud?> LeerAsync(Guid solicitudId, CancellationToken ct);
}

/// <summary>
/// Lee las reglas del <c>definicion_snapshot</c> (<c>DefinicionTipoTramite</c> del diseño §6.2): <c>actores[]</c> con
/// <c>rol</c>, <c>min</c> y <c>biometria</c>. Tolerante con la forma: los nombres se comparan sin mayúsculas ni guiones bajos
/// (<c>personaNaturalPermitida</c> = <c>persona_natural_permitida</c>) y un <c>min</c> ausente en un rol con identidad vale 1
/// (lo conservador: el rol pide a alguien). Un snapshot sin <c>actores</c> no tiene ningún rol: ningún endpoint de identidad
/// aplica.
/// </summary>
public sealed class ReglasDeIdentidadDesdeSnapshot(PersonaNaturalDb db) : IReglasDeIdentidad
{
    public async Task<ReglasDeLaSolicitud?> LeerAsync(Guid solicitudId, CancellationToken ct)
    {
        var snapshot = await db.Set<SolicitudTramite>().AsNoTracking().Where(s => s.Id == solicitudId)
            .Select(s => s.DefinicionSnapshot).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return snapshot is null ? null : Interpretar(snapshot);
    }

    /// <summary>Separado de la lectura para probar la forma del JSON sin base.</summary>
    internal static ReglasDeLaSolicitud Interpretar(string snapshot)
    {
        var definidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conIdentidad = new List<RolConIdentidad>();
        try
        {
            using var doc = JsonDocument.Parse(snapshot);
            if (Propiedad(doc.RootElement, "actores") is { ValueKind: JsonValueKind.Array } actores)
            {
                foreach (var actor in actores.EnumerateArray())
                {
                    if (Propiedad(actor, "rol") is not { ValueKind: JsonValueKind.String } rolJson || string.IsNullOrWhiteSpace(rolJson.GetString()))
                        continue;
                    var rol = rolJson.GetString()!.Trim();
                    definidos.Add(rol);
                    if (Propiedad(actor, "biometria") is { ValueKind: JsonValueKind.True })
                    {
                        var min = Propiedad(actor, "min") is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var valor) ? valor : 1;
                        conIdentidad.Add(new RolConIdentidad(rol, Math.Max(min, 0)));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Un snapshot ilegible no inventa reglas: queda sin roles.
        }

        return new ReglasDeLaSolicitud(definidos, conIdentidad);
    }

    private static JsonElement? Propiedad(JsonElement elemento, string nombre)
    {
        if (elemento.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var propiedad in elemento.EnumerateObject())
        {
            if (string.Equals(propiedad.Name.Replace("_", string.Empty, StringComparison.Ordinal), nombre, StringComparison.OrdinalIgnoreCase))
                return propiedad.Value;
        }

        return null;
    }
}
