using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Respaldo del alta (M07b, D-ALTA-IDEM, plan §5.8): el <see cref="Aprovisionador"/> lo usa solo cuando el alta idempotente no
/// dejó credenciales (sin reintentos automáticos pendientes y sin un 409). Mira las credenciales que ya hay:
/// <list type="number">
///   <item>Si la compañía ya tiene credencial propia en core-impuestos, se valida con <c>GET /fuentes</c>: 200 con secreto ⇒
///   <c>LISTO</c>, sin secreto ⇒ <c>LISTO_SIN_WEBHOOK</c>; 401 ⇒ <c>REQUIERE_CREDENCIALES</c> (el alta ya se intentó).</item>
///   <item>Si no, se pide a Identidad la llave de Diagnóstico (<c>ObtenerCredencialImpuestoParaCopia</c>, DA-3 residual) y se
///   valida igual: 200 ⇒ se copia (origen <c>COPIA_DIAGNOSTICO</c>, sin secreto) y queda <c>LISTO_SIN_WEBHOOK</c>, sin rotación;
///   401 ⇒ <c>REQUIERE_CREDENCIALES</c> y no se guarda.</item>
/// </list>
/// Devuelve null si no hay ninguna de las dos (queda el resultado del alta). <c>GET /fuentes</c> es idempotente: si no salió,
/// se cortó o respondió 5xx se reintenta solo (<see cref="EventoAprovisionamiento.NoSalio"/>); otro 4xx o el API sin configurar
/// es error del ambiente. Si Identidad no responde también se reintenta. Nunca registra las llaves.
/// </summary>
/// <example>
/// <code>
/// var resultado = await evaluador.EvaluarAsync(empresa, ct) ?? resultadoDelAlta;
/// </code>
/// </example>
public sealed partial class EvaluadorCredencialesExistentes(
    AlmacenCredenciales almacen, ClienteApiLiquidacion api, IClienteIdentidadAlta identidad, ILogger<EvaluadorCredencialesExistentes> log)
{
    public async Task<ResultadoEvaluacion?> EvaluarAsync(Guid empresa, CancellationToken ct)
    {
        var propia = await almacen.LeerAsync(empresa, ct).ConfigureAwait(false);
        if (propia is not null)
        {
            return await ValidarAsync(empresa, propia.ApiKey, ct).ConfigureAwait(false) switch
            {
                Validacion.Valida => propia.WebhookSecret is null
                    ? new ResultadoEvaluacion(EventoAprovisionamiento.CredencialPropiaValidaSinSecreto, MotivosAprovisionamiento.SinWebhookSecret)
                    : new ResultadoEvaluacion(EventoAprovisionamiento.CredencialPropiaValidaConSecreto, null),
                Validacion.Rechazada => new ResultadoEvaluacion(EventoAprovisionamiento.CredencialRechazada, MotivosAprovisionamiento.CredencialRechazada),
                Validacion.Ambiente => new ResultadoEvaluacion(EventoAprovisionamiento.AltaAmbienteInvalido, MotivosAprovisionamiento.AmbienteNoConfigurado),
                _ => new ResultadoEvaluacion(EventoAprovisionamiento.NoSalio, MotivosAprovisionamiento.ReintentoProgramado),
            };
        }

        LlaveDiagnostico llave;
        try
        {
            llave = await identidad.ObtenerLlaveDiagnosticoAsync(empresa, ct).ConfigureAwait(false);
        }
        catch (IdentidadNoDisponibleException ex)
        {
            IdentidadCaida(log, empresa, ex.Message);
            return new ResultadoEvaluacion(EventoAprovisionamiento.NoSalio, MotivosAprovisionamiento.ReintentoProgramado);
        }

        if (!llave.Existe || string.IsNullOrEmpty(llave.ApiKey))
            return null;

        return await ValidarAsync(empresa, llave.ApiKey, ct).ConfigureAwait(false) switch
        {
            Validacion.Valida => new ResultadoEvaluacion(
                EventoAprovisionamiento.CopiaDiagnosticoValida, MotivosAprovisionamiento.SinWebhookSecret,
                new CredencialesEnClaro(llave.ApiKey, null), OrigenCredencial.CopiaDiagnostico),
            Validacion.Rechazada => new ResultadoEvaluacion(EventoAprovisionamiento.CredencialRechazada, MotivosAprovisionamiento.CredencialRechazada),
            Validacion.Ambiente => new ResultadoEvaluacion(EventoAprovisionamiento.AltaAmbienteInvalido, MotivosAprovisionamiento.AmbienteNoConfigurado),
            _ => new ResultadoEvaluacion(EventoAprovisionamiento.NoSalio, MotivosAprovisionamiento.ReintentoProgramado),
        };
    }

    private async Task<Validacion> ValidarAsync(Guid empresa, string apiKey, CancellationToken ct)
    {
        ResultadoEnvio<FuentesRespuesta> r;
        try
        {
            r = await api.ObtenerFuentesAsync(apiKey, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // El cliente no llama si falta su configuración (base del API); el mensaje nombra la variable, no un valor.
            SinConfigurar(log, empresa, ex.Message);
            return Validacion.Ambiente;
        }

        if (r.Exitoso)
            return Validacion.Valida;
        if (r is { Clase: ClaseEnvio.Respuesta, Status: StatusCodes.Status401Unauthorized })
            return Validacion.Rechazada;
        NoValidada(log, empresa, r.Clase, r.Status, r.CodigoError);
        return r.Clase == ClaseEnvio.Respuesta && r.Status is >= 400 and < 500 && r.Status != StatusCodes.Status429TooManyRequests
            ? Validacion.Ambiente
            : Validacion.Reintentar;
    }

    private enum Validacion
    {
        Valida,
        Rechazada,
        Ambiente,
        Reintentar,
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo pedir a Identidad la llave de Diagnóstico de la empresa {Empresa} ({Motivo}); se reintenta.")]
    private static partial void IdentidadCaida(ILogger logger, Guid empresa, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo validar con GET /fuentes la credencial de la empresa {Empresa} ({Clase}, estado {Status}, código {Codigo}).")]
    private static partial void NoValidada(ILogger logger, Guid empresa, ClaseEnvio clase, int? status, string? codigo);

    [LoggerMessage(Level = LogLevel.Error, Message = "El cliente del API de liquidación no está configurado; la empresa {Empresa} queda en error de ambiente: {Motivo}")]
    private static partial void SinConfigurar(ILogger logger, Guid empresa, string motivo);
}
