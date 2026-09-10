namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// HU #11360 AC6 — el login del canal Renting falló (credenciales rechazadas, respuesta sin
/// token, error del proveedor, o el login agotó su propio tiempo de espera
/// <c>RENTING_API_LOGIN_SECONDS_TIMEOUT</c>). Se lanza SIEMPRE que el login no produce un token
/// utilizable — nunca se silencia devolviendo <c>null</c> tratado como éxito parcial (ese es
/// exactamente el defecto de <c>FasecoldaTokenCache.GetTokenAsync</c>, que este tipo evita a
/// propósito). Quien la captura (<see cref="RentingTokenCache"/>,
/// <see cref="RentingAuthenticatedRequestExecutor"/>) la mapea a
/// <see cref="Flit.Modules.Security.Domain.Auth.EmailSendOutcome.AuthenticationFailed"/>.
/// </summary>
public sealed class RentingAuthenticationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
