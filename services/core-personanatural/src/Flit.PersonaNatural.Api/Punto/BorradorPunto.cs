using System.Text.Json;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>Lo que el gate a Pago necesita del borrador guardado (nunca datos personales).</summary>
public sealed record DatosBorradorPunto(
    Guid SolicitudId,
    Guid OrganismoId,
    string TipoCodigo,
    bool EsCanalPunto,
    DatosSolicitud Datos,
    IReadOnlyDictionary<string, string> Campos,
    IReadOnlySet<string> DocCodesCargados,
    bool HayPersonaJuridica);

/// <summary>
/// Acceso del canal del punto al borrador del portal. Se abstrae para que el gate se pruebe sin Postgres; la implementación
/// real valida el token de sesión del borrador (la misma sesión que usan <c>/solicitudes/{id}/adjuntos</c> y la identidad).
/// </summary>
public interface IBorradorPunto
{
    /// <summary>Id de la solicitud si el token es una sesión de borrador vigente; <c>null</c> si falta o no sirve.</summary>
    Task<Guid?> ResolverSesionAsync(string? token, CancellationToken ct);

    Task<DatosBorradorPunto?> LeerAsync(Guid solicitudId, CancellationToken ct);
}

public sealed class BorradorPuntoDb(PersonaNaturalDb db, SesionService sesiones) : IBorradorPunto
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Guid?> ResolverSesionAsync(string? token, CancellationToken ct)
    {
        var validada = await sesiones.ValidarAsync(token, ct).ConfigureAwait(false);
        return validada.Estado == EstadoSesion.Valida ? validada.SolicitudId : null;
    }

    public async Task<DatosBorradorPunto?> LeerAsync(Guid solicitudId, CancellationToken ct)
    {
        var s = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(x => x.Id == solicitudId)
            .Select(x => new { x.Id, x.OrganismoId, x.TipoTramiteCodigo, x.Datos, x.FieldValues })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (s is null)
            return null;

        var tipos = await db.Set<Adjunto>().AsNoTracking()
            .Where(a => a.SolicitudId == solicitudId).Select(a => a.Tipo).Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
        var hayJuridica = await db.Set<PersonaSolicitante>().AsNoTracking()
            .AnyAsync(p => p.SolicitudId == solicitudId && p.TipoDocumento == "NIT", ct).ConfigureAwait(false);

        return new DatosBorradorPunto(
            s.Id,
            s.OrganismoId,
            s.TipoTramiteCodigo,
            EsCanalPunto(s.Datos),
            DatosSolicitud.Leer(s.Datos),
            LeerCampos(s.FieldValues),
            tipos.ToHashSet(StringComparer.Ordinal),
            hayJuridica);
    }

    private static bool EsCanalPunto(string? datosJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(datosJson) ? "{}" : datosJson);
            return doc.RootElement.TryGetProperty("canal", out var canal)
                && string.Equals(canal.GetString(), ActoresPuntoService.CanalOrigen, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> LeerCampos(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(string.IsNullOrWhiteSpace(json) ? "{}" : json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
