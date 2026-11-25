using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Credenciales;

/// <summary>Lo que el backoffice puede saber de las credenciales de una compañía: existencia, nunca valores (EP-CF3c).</summary>
public sealed record VistaCredenciales(bool TieneApiKey, bool TieneWebhookSecret, string? Origen, string? ApiKeyPrefijo, DateTimeOffset? ActualizadoEn);

/// <summary>
/// Servicio de las credenciales de una compañía (D-409, DA-2=b, plan §3.1 y §5.1). <see cref="CargarAsync"/> es la carga
/// manual del Super Admin: valida la forma, comprueba con la máquina de estados que el aprovisionamiento la admite
/// (<c>REQUIERE_CREDENCIALES</c> o <c>LISTO_SIN_WEBHOOK</c>; si no, 409 <c>ESTADO_NO_ADMITE_CARGA</c> sin llamar al API), valida la
/// api_key con <c>GET /fuentes</c> y, en una sola transacción con la fila del aprovisionamiento bloqueada (que vuelve a comprobar la
/// transición con el estado de ese momento), guarda la credencial cifrada, aplica la transición
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
    public const string CodigoRequerida = "CREDENCIAL_REQUERIDA";
    public const string CodigoEstadoNoAdmiteCarga = "ESTADO_NO_ADMITE_CARGA";

    /// <summary>Literal de <c>CREDENCIAL_REQUERIDA</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0).</summary>
    public const string LiteralRequerida = "Escribe la API key o el secreto del webhook.";

    /// <summary>Literal de <c>ESTADO_NO_ADMITE_CARGA</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0).</summary>
    public const string LiteralEstadoNoAdmiteCarga = "En el estado actual de la compañía no se pueden cargar credenciales.";
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

        var evento = secreto is null ? EventoAprovisionamiento.CargaManualSinSecreto : EventoAprovisionamiento.CargaManualConSecreto;
        await using var db = almacen.ContextoDe(empresa);
        // Antes de llamar al API: un estado que no admite carga es 409 ESTADO_NO_ADMITE_CARGA y no gasta la llamada.
        var previa = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false);
        _ = Transicion(previa, evento);

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

        // Con la fila bloqueada se relee el estado (pudo cambiar durante la validación, p. ej. una suspensión) y se vuelve a
        // comprobar la transición; aprovisionamiento y credencial se guardan juntos (IMP-4).
        var estado = await BloqueoAprovisionamiento.EnTransaccionAsync(db, empresa, async c =>
        {
            var aprovisionamiento = await db.Set<AprovisionamientoEmpresa>().FirstOrDefaultAsync(a => a.TenantId == empresa, c).ConfigureAwait(false);
            var nueva = Transicion(aprovisionamiento, evento).Nueva;
            var actual = await almacen.FilaAsync(db, empresa, c).ConfigureAwait(false);
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
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return aprovisionamiento.Estado;
        }, ct).ConfigureAwait(false);
        Cargadas(log, empresa, estado, secreto is not null);
    }

    /// <summary>
    /// La transición de la carga manual desde el estado de la fila. El dominio responde con <c>TransicionInvalida</c>; el código
    /// del contrato (409 <c>ESTADO_NO_ADMITE_CARGA</c>) se pone aquí, en el borde.
    /// </summary>
    private static ResultadoAprovisionamiento Transicion(AprovisionamientoEmpresa? fila, EventoAprovisionamiento evento)
    {
        var situacion = fila is null
            ? SituacionAprovisionamiento.SinFila
            : new SituacionAprovisionamiento(fila.Estado, fila.EstadoPrevio, fila.IntentosAutomaticos, fila.FirmaConfirmada, fila.ErrorTipo);
        try
        {
            return TransicionesAprovisionamiento.Aplicar(situacion, evento);
        }
        catch (TransicionInvalida)
        {
            throw ReglaException.Conflicto(CodigoEstadoNoAdmiteCarga, LiteralEstadoNoAdmiteCarga);
        }
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
    /// Texto visible (ASCII 0x21–0x7E) y, si se da, con el prefijo. Vacío o en blanco: 422 <see cref="CodigoRequerida"/>; mal
    /// formado: 422 <see cref="CodigoInvalida"/>. Los dos con el campo y el literal del contrato.
    /// </summary>
    private static string Validar(string? valor, string campo, string? prefijo = null)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ReglaException(CodigoRequerida, LiteralRequerida) { Campo = campo };
        if (valor.Any(c => c is < '!' or > '~')
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
