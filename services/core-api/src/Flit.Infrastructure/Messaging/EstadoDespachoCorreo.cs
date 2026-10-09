using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Estado de un cupo de aviso de correo tras pasarlo al emisor (HU #13359). Desde el corte, core-api no entrega: deja
/// el correo como trabajo para Notificaciones, y eso es <c>encolado</c>, no <c>enviado</c>. Si llegó lo dice el registro
/// de entregas de Notificaciones. <c>enviado</c> queda para las filas de antes del corte.
/// </summary>
internal static class EstadoDespachoCorreo
{
    public const string Enviado = "enviado";
    public const string Encolado = "encolado";

    /// <summary>Estado de una fila cuyo envío salió bien.</summary>
    public static string De(EmailSendResult resultado) =>
        resultado.Outcome == EmailSendOutcome.Queued ? Encolado : Enviado;

    /// <summary>La fila ya salió de core-api (entregada o encolada): no se reintenta.</summary>
    public static bool Salio(string status) => status is Enviado or Encolado;
}
