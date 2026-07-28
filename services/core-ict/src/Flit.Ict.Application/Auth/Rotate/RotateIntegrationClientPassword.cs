namespace Flit.Ict.Application.Auth.Rotate;

/// <summary>
/// Petición de rotación de contraseña del cliente de integración. Se autentica con la contraseña ACTUAL
/// (no requiere token): así un cliente con <c>must_rotate=true</c> —que el login bloquea sin emitir token—
/// puede rotar y desbloquearse.
/// </summary>
public sealed record RotateIntegrationClientPasswordCommand(string Username, string CurrentPassword, string NewPassword);
