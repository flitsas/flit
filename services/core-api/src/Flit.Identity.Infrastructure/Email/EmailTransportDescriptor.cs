namespace Flit.Infrastructure.Email;

/// <summary>
/// HU #11368 (Feature #11349, AC8) — declara si el TRANSPORTE de correo activo en este proceso es
/// de consola (Development, sin correo real) o real (<see cref="SmtpEmailSender"/>).
/// </summary>
/// <remarks>
/// El puerto <c>IEmailSender</c> no sirve para esta distinción: <see cref="ConsoleEmailSender"/>
/// devuelve <c>EmailSendResult.Sent</c> exactamente igual que <see cref="SmtpEmailSender"/>, así
/// que un consumidor no puede diferenciarlos por el retorno de <c>SendAsync</c>. Y el
/// <c>IEmailSender</c> resuelto por inyección de dependencias NO es el transporte concreto: es el
/// decorador de bitácora (<c>NotificationDeliveryLoggingEmailSender</c>, HU #11363), que envuelve
/// a cualquiera de los dos.
/// <para>
/// Este descriptor se calcula UNA sola vez, con la MISMA condición que decide qué implementación
/// concreta registrar (<c>useConsoleEmailSender</c> en <c>InfrastructureExtensions.AddPostgresInfrastructure</c>),
/// y se publica como <c>Singleton</c> para que un consumidor (el banco de pruebas de notificaciones,
/// HU #11368) pueda preguntarlo sin inspeccionar el árbol de inyección de dependencias.
/// </para>
/// </remarks>
/// <param name="IsConsole">
/// <c>true</c> cuando el transporte activo es <see cref="ConsoleEmailSender"/> — ningún correo real
/// sale del proceso, solo queda una línea en el log.
/// </param>
public sealed record EmailTransportDescriptor(bool IsConsole);
