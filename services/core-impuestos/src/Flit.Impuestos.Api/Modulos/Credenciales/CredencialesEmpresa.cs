using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Credenciales;

/// <summary>Lo que el backoffice puede saber de las credenciales de una compañía: existencia, nunca valores (EP-CF3c).</summary>
public sealed record VistaCredenciales(bool TieneApiKey, bool TieneWebhookSecret, string? Origen, string? ApiKeyPrefijo, DateTimeOffset? ActualizadoEn);

/// <summary>
/// Servicio de las credenciales de una compañía (D-409, DA-2=b, plan §3.1 y §5.1). <see cref="CargarAsync"/> es la carga
/// manual del Super Admin: valida la forma, comprueba con la máquina de estados que el aprovisionamiento la admite
/// (<c>REQUIERE_CREDENCIALES</c> o <c>LISTO_SIN_WEBHOOK</c>; si no, 409 <c>TRANSICION_INVALIDA</c> sin llamar al API), valida la
/// api_key con <c>GET /fuentes</c> y, en una sola transacción, guarda la credencial cifrada, aplica la transición
/// (<c>LISTO</c> con secreto, <c>LISTO_SIN_WEBHOOK</c> sin él), sube la versión y escribe el historial. Un secreto nuevo apaga el
/// modo respaldo de firma (F2b-CF3). El reporte a la plataforma lo hace el reportador de M07 por la versión. Nunca registra ni
/// devuelve valores.
/// </summary>
/// <example>
/// <code>
/// await credenciales.CargarAsync(empresa, apiKey, webhookSecret, superAdminId, ct);
/// var vista = await credenciales.VerAsync(empresa, ct); // { TieneApiKey, TieneWebhookSecret, Origen, ApiKeyPrefijo, ActualizadoEn }
/// </code>
/// </example>
public sealed partial class CredencialesEmpresa(AlmacenCredenciales almacen, ClienteApiLiquidacion api, TimeProvider reloj, ILogger<CredencialesEmpresa> log)
{
    public const string CodigoInvalida = "CREDENCIAL_INVALIDA";
    public const string CodigoApiNoDisponible = "PROVEEDOR_NO_DISPONIBLE";

    /// <summary>Literal de <c>CREDENCIAL_INVALIDA</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0).</summary>
    public const string LiteralInvalida = "El proveedor rechazó la API key: revisa el valor e intenta de nuevo.";

    /// <summary>Literal de <c>PROVEEDOR_NO_DISPONIBLE</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0).</summary>
    public const string LiteralApiNoDisponible = "El proveedor no responde. Intenta más tarde.";
    public const string MotivoCargaManual = "CARGA_MANUAL";
    public const string ActorSuperAdmin = "SUPER_ADMIN";
    public const string PrefijoWebhookSecret = "whsec_";

    public async Task CargarAsync(Guid empresa, string? apiKey, string? webhookSecret, Guid actor, CancellationToken ct)
    {
        var llave = Validar(apiKey, "apiKey");
        var secreto = string.IsNullOrEmpty(webhookSecret) ? null : Validar(webhookSecret, "webhookSecret", PrefijoWebhookSecret);

        await using var db = almacen.ContextoDe(empresa);
        var aprovisionamiento = await db.Set<AprovisionamientoEmpresa>().FirstOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false);
        var situacion = aprovisionamiento is null
            ? SituacionAprovisionamiento.SinFila
            : new SituacionAprovisionamiento(
                aprovisionamiento.Estado, aprovisionamiento.EstadoPrevio, aprovisionamiento.IntentosAutomaticos, aprovisionamiento.FirmaConfirmada, aprovisionamiento.ErrorTipo);
        var evento = secreto is null ? EventoAprovisionamiento.CargaManualSinSecreto : EventoAprovisionamiento.CargaManualConSecreto;
        // Antes de llamar al API: un estado que no admite carga es 409 (TransicionInvalida) y no gasta la llamada.
        var nueva = TransicionesAprovisionamiento.Aplicar(situacion, evento).Nueva;

        var validacion = await api.ObtenerFuentesAsync(llave, ct).ConfigureAwait(false);
        if (!validacion.Exitoso)
        {
            if (validacion is { Clase: ClaseEnvio.Respuesta, Status: StatusCodes.Status401Unauthorized })
            {
                Rechazada(log, empresa);
                throw new ReglaException(CodigoInvalida, LiteralInvalida) { Campo = "apiKey" };
            }

            NoValidada(log, empresa, validacion.Clase, validacion.Status, validacion.CodigoError);
            throw new ReglaException(CodigoApiNoDisponible, LiteralApiNoDisponible, StatusCodes.Status503ServiceUnavailable);
        }

        var actual = await almacen.FilaAsync(db, empresa, ct).ConfigureAwait(false);
        almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(llave, secreto), OrigenCredencial.CargaManual, actor);

        var ahora = reloj.GetUtcNow();
        var desde = aprovisionamiento!.Estado;
        aprovisionamiento.Estado = nueva.Estado!.Value;
        aprovisionamiento.EstadoPrevio = nueva.EstadoPrevio;
        aprovisionamiento.ErrorTipo = nueva.TipoError;
        aprovisionamiento.FirmaConfirmada = nueva.FirmaConfirmada;
        aprovisionamiento.MotivoCodigo = null;
        aprovisionamiento.MotivoDetalle = null;
        if (secreto is not null)
        {
            aprovisionamiento.ModoRespaldoFirma = false;
            aprovisionamiento.FirmasInvalidasConsecutivas = 0;
        }

        aprovisionamiento.Version++;
        aprovisionamiento.ActualizadoEn = ahora;
        db.Add(new HistorialAprovisionamiento
        {
            TenantId = empresa,
            Desde = desde,
            Hacia = aprovisionamiento.Estado,
            MotivoCodigo = MotivoCargaManual,
            ActorId = actor,
            ActorTipo = ActorSuperAdmin,
            En = ahora,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Cargadas(log, empresa, aprovisionamiento.Estado, secreto is not null);
    }

    public async Task<VistaCredenciales> VerAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var fila = await db.Set<CredencialEmpresa>().AsNoTracking()
            .Where(c => c.TenantId == empresa)
            .Select(c => new { c.ApiKeyPrefijo, TieneSecreto = c.WebhookSecretCifrado != null, c.Origen, c.ActualizadoEn })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return fila is null
            ? new VistaCredenciales(false, false, null, null, null)
            : new VistaCredenciales(true, fila.TieneSecreto, CodigosDeEnum.Codigo(fila.Origen), fila.ApiKeyPrefijo, fila.ActualizadoEn);
    }

    /// <summary>
    /// Texto visible (ASCII 0x21–0x7E) y, si se da, con el prefijo; si no, 422 <see cref="CodigoInvalida"/> con el campo y el
    /// literal del contrato (el campo indica cuál de los dos valores revisar).
    /// </summary>
    private static string Validar(string? valor, string campo, string? prefijo = null)
    {
        if (string.IsNullOrEmpty(valor)
            || valor.Any(c => c is < '!' or > '~')
            || (prefijo is not null && (!valor.StartsWith(prefijo, StringComparison.Ordinal) || valor.Length == prefijo.Length)))
            throw new ReglaException(CodigoInvalida, LiteralInvalida) { Campo = campo };
        return valor;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Credenciales del API cargadas a mano para la empresa {Empresa}: queda en {Estado} (con secreto de webhook: {ConSecreto}).")]
    private static partial void Cargadas(ILogger logger, Guid empresa, EstadoAprovisionamiento estado, bool conSecreto);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El API de liquidación rechazó (401) la api_key cargada para la empresa {Empresa}; no se guardó.")]
    private static partial void Rechazada(ILogger logger, Guid empresa);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo validar la credencial de la empresa {Empresa} con el API ({Clase}, estado {Status}, código {Codigo}); no se guardó.")]
    private static partial void NoValidada(ILogger logger, Guid empresa, ClaseEnvio clase, int? status, string? codigo);
}
