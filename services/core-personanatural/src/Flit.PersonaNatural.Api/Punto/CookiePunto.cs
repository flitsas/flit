using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Cookie <c>pn_punto</c> (HU #14216): HttpOnly, SameSite=Lax, con el organismo fijo. El valor está protegido: alterarlo
/// no cambia el organismo, deja la cookie inválida.
/// </summary>
public sealed class CookiePunto(IDataProtectionProvider proteccion)
{
    public const string Nombre = "pn_punto";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector = proteccion.CreateProtector("Flit.PersonaNatural.Punto.v1");

    public void Fijar(HttpContext http, OrganismoPunto organismo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(organismo);
        var carga = JsonSerializer.Serialize(organismo, Json);
        http.Response.Cookies.Append(Nombre, _protector.Protect(carga), Opciones(http, persistir: true));
    }

    /// <summary>Borra la cookie. Un fallo de entrada no debe dejar una sesión previa.</summary>
    public void Borrar(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Cookies.Delete(Nombre, Opciones(http, persistir: false));
    }

    public OrganismoPunto? Leer(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!http.Request.Cookies.TryGetValue(Nombre, out var valor) || string.IsNullOrWhiteSpace(valor))
            return null;

        try
        {
            var organismo = JsonSerializer.Deserialize<OrganismoPunto>(_protector.Unprotect(valor), Json);
            return organismo is { OrganismoId: var id } && id != Guid.Empty && !string.IsNullOrWhiteSpace(organismo.Codigo)
                ? organismo
                : null;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private static CookieOptions Opciones(HttpContext http, bool persistir) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        Path = "/",
        MaxAge = persistir ? TimeSpan.FromHours(12) : null,
        IsEssential = true,
    };
}
