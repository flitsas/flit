using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Alcance por compañía del historial operativo por placa (Feature #12189, HU #12192, decisión D1
/// del PO): <c>SuperAdmin</c> consulta la placa en TODAS las compañías; cualquier otro rol solo en
/// la suya.
///
/// <para>
/// Es una función pura y no una línea dentro del endpoint por una razón concreta: el listado
/// filtrado aplica el filtro de tenant de forma CONDICIONAL
/// (<c>if (tenantId is { } tid) query = query.Where(...)</c>), así que <c>null</c> no significa
/// "sin resolver" sino "todas las compañías". Un <c>null</c> que se cuele para un rol que no es
/// SuperAdmin no falla ni lanza: devuelve datos de otras empresas con apariencia de resultado
/// correcto. Ese error ya se cometió dos veces en el módulo de improntas. Aquí la decisión queda
/// aislada, con nombre, y cubierta por pruebas que valen para los tres casos.
/// </para>
///
/// <para>
/// El caso "autenticado pero sin compañía" se resuelve con 403 y NUNCA cayendo a <c>null</c>:
/// ante la duda, el alcance se cierra, no se abre. El 401 no se decide aquí — lo emite el pipeline
/// de autenticación antes de llegar al endpoint.
/// </para>
/// </summary>
public static class PlateHistoryScope
{
    /// <summary>
    /// Alcance resuelto. <see cref="TenantId"/> solo puede ser <c>null</c> cuando
    /// <see cref="IsGlobal"/> es <c>true</c> (SuperAdmin).
    /// </summary>
    /// <param name="Allowed">La consulta puede ejecutarse.</param>
    /// <param name="TenantId">Compañía a la que se acota; <c>null</c> = todas (solo SuperAdmin).</param>
    /// <param name="IsGlobal">El alcance abarca todas las compañías.</param>
    /// <param name="Detail">Motivo del rechazo (solo cuando <paramref name="Allowed"/> es falso).</param>
    public sealed record Result(bool Allowed, Guid? TenantId, bool IsGlobal, string? Detail)
    {
        /// <summary>Código HTTP del rechazo: 403 para el autenticado sin compañía resoluble.</summary>
        public int StatusCode => Allowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden;
    }

    private const string SinCompania =
        "El usuario autenticado no tiene una compañía asignada, así que no se puede acotar el "
        + "historial por placa a su empresa.";

    /// <summary>
    /// Resuelve el alcance a partir del contexto que deja <c>TenantEnforcementMiddleware</c>.
    /// </summary>
    /// <param name="contextTenantId">Tenant resuelto por el middleware (puede ser null).</param>
    /// <param name="isSuperAdmin">El consultante tiene rol SuperAdmin.</param>
    public static Result Resolve(Guid? contextTenantId, bool isSuperAdmin)
    {
        // SuperAdmin: alcance global explícito. Se ignora a propósito un X-Tenant-Id de acotación,
        // porque el criterio del PO para ESTE módulo es "la placa en todas las compañías" — el
        // listado general sigue siendo el sitio para mirar una empresa concreta.
        if (isSuperAdmin)
            return new Result(Allowed: true, TenantId: null, IsGlobal: true, Detail: null);

        // Cualquier otro rol queda acotado a SU compañía. Guid.Empty se trata como "sin compañía":
        // filtrar por el tenant vacío devolvería cero filas en vez de decir que falta el contexto.
        if (contextTenantId is { } tenantId && tenantId != Guid.Empty)
            return new Result(Allowed: true, TenantId: tenantId, IsGlobal: false, Detail: null);

        return new Result(Allowed: false, TenantId: null, IsGlobal: false, Detail: SinCompania);
    }

    /// <summary>
    /// Arma la consulta del historial a partir de un alcance YA resuelto.
    ///
    /// <para>
    /// El endpoint no fija <c>TenantId</c> por su cuenta: lo toma de <paramref name="scope"/>, que es
    /// lo único que sabe si el <c>null</c> está autorizado. Así el "todas las compañías" tiene un
    /// único origen posible y no puede aparecer por descuido en otra rama del endpoint.
    /// </para>
    ///
    /// <para>
    /// Orden y tope NO son negociables por el cliente: <c>createdAt</c> descendente siempre (el
    /// criterio del módulo es "más reciente primero", y dejarlo en un parámetro opcional significaba
    /// que sin él el orden lo decidía el handler), y <c>take</c> acotado a
    /// <see cref="ListProcedureInstancesHandler.MaxItems"/>. Un <c>take</c> nulo, cero o negativo cae
    /// al tope en lugar de devolver cero filas.
    /// </para>
    /// </summary>
    /// <param name="scope">Alcance resuelto por <see cref="Resolve"/>; debe estar autorizado.</param>
    /// <param name="placaNormalizada">Placa ya normalizada (ver <c>PlacaNormalizer</c>).</param>
    /// <param name="skip">Desplazamiento pedido; negativo se trata como 0.</param>
    /// <param name="take">Tamaño de página pedido; fuera de rango cae al tope.</param>
    public static ProcedureInstanceListRequest BuildRequest(
        Result scope, string placaNormalizada, int? skip, int? take)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!scope.Allowed)
            throw new InvalidOperationException(
                "No se puede consultar el historial por placa con un alcance no autorizado.");

        return new ProcedureInstanceListRequest
        {
            // null SOLO puede venir de un alcance global (SuperAdmin): ver Resolve.
            TenantId = scope.TenantId,
            Placa = placaNormalizada,
            Skip = Math.Max(0, skip ?? 0),
            Take = take is > 0 and <= ListProcedureInstancesHandler.MaxItems
                ? take.Value
                : ListProcedureInstancesHandler.MaxItems,
            SortBy = SortByCreatedAt,
            SortDescending = true,
        };
    }

    /// <summary>
    /// Campo de orden fijo del historial. Está en la whitelist de
    /// <see cref="ProcedureInstanceSortFields"/>, así que nunca se concatena en SQL.
    /// </summary>
    public const string SortByCreatedAt = "createdAt";
}
