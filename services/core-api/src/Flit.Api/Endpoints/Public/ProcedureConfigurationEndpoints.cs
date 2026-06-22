using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Public;

internal static class ProcedureConfigurationEndpoints
{
    internal static IEndpointRouteBuilder MapPublicProcedureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/procedure-types/{code}/configuration", async (
            string code,
            GetProcedureTypeConfigurationHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(code, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Published procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureTypeConfiguration");

        return app;
    }
}
