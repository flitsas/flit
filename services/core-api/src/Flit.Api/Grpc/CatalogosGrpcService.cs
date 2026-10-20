using Flit.Infrastructure.Persistence;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Grpc;

/// <summary>
/// Catálogos de la plataforma por gRPC (ADR-0070, HU #13459). Comparendos sincroniza a diario su réplica de organismos
/// de tránsito con esta lista (S-66) en vez de leer <c>catalogs.transit_offices</c> por SQL (ADR-0064). Devuelve también
/// los inactivos, para que la réplica los marque.
/// </summary>
public sealed class CatalogosGrpcService(FlitDbContext db) : CatalogosService.CatalogosServiceBase
{
    public override async Task<ListarOrganismosTransitoResponse> ListarOrganismosTransito(ListarOrganismosTransitoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var filas = await db.TransitOffices.AsNoTracking().OrderBy(o => o.Code)
            .Select(o => new { o.Id, o.Code, o.Name, o.DepartmentCode, o.CityCode, o.CityName, o.DepartmentName, o.IsActive })
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ListarOrganismosTransitoResponse();
        respuesta.Organismos.AddRange(filas.Select(o => new OrganismoTransito
        {
            Id = o.Id.ToString(),
            Codigo = o.Code,
            Nombre = o.Name,
            DepartamentoCodigo = o.DepartmentCode,
            MunicipioCodigo = o.CityCode,
            Municipio = o.CityName ?? string.Empty,
            Departamento = o.DepartmentName ?? string.Empty,
            Activo = o.IsActive,
        }));
        return respuesta;
    }
}
