using Flit.Admin.Application.Companies.ListCompanies;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del Administrador de Compañías (HU #10189).
/// </summary>
public static class AdminCompaniesEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy);

        // GET /api/v1/admin/companies/index — listado paginado con filtros (AC1, AC2).
        group.MapGet("/index", ListCompaniesAsync)
            .WithName("AdminCompaniesIndex");

        return app;
    }

    private static async Task<IResult> ListCompaniesAsync(
        [FromServices] ListCompaniesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? nit = null,
        [FromQuery] string? razonSocial = null,
        [FromQuery] bool? estadoActivo = null,
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = new ListCompaniesQuery
        {
            Nit = nit,
            RazonSocial = razonSocial,
            EstadoActivo = estadoActivo,
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta,
            Page = page,
            PageSize = pageSize,
        };

        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }
}
