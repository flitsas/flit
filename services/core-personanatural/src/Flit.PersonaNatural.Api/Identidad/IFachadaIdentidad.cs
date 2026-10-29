using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.PersonaNatural.Api.Identidad;

/// <summary>Datos de contacto que viajan a FLIT 2 para enviar el enlace. Solo en memoria: nunca se loguean.</summary>
public sealed record PersonaParaValidar(string TipoDocumento, string Documento, string Nombre, string Correo, string? Celular);

/// <summary>Respuesta de FLIT 2: id de la validación y su estado ya traducido (<see cref="EstadosIdentidad"/>).</summary>
public sealed record ValidacionDeFlit2(Guid ValidacionId, string Estado);

/// <summary>
/// Lo que el BFF le pide a la fachada gRPC de core-api para la identidad de las partes (HU #14044 y #14045). Separada de
/// <see cref="IFachadaTramites"/> (catálogo y cotización) para poder probarla con una fachada falsa y porque son casos de
/// uso distintos. Los errores salen como <see cref="FachadaException"/> con el código estable de FLIT 2.
/// </summary>
public interface IFachadaIdentidad
{
    /// <summary>Pide una validación NUEVA para esa parte de esa solicitud; idempotente por (solicitud, parte, documento).</summary>
    Task<ValidacionDeFlit2> SolicitarAsync(Guid solicitudId, string parte, PersonaParaValidar persona, CancellationToken ct);

    /// <summary>Reenvía el enlace de la misma validación, a un correo corregido si se indica. Devuelve el estado resultante.</summary>
    Task<string> ReenviarAsync(Guid validacionId, string? correoNuevo, CancellationToken ct);

    /// <summary>Estado actual de la validación en FLIT 2.</summary>
    Task<string> ObtenerEstadoAsync(Guid validacionId, CancellationToken ct);
}

internal sealed class FachadaIdentidadNoConfigurada : IFachadaIdentidad
{
    private static FachadaException NoDisponible() =>
        new(FachadaIdentidadGrpc.ServicioNoDisponible, "La validación de identidad no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);

    public Task<ValidacionDeFlit2> SolicitarAsync(Guid solicitudId, string parte, PersonaParaValidar persona, CancellationToken ct) => throw NoDisponible();

    public Task<string> ReenviarAsync(Guid validacionId, string? correoNuevo, CancellationToken ct) => throw NoDisponible();

    public Task<string> ObtenerEstadoAsync(Guid validacionId, CancellationToken ct) => throw NoDisponible();
}

/// <summary>
/// Cliente de los tres métodos de identidad de <c>PersonaNaturalService</c> con el token de servicio del SDK y la empresa de
/// Persona Natural en la metadata (misma que el catálogo). Traduce el estado gRPC a un código estable; nada del detalle
/// interno ni de los datos de la persona sale al ciudadano ni a los logs.
/// </summary>
internal sealed class FachadaIdentidadGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IFachadaIdentidad
{
    /// <summary>Código de «FLIT 2 no respondió»: el único que el BFF trata como transitorio (deja <c>pendiente_envio</c>).</summary>
    public const string ServicioNoDisponible = "servicio_no_disponible";

    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(15);

    private static Metadata Empresa() =>
        new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };

    public async Task<ValidacionDeFlit2> SolicitarAsync(Guid solicitudId, string parte, PersonaParaValidar persona, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(persona);
        var peticion = new SolicitarValidacionIdentidadRequest
        {
            SolicitudId = solicitudId.ToString(),
            Parte = parte,
            Persona = new PersonaIdentidad
            {
                TipoDocumento = persona.TipoDocumento,
                NumeroDocumento = persona.Documento,
                Nombre = persona.Nombre,
                Correo = persona.Correo,
                Celular = persona.Celular ?? string.Empty,
            },
        };
        var r = await Llamar(() => cliente.SolicitarValidacionIdentidadAsync(peticion, Empresa(), DateTime.UtcNow.Add(Plazo), ct).ResponseAsync).ConfigureAwait(false);
        return new ValidacionDeFlit2(Guid.Parse(r.ValidacionId), r.Estado);
    }

    public async Task<string> ReenviarAsync(Guid validacionId, string? correoNuevo, CancellationToken ct)
    {
        var peticion = new ReenviarValidacionIdentidadRequest { ValidacionId = validacionId.ToString(), Correo = correoNuevo ?? string.Empty };
        var r = await Llamar(() => cliente.ReenviarValidacionIdentidadAsync(peticion, Empresa(), DateTime.UtcNow.Add(Plazo), ct).ResponseAsync).ConfigureAwait(false);
        return r.Estado;
    }

    public async Task<string> ObtenerEstadoAsync(Guid validacionId, CancellationToken ct)
    {
        var peticion = new ObtenerEstadoValidacionIdentidadRequest { ValidacionId = validacionId.ToString() };
        var r = await Llamar(() => cliente.ObtenerEstadoValidacionIdentidadAsync(peticion, Empresa(), DateTime.UtcNow.Add(Plazo), ct).ResponseAsync).ConfigureAwait(false);
        return r.Estado;
    }

    private static async Task<T> Llamar<T>(Func<Task<T>> llamada)
    {
        try
        {
            return await llamada().ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            var razon = PlatformRpcErrors.Reason(ex);
            throw razon switch
            {
                "persona_juridica_no_soportada" => new FachadaException(razon, "El portal solo atiende personas naturales.", StatusCodes.Status422UnprocessableEntity),
                "contacto_invalido_correo" => new FachadaException(razon, "El correo no tiene un formato válido.", StatusCodes.Status422UnprocessableEntity),
                "validacion_no_encontrada" => new FachadaException(razon, "No encontramos esa validación.", StatusCodes.Status404NotFound),
                "identidad_aprobada" => new FachadaException(razon, "La identidad ya fue validada.", StatusCodes.Status409Conflict),
                "validacion_en_curso" => new FachadaException(razon, "Ya hay una validación en curso.", StatusCodes.Status409Conflict),
                _ when ex.StatusCode == StatusCode.InvalidArgument => new FachadaException("datos_invalidos", "Revise los datos de la persona.", StatusCodes.Status400BadRequest),
                _ => new FachadaException(ServicioNoDisponible, "La validación de identidad no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable),
            };
        }
    }
}
