namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lista blanca del orden de la bandeja del OT (<c>OtClientProcedureRepository.ApplyListSort</c>): los valores de
/// <c>sortBy</c> que mueven el orden, en minúsculas (el repositorio compara tras <c>Trim().ToLowerInvariant()</c>).
/// Cualquier otro valor cae al orden por defecto (fecha de creación descendente).
/// </summary>
/// <remarks>
/// Épica #13216 (HU #13390, hallazgo L3) — el <c>switch</c> del orden usa estas constantes y el resumen auditado del
/// lote (<c>OtBandejaLoteFiltro</c>) solo guarda un <c>sortBy</c> literal si está aquí: una sola lista, sin duplicar.
/// <para>Uso de ejemplo:
/// <code>
/// OtClientProcedureListSort.Canonico(" Placa ");     // "placa"
/// OtClientProcedureListSort.Canonico("JUAN PEREZ");  // null
/// </code></para>
/// </remarks>
public static class OtClientProcedureListSort
{
    public const string Vin = "vin";
    public const string Placa = "placa";
    public const string Vendedor = "vendedor";
    public const string Comprador = "comprador";
    public const string Gestor = "gestor";
    public const string ReferenceNumber = "referencenumber";
    public const string Radicado = "radicado";
    public const string Status = "status";
    public const string Estado = "estado";
    public const string Empresa = "empresa";
    public const string TipoTramite = "tipo_tramite";
    public const string TipoTramiteSinGuion = "tipotramite";
    public const string CreatedAt = "createdat";
    public const string FechaRadicacion = "fecharadicacion";

    public const string Asc = "asc";
    public const string Desc = "desc";

    /// <summary>Todos los <c>sortBy</c> que reconoce el <c>switch</c> del orden.</summary>
    public static IReadOnlySet<string> Todos { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Vin, Placa, Vendedor, Comprador, Gestor, ReferenceNumber, Radicado, Status, Estado, Empresa, TipoTramite,
        TipoTramiteSinGuion, CreatedAt, FechaRadicacion,
    };

    /// <summary>La forma con la que el repositorio compara <c>sortBy</c>.</summary>
    public static string Normalizar(string? sortBy) => (sortBy ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary><c>sortBy</c> normalizado si está en la lista blanca; si no, <c>null</c>.</summary>
    public static string? Canonico(string? sortBy)
    {
        var normalizado = Normalizar(sortBy);
        return Todos.Contains(normalizado) ? normalizado : null;
    }

    /// <summary><c>asc</c>/<c>desc</c> en minúsculas, o <c>null</c> si no es ninguno de los dos.</summary>
    public static string? DireccionCanonica(string? sortDir)
    {
        var normalizado = Normalizar(sortDir);
        return normalizado is Asc or Desc ? normalizado : null;
    }
}
