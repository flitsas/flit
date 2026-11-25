using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;

namespace Flit.Tramites.Application.Identity.Rounds;

/// <summary>Resultados de una alerta de rondas en <c>admin.identity_rounds_alert_audit.resultado</c>.</summary>
public static class IdentityRoundsAlertResults
{
    public const string Pendiente = "pendiente";
    public const string Enviado = "enviado";
    public const string Parcial = "parcial";
    public const string Fallido = "fallido";
    public const string SinDestinatarios = "sin_destinatarios";
}

/// <summary>Fila de auditoría de una alerta. <c>UNIQUE (validation_id, ronda)</c> en base: una alerta por ronda como máximo.</summary>
public sealed record IdentityRoundsAlertRecord(
    Guid Id,
    Guid ValidationId,
    Guid? ProcedureInstanceId,
    int Ronda,
    string Disparador,
    IReadOnlyList<string> Destinatarios,
    DateTimeOffset Fecha,
    string Resultado);

/// <summary>
/// Datos COMPLETOS de la alerta (sin enmascarar: es para que operación sepa por qué esa persona no puede validar). Solo viaja al
/// correo; nunca se escribe en logs ni en la auditoría.
/// </summary>
public sealed record IdentityRoundsAlertMessage(
    Guid TenantId,
    Guid ValidationId,
    Guid? ProcedureInstanceId,
    string? TramiteReferencia,
    string Nombre,
    string TipoDocumento,
    string NumeroDocumento,
    string Motivo,
    int RondasUsadas,
    int RondasMaximas,
    string Disparador,
    DateTimeOffset Fecha);

/// <summary>Resultado del envío a la lista: cuántos correos salieron y cuántos no.</summary>
public sealed record IdentityRoundsAlertDelivery(int Enviados, int Fallidos);

/// <summary>Persistencia global del configurador de rondas y de la auditoría de alertas (<c>admin.*</c>, sin RLS).</summary>
public interface IIdentityRoundsStore
{
    /// <summary>Configuración vigente; <see cref="IdentityRoundsConfig.Default"/> si la fila única no existe.</summary>
    Task<IdentityRoundsConfig> GetConfigAsync(CancellationToken ct = default);

    /// <summary>Rondas usadas por la persona (tipo y número de documento) en la empresa: suma de <c>1 + ResendCount</c> de todas sus filas.</summary>
    Task<int> CountRoundsForPersonAsync(Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default);

    /// <summary>Guarda la fila única (upsert) con quién y cuándo.</summary>
    Task SaveConfigAsync(IdentityRoundsConfig config, Guid? updatedBy, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Registra la alerta SOLO si no existe ya una para (validación, ronda). <c>true</c> = esta llamada la creó y debe enviar;
    /// <c>false</c> = ya se había registrado (reenvío del webhook o reconciliación): no se manda otro correo.
    /// </summary>
    Task<bool> TryRegisterAlertAsync(IdentityRoundsAlertRecord alert, CancellationToken ct = default);

    Task CompleteAlertAsync(Guid alertId, string resultado, CancellationToken ct = default);
}

/// <summary>Envía la alerta por el canal de correo de FLIT (<c>IEmailSender</c>); nunca lanza.</summary>
public interface IIdentityRoundsAlertSender
{
    Task<IdentityRoundsAlertDelivery> SendAsync(
        IdentityRoundsAlertMessage message, IReadOnlyList<string> recipients, CancellationToken ct = default);
}

/// <summary>Auditoría de cambios de configuración (rastro administrativo de Plataforma): quién, cuándo, valores anterior y nuevo.</summary>
public interface IIdentityRoundsConfigAuditor
{
    Task WriteAsync(Guid? actorUserId, IdentityRoundsConfig before, IdentityRoundsConfig after, CancellationToken ct = default);
}

/// <summary>
/// Regla de rondas para el flujo de identidad. <see cref="IsResendBlockedAsync"/> lo consulta el núcleo de reenvío antes de abrir la
/// siguiente ronda; <see cref="NotifyRoundRejectedAsync"/> lo invoca el punto único donde se aplica un rechazo terminal.
/// </summary>
public interface IIdentityRoundsPolicy
{
    /// <summary><c>true</c> si la fila ya usó todas las rondas permitidas (el reenvío debe responder <c>limite_rondas_alcanzado</c>).</summary>
    Task<bool> IsResendBlockedAsync(ProcedureInstanceBiometricValidation validation, CancellationToken ct = default);

    /// <summary><c>true</c> si la persona ya usó todas las rondas permitidas en la empresa: no se puede crear una fila nueva.</summary>
    Task<bool> IsPersonBlockedAsync(Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default);

    /// <summary>Si corresponde según el disparador, manda la alerta de la ronda rechazada UNA sola vez. Nunca lanza.</summary>
    Task NotifyRoundRejectedAsync(ProcedureInstanceBiometricValidation validation, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>Código de error de negocio cuando el reenvío ya no cabe en el tope de rondas.</summary>
public static class IdentityRoundsErrors
{
    public const string LimiteRondasAlcanzado = "limite_rondas_alcanzado";

    public const string LimiteRondasAlcanzadoMensaje =
        "Esta validación de identidad ya usó todas las rondas permitidas. Ya no se puede reenviar; contacta a soporte de FLIT.";
}
