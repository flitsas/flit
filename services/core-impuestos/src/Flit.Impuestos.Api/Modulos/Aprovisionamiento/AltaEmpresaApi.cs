using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Paso 3 de D-CRED-DIAG: el alta de la compañía en el API (<c>POST /api/v1/integracion/empresas</c>, plan §2.1). Pide los datos
/// a Identidad (<c>ObtenerDatosAltaProveedor</c>: razón social, NIT base de D-TAXID, correo de D-EMAIL) y decide sin llamar si no
/// se puede: NIT que no es <c>VALIDO</c> ⇒ <c>REQUIERE_REVISION_NIT</c> (DA-8), razón social de más de
/// <see cref="LargoMaximoRazonSocial"/> ⇒ <c>ERROR(DATOS)</c> <c>RAZON_SOCIAL_LARGA</c>, sin correo (sin AdminCompany activo) ⇒
/// <c>ERROR(DATOS)</c>. Si se llama, <c>antesDelEnvio</c> registra el envío ANTES del POST. Cada respuesta según
/// D-ALTA-REINTENTOS: 201 ⇒ credenciales (una sola vez) y <c>LISTO</c>; 409 ⇒ <c>REQUIERE_CREDENCIALES</c>; 400, 415 y otro 4xx
/// de datos ⇒ <c>ERROR(DATOS)</c>; 401, 403 y 404 ⇒ <c>ERROR(AMBIENTE)</c>; no salió (o 429: el API no lo atendió) ⇒ reintento
/// automático; timeout, 5xx o corte tras enviar ⇒ <c>INCIERTO</c>. Nunca registra la api_key, el secreto, el token ni el correo.
/// </summary>
/// <example>
/// <code>
/// var resultado = await alta.EjecutarAsync(empresa, async t => await MarcarEnvioAsync(empresa, t), ct);
/// </code>
/// </example>
public sealed partial class AltaEmpresaApi(
    ClienteApiLiquidacion api, IOptions<OpcionesApiLiquidacion> opciones, IClienteIdentidadAlta identidad, ILogger<AltaEmpresaApi> log)
{
    /// <summary>Máximo de <c>legal_name</c> que admite el API (1 a 200, plan §1.3 S14).</summary>
    public const int LargoMaximoRazonSocial = 200;

    /// <summary>Detalle técnico de un alta que no se intentó porque la compañía no tiene AdminCompany activo (D-EMAIL).</summary>
    public const string DetalleSinAdmin = "SIN_ADMIN";

    public async Task<ResultadoEvaluacion> EjecutarAsync(Guid empresa, Func<CancellationToken, Task> antesDelEnvio, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(antesDelEnvio);
        DatosAltaProveedor datos;
        try
        {
            datos = await identidad.ObtenerDatosAltaAsync(empresa, ct).ConfigureAwait(false);
        }
        catch (IdentidadNoDisponibleException ex)
        {
            IdentidadCaida(log, empresa, ex.Message);
            return new ResultadoEvaluacion(EventoAprovisionamiento.NoSalio, MotivosAprovisionamiento.ReintentoProgramado);
        }

        if (datos.EstadoNit != EstadoNitAlta.Valido)
        {
            return new ResultadoEvaluacion(EventoAprovisionamiento.NitNoValido, datos.EstadoNit switch
            {
                EstadoNitAlta.Ausente => MotivosAprovisionamiento.NitAusente,
                EstadoNitAlta.IgualACabeza => MotivosAprovisionamiento.NitIgualACabeza,
                _ => MotivosAprovisionamiento.NitAmbiguo,
            });
        }

        if (datos.LegalName.Length > LargoMaximoRazonSocial)
            return new ResultadoEvaluacion(EventoAprovisionamiento.AltaDatosInvalidos, MotivosAprovisionamiento.RazonSocialLarga);
        if (string.IsNullOrWhiteSpace(datos.ContactEmail))
        {
            SinAdmin(log, empresa);
            return new ResultadoEvaluacion(EventoAprovisionamiento.AltaDatosInvalidos, MotivosAprovisionamiento.AltaDatosRechazados) { Detalle = DetalleSinAdmin };
        }

        try
        {
            // Sin base ni token no se marca ni se llama: es configuración del ambiente.
            _ = opciones.Value.BaseValidada();
            _ = opciones.Value.TokenValidado();
        }
        catch (InvalidOperationException ex)
        {
            SinConfigurar(log, empresa, ex.Message);
            return new ResultadoEvaluacion(EventoAprovisionamiento.AltaAmbienteInvalido, MotivosAprovisionamiento.AmbienteNoConfigurado);
        }

        await antesDelEnvio(ct).ConfigureAwait(false);
        var r = await api.CrearEmpresaAsync(new AltaEmpresaPeticion(datos.LegalName, datos.TaxIdBase, datos.ContactEmail), ct).ConfigureAwait(false);
        Respondio(log, empresa, r.Clase, r.Status, r.CodigoError);
        if (r.Exitoso)
        {
            var alta = r.Valor!;
            if (string.IsNullOrEmpty(alta.ApiKey))
                return new ResultadoEvaluacion(EventoAprovisionamiento.Incierto, MotivosAprovisionamiento.AltaSinConfirmar);
            var credenciales = new CredencialesEnClaro(alta.ApiKey, string.IsNullOrEmpty(alta.WebhookSecret) ? null : alta.WebhookSecret, alta.Company?.Id);
            return credenciales.WebhookSecret is null
                ? new ResultadoEvaluacion(EventoAprovisionamiento.CredencialPropiaValidaSinSecreto, MotivosAprovisionamiento.SinWebhookSecret, credenciales, OrigenCredencial.Alta)
                : new ResultadoEvaluacion(EventoAprovisionamiento.Alta201, null, credenciales, OrigenCredencial.Alta);
        }

        return r.Clase switch
        {
            ClaseEnvio.NoSalio => new ResultadoEvaluacion(EventoAprovisionamiento.NoSalio, MotivosAprovisionamiento.ReintentoProgramado),
            ClaseEnvio.Incierto => new ResultadoEvaluacion(EventoAprovisionamiento.Incierto, MotivosAprovisionamiento.AltaSinConfirmar),
            _ => r.Status switch
            {
                StatusCodes.Status409Conflict => new ResultadoEvaluacion(EventoAprovisionamiento.Alta409, MotivosAprovisionamiento.EmpresaYaRegistrada),
                StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound =>
                    new ResultadoEvaluacion(EventoAprovisionamiento.AltaAmbienteInvalido, MotivosAprovisionamiento.AmbienteNoConfigurado),
                StatusCodes.Status429TooManyRequests => new ResultadoEvaluacion(EventoAprovisionamiento.NoSalio, MotivosAprovisionamiento.ReintentoProgramado),
                >= 400 and < 500 => new ResultadoEvaluacion(EventoAprovisionamiento.AltaDatosInvalidos, MotivosAprovisionamiento.AltaDatosRechazados),
                // Un 2xx sin el cuerpo esperado o un 1xx/3xx: el alta pudo hacerse.
                _ => new ResultadoEvaluacion(EventoAprovisionamiento.Incierto, MotivosAprovisionamiento.AltaSinConfirmar),
            },
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudieron pedir a Identidad los datos del alta de la empresa {Empresa} ({Motivo}); se reintenta.")]
    private static partial void IdentidadCaida(ILogger logger, Guid empresa, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La empresa {Empresa} no tiene un AdminCompany activo: no hay correo de contacto para el alta (D-EMAIL); no se llama al API.")]
    private static partial void SinAdmin(ILogger logger, Guid empresa);

    [LoggerMessage(Level = LogLevel.Error, Message = "El alta de la empresa {Empresa} no se intentó: {Motivo}")]
    private static partial void SinConfigurar(ILogger logger, Guid empresa, string motivo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alta en el API de la empresa {Empresa}: {Clase}, estado {Status}, código {Codigo}.")]
    private static partial void Respondio(ILogger logger, Guid empresa, ClaseEnvio clase, int? status, string? codigo);
}
