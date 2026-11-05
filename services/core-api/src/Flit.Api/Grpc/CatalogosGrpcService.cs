using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Grpc;

/// <summary>
/// Catálogos de la plataforma por gRPC (ADR-0070, HU #13459). Comparendos sincroniza a diario su réplica de organismos
/// de tránsito con esta lista (S-66) en vez de leer <c>catalogs.transit_offices</c> por SQL (ADR-0064). Devuelve también
/// los inactivos, para que la réplica los marque. Diagnóstico lee los tipos de trámite publicados (HU #13731).
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

    /// <summary>
    /// Tipos de trámite publicados y activos, ordenados por código: el mismo criterio de publicados que
    /// <c>GetPublishedProcedureTypesHandler</c> más <c>is_active</c>. Catálogo global, sin tenant. Además de la policy del
    /// servicio exige <see cref="CatalogoTiposTramiteAuthorization.Policy"/> (solo svc-diagnostico).
    /// </summary>
    [Authorize(Policy = CatalogoTiposTramiteAuthorization.Policy)]
    public override async Task<ListarTiposTramiteResponse> ListarTiposTramite(ListarTiposTramiteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var filas = await db.ProcedureTypes.AsNoTracking()
            .Where(t => t.PublicationStatus == PublicationStatus.Published && t.IsActive)
            .OrderBy(t => t.Code)
            .Select(t => new { t.Code, t.Name, t.Family, t.IsActive, t.WizardEnabled })
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ListarTiposTramiteResponse();
        respuesta.Tipos.AddRange(filas
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => new TipoTramite { Codigo = t.Code, Nombre = t.Name, Familia = t.Family, Activo = t.IsActive, WizardEnabled = t.WizardEnabled }));
        return respuesta;
    }
}
