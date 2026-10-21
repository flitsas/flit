namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Catálogo de organismos por el gRPC de catálogos de core-api (<c>flit.tramites.v1.CatalogosService.ListarOrganismosTransito</c>,
/// ADR-0070, HU #13733; mismo cliente y scope <c>platform.tramites.catalogos</c> que los tipos de trámite, patrón Comparendos
/// HU #13459). Descarta los organismos sin código y recorta los textos; un departamento vacío queda null.
/// </summary>
/// <example>
/// <code>
/// var organismos = await new CatalogoOrganismosGrpc(cliente).ListarAsync(ct);
/// </code>
/// </example>
public sealed class CatalogoOrganismosGrpc(Flit.Tramites.Grpc.V1.CatalogosService.CatalogosServiceClient cliente) : ICatalogoOrganismosPlataforma
{
    public async Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct)
    {
        var r = await cliente.ListarOrganismosTransitoAsync(new Flit.Tramites.Grpc.V1.ListarOrganismosTransitoRequest(), cancellationToken: ct).ConfigureAwait(false);
        static string? Texto(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return [.. r.Organismos.Where(o => !string.IsNullOrWhiteSpace(o.Codigo))
            .Select(o => new OrganismoPlataforma(o.Codigo.Trim(), o.Nombre.Trim(), Texto(o.DepartamentoCodigo), Texto(o.Departamento)))];
    }
}
