namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Valores del campo <c>quipuxEstado</c> de la fila del listado de trámites (contrato con el frontend). <c>null</c> = el
/// trámite no va por Quipux. Es un vocabulario de PRESENTACIÓN, distinto del ciclo de la submission
/// (<c>QuipuxSubmissionEstado</c>), que vive en el módulo Quipux del que este dominio no depende.
/// </summary>
public static class QuipuxListadoEstado
{
    /// <summary>Preparado, con canal Quipux activo, esperando su radicación (sin envío o con el último pendiente).</summary>
    public const string EnCola = "en_cola";

    /// <summary>Último envío registrado en Quipux (a la espera de su resolución).</summary>
    public const string Radicado = "radicado";

    public const string Fallido = "fallido";

    public const string Rechazado = "rechazado";

    public const string Aprobado = "aprobado";
}
