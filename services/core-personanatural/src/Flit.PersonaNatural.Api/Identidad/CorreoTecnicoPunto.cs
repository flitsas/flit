using Microsoft.Extensions.Configuration;

namespace Flit.PersonaNatural.Api.Identidad;

/// <summary>
/// Correo técnico del radicante in-situ del punto: la validación se abre sin enviarle nada, pero el contrato pide un correo.
/// Dominio público válido y propio de FLIT (no entregable a terceros). Configurable con
/// <c>PersonaNatural:Punto:CorreoTecnico</c>; <c>{id}</c> se reemplaza por el id de la solicitud (sin guiones).
/// </summary>
public static class CorreoTecnicoPunto
{
    public const string Clave = "PersonaNatural:Punto:CorreoTecnico";
    public const string PorDefecto = "validaciones+punto-{id}@flitsas.com";

    public static string Para(IConfiguration? configuracion, Guid solicitudId)
    {
        var plantilla = configuracion?[Clave];
        if (string.IsNullOrWhiteSpace(plantilla))
            plantilla = PorDefecto;
        return plantilla.Replace("{id}", solicitudId.ToString("N"), StringComparison.Ordinal);
    }
}
