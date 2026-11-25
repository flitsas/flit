using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// El alta idempotente de la compañía en el API (<c>POST /api/v1/integracion/empresas</c>, guía del proveedor §3; plan §5.8,
/// M07b y D-ALTA-IDEM). Pide los datos a Identidad (<c>ObtenerDatosAltaProveedor</c>: razón social, NIT base de D-TAXID, correo
/// de D-EMAIL) y decide sin llamar si no se puede: NIT que no es <c>VALIDO</c> ⇒ <c>REQUIERE_REVISION_NIT</c> (DA-8), razón
/// social de más de <see cref="LargoMaximoRazonSocial"/> ⇒ <c>ERROR(DATOS)</c> <c>RAZON_SOCIAL_LARGA</c>, sin correo (sin
/// AdminCompany activo) ⇒ <c>ERROR(DATOS)</c>. Si se llama, cada respuesta:
/// <list type="bullet">
///   <item>200 o 201 con <c>api_key</c>: credenciales (las mismas en cada llamada con ese NIT). Con <c>company.active=false</c>
///   se guardan y queda <c>ERROR(AMBIENTE)</c> <c>EMPRESA_INACTIVA_EN_PROVEEDOR</c>; si no, <c>LISTO</c> con secreto o
///   <c>LISTO_SIN_WEBHOOK</c> sin él. <c>api_key_rotated</c> va en <see cref="ResultadoEvaluacion.ClaveRotada"/>.</item>
///   <item>No salió, timeout, 5xx, 429 o un 2xx sin credenciales: reintento automático con el mismo cuerpo (el proveedor no
///   duplica la empresa); ya no hay marca del envío ni <c>INCIERTO</c> desde el alta.</item>
///   <item>409: contrato del proveedor inesperado ⇒ <c>ERROR(AMBIENTE)</c> con alerta y detalle
///   <see cref="DetalleContratoInesperado"/>.</item>
///   <item>401, 403 y 404 ⇒ <c>ERROR(AMBIENTE)</c>; 400, 415 y otro 4xx ⇒ <c>ERROR(DATOS)</c>.</item>
/// </list>
/// Nunca registra la api_key, el secreto, el token ni el correo.
/// </summary>
/// <example>
/// <code>
/// var resultado = await alta.EjecutarAsync(empresa, ct);
/// </code>
/// </example>
public sealed partial class AltaEmpresaApi(
    ClienteApiLiquidacion api, IOptions<OpcionesApiLiquidacion> opciones, IClienteIdentidadAlta identidad, ILogger<AltaEmpresaApi> log)
{
    /// <summary>Máximo de <c>legal_name</c> que admite el API (1 a 200, plan §1.3 S14).</summary>
    public const int LargoMaximoRazonSocial = 200;

    /// <summary>Detalle técnico de un alta que no se intentó porque la compañía no tiene AdminCompany activo (D-EMAIL).</summary>
    public const string DetalleSinAdmin = "SIN_ADMIN";

    /// <summary>Detalle técnico de un 409 del alta: el contrato idempotente no lo emite (plan §5.8 punto 1).</summary>
    public const string DetalleContratoInesperado = "ALTA_409_CONTRATO_INESPERADO";

    public async Task<ResultadoEvaluacion> EjecutarAsync(Guid empresa, CancellationToken ct)
    {
        DatosAltaProveedor datos;
        try
        {
            datos = await identidad.ObtenerDatosAltaAsync(empresa, ct).ConfigureAwait(false);
        }
        catch (IdentidadNoDisponibleException ex)
        {
            IdentidadCaida(log, empresa, ex.Message);
            return Reintento();
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
            // Sin base ni token no se llama: es configuración del ambiente.
            _ = opciones.Value.BaseValidada();
            _ = opciones.Value.TokenValidado();
        }
        catch (InvalidOperationException ex)
        {
            SinConfigurar(log, empresa, ex.Message);
            return new ResultadoEvaluacion(EventoAprovisionamiento.AltaAmbienteInvalido, MotivosAprovisionamiento.AmbienteNoConfigurado);
        }

        var r = await api.CrearEmpresaAsync(new AltaEmpresaPeticion(datos.LegalName, datos.TaxIdBase, datos.ContactEmail), ct).ConfigureAwait(false);
        Respondio(log, empresa, r.Clase, r.Status, r.CodigoError);
        if (r.Exitoso)
        {
            var alta = r.Valor!;
            if (string.IsNullOrEmpty(alta.ApiKey))
                return Reintento();
            var rotada = alta.ApiKeyRotated == true;
            Leida(log, empresa, alta.Created, rotada, alta.Company?.Active, alta.Company?.HasWebhookSecret, alta.Company?.TaxIdDv is not null);
            var credenciales = new CredencialesEnClaro(alta.ApiKey, string.IsNullOrEmpty(alta.WebhookSecret) ? null : alta.WebhookSecret, alta.Company?.Id);
            if (alta.Company?.Active == false)
            {
                return new ResultadoEvaluacion(EventoAprovisionamiento.AltaAmbienteInvalido, MotivosAprovisionamiento.EmpresaInactivaEnProveedor, credenciales, OrigenCredencial.Alta)
                {
                    ClaveRotada = rotada,
                };
            }

            return credenciales.WebhookSecret is null
                ? new ResultadoEvaluacion(EventoAprovisionamiento.CredencialPropiaValidaSinSecreto, MotivosAprovisionamiento.SinWebhookSecret, credenciales, OrigenCredencial.Alta) { ClaveRotada = rotada }
                : new ResultadoEvaluacion(EventoAprovisionamiento.Alta201, null, credenciales, OrigenCredencial.Alta) { ClaveRotada = rotada };
        }

        return r.Clase switch
        {
            // Guía §3: si se corta por timeout o falla con 5xx, se reintenta con el mismo cuerpo; no duplica la empresa.
            ClaseEnvio.NoSalio or ClaseEnvio.Incierto => Reintento(),
            _ => r.Status switch
            {
                StatusCodes.Status409Conflict => new ResultadoEvaluacion(EventoAprovisionamiento.Alta409, MotivosAprovisionamiento.AmbienteNoConfigurado)
                {
                    Detalle = DetalleContratoInesperado,
                },
                StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound =>
                    new ResultadoEvaluacion(EventoAprovisionamiento.AltaAmbienteInvalido, MotivosAprovisionamiento.AmbienteNoConfigurado),
                StatusCodes.Status429TooManyRequests => Reintento(),
                >= 400 and < 500 => new ResultadoEvaluacion(EventoAprovisionamiento.AltaDatosInvalidos, MotivosAprovisionamiento.AltaDatosRechazados),
                // Un 2xx sin el cuerpo esperado o un 1xx/3xx: repetir el alta es seguro.
                _ => Reintento(),
            },
        };
    }

    private static ResultadoEvaluacion Reintento() => new(EventoAprovisionamiento.NoSalio, MotivosAprovisionamiento.ReintentoProgramado);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudieron pedir a Identidad los datos del alta de la empresa {Empresa} ({Motivo}); se reintenta.")]
    private static partial void IdentidadCaida(ILogger logger, Guid empresa, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La empresa {Empresa} no tiene un AdminCompany activo: no hay correo de contacto para el alta (D-EMAIL); no se llama al API.")]
    private static partial void SinAdmin(ILogger logger, Guid empresa);

    [LoggerMessage(Level = LogLevel.Error, Message = "El alta de la empresa {Empresa} no se intentó: {Motivo}")]
    private static partial void SinConfigurar(ILogger logger, Guid empresa, string motivo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alta en el API de la empresa {Empresa}: {Clase}, estado {Status}, código {Codigo}.")]
    private static partial void Respondio(ILogger logger, Guid empresa, ClaseEnvio clase, int? status, string? codigo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alta de la empresa {Empresa} con credenciales: created {Creada}, api_key_rotated {Rotada}, active {Activa}, has_webhook_secret {ConSecreto}, con tax_id_dv {ConDv}.")]
    private static partial void Leida(ILogger logger, Guid empresa, bool? creada, bool rotada, bool? activa, bool? conSecreto, bool conDv);
}
