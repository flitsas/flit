namespace Flit.Ict.Domain.Enums;

/// <summary>
/// Vocabulario v2-native del ciclo de vida del PRE-TRÁMITE en ICT (expuesto al cliente por
/// la API de estado). Reemplaza los códigos numéricos de v1. El mapeo interno desde
/// <c>process_status_id</c> (que los stored procedures siguen escribiendo) vive en
/// <see cref="Map(short, bool, bool, bool)"/>.
/// </summary>
public static class IctEstado
{
    public const string Recibido = "recibido";
    public const string EnValidacionNegocio = "en_validacion_negocio";
    public const string EnValidacionExterna = "en_validacion_externa";
    public const string Procesado = "procesado";
    public const string BorradorCreado = "borrador_creado";
    public const string ConNovedades = "con_novedades";
    public const string Anulado = "anulado";

    public static readonly IReadOnlyList<string> Terminales = [BorradorCreado, Anulado];

    /// <summary>
    /// Proyecta el estado interno v1 (<c>process_status_id</c> + flags de validación) al
    /// vocabulario v2 expuesto al cliente. Ver plan §A.9 (Plano B).
    /// </summary>
    public static string Map(short processStatusId, bool hasProcedureInstance, bool businessValidated, bool externalStarted)
    {
        if (hasProcedureInstance)
        {
            return BorradorCreado;
        }

        return processStatusId switch
        {
            1 => Recibido,
            2 when !businessValidated => EnValidacionNegocio,
            2 => EnValidacionExterna,
            3 => Procesado,
            4 => ConNovedades,
            _ => Anulado,
        };
    }
}
