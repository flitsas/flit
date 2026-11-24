namespace Flit.Ict.Domain.Enums;

/// <summary>
/// Catálogo de códigos numéricos v1 de <c>ict.external_integration_parameter_process_status</c>
/// y mapa desde el vocabulario v2 (ToStatus del callback de estados). Fuente canónica para todos
/// los lugares del servicio que describen o traducen estados del pipeline.
/// </summary>
public static class IctProcessStatusCodes
{
    public const short Registrado   = 1;
    public const short EnValidacion = 2;
    public const short Procesado    = 3;
    public const short ConNovedades = 4;
    public const short Borrador     = 5;
    public const short Anulado      = 6;
    public const short Preparado    = 7;
    public const short Preasignacion = 8;
    public const short Asignado     = 9;
    public const short Entregado    = 10;
    public const short Aprobado     = 11;
    public const short Rechazado    = 12;
    public const short Revocado     = 13;

    /// <summary>
    /// Descripción legible del código v1 (paridad con la columna <c>name_description</c> del
    /// catálogo, en Title Case como el histórico de estado v1 que ve el cliente gestor).
    /// </summary>
    public static string Describe(short code) => code switch
    {
        Registrado    => "Registrado",
        EnValidacion  => "En Validacion",
        Procesado     => "Procesado",
        ConNovedades  => "Con Novedades",
        Borrador      => "Borrador",
        Anulado       => "Anulado",
        Preparado     => "Preparado",
        Preasignacion => "Preasignacion",
        Asignado      => "Asignado",
        Entregado     => "Entregado",
        Aprobado      => "Aprobado",
        Rechazado     => "Rechazado",
        Revocado      => "Revocado",
        _             => string.Empty,
    };

    /// <summary>
    /// Mapea el vocabulario v2 de <c>StateChangeNotification.ToStatus</c> (minúsculas) al código
    /// numérico v1. Valores no reconocidos caen en <see cref="Procesado"/> (3) como fallback seguro,
    /// que es lo que el callback enviaba antes de este mapeo.
    /// </summary>
    public static short MapFromV2(string? toStatus) => toStatus?.ToLowerInvariant() switch
    {
        "borrador"      => Borrador,
        "preparado"     => Preparado,
        "preasignacion" => Preasignacion,
        "asignado"      => Asignado,
        "entregado"     => Entregado,
        "aprobado"      => Aprobado,
        "rechazado"     => Rechazado,
        "anulado"       => Anulado,
        "revocado"      => Revocado,
        _               => Procesado,
    };

    /// <summary>
    /// Devuelve <c>true</c> para los estados OT post-borrador (códigos 7-13) que deben actualizar
    /// el <c>process_status_id</c> del master para que <c>statusValidation</c> refleje el último
    /// estado conocido del trámite ya materializado.
    /// </summary>
    public static bool IsOtState(short code) => code is >= 7 and <= 13;
}
