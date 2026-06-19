using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Public;

/// <summary>
/// Endpoints PÚBLICOS de la biométrica (Slice 6): el participante abre el magic-link con su token
/// y sube las 3 fotos. Sin auth ni tenant header; el token (alto entropía) es la credencial.
/// </summary>
internal static class PublicBiometricaEndpoints
{
    internal static IEndpointRouteBuilder MapPublicBiometricaEndpoints(this IEndpointRouteBuilder app)
    {
        // GET info de la tarea por token (sin enumeración de PII).
        app.MapGet("/api/v1/public/biometric/{token}", async (
            string token,
            GetBiometriaByTokenHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(token, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Validación biométrica no encontrada.")
                : Results.Ok(result);
        }).WithName("GetPublicBiometric").AllowAnonymous();

        // POST completar con las 3 fotos (multipart/form-data). Antiforgery deshabilitado (público).
        app.MapPost("/api/v1/public/biometric/{token}", async (
            string token,
            IFormFile? rostro,
            [FromForm(Name = "cedula_frontal")] IFormFile? cedulaFrontal,
            [FromForm(Name = "cedula_reverso")] IFormFile? cedulaReverso,
            CompletarBiometriaHandler handler,
            CancellationToken ct) =>
        {
            await using var sRostro = rostro?.OpenReadStream();
            await using var sFrontal = cedulaFrontal?.OpenReadStream();
            await using var sReverso = cedulaReverso?.OpenReadStream();

            var input = new CompletarBiometriaInput(sRostro, sFrontal, sReverso);
            var (result, error) = await handler.HandleAsync(token, input, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Validación biométrica no encontrada."),
                "expirada" => Results.Problem(statusCode: 410, title: "Gone", detail: "El enlace de validación biométrica expiró."),
                "estado_invalido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La validación biométrica ya no admite intentos."),
                "intentos_agotados" => Results.Problem(statusCode: 429, title: "Too Many Requests", detail: "Se agotaron los intentos de validación biométrica."),
                _ => Results.Ok(result),
            };
        }).WithName("CompletePublicBiometric").AllowAnonymous().DisableAntiforgery();

        return app;
    }
}
