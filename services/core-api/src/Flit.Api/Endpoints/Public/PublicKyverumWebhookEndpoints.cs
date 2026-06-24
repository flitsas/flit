using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Public;

/// <summary>
/// Webhook PÚBLICO de Kyverum Verify (HU #10233, AC2/AC3). Kyverum notifica aquí el resultado de una
/// validación, haciendo POST a la URL que registramos al crearla — con NUESTRO id de validación
/// incrustado en el path (<c>{validationId}</c>), ya que el cuerpo no lo repite. Sin auth de borde (la
/// credencial es la firma HMAC del header <c>x-kv-signature</c>): expuesto vía Gateway YARP con una ruta
/// pública (<c>/api/v1/webhooks/{**catch-all}</c>) y <c>AllowAnonymous</c> aquí. Se lee el cuerpo CRUDO
/// (bytes exactos) para verificar la firma sin re-serializar.
/// </summary>
internal static class PublicKyverumWebhookEndpoints
{
    internal static IEndpointRouteBuilder MapPublicKyverumWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/kyverum-verify/{validationId:guid}", async (
            Guid validationId,
            HttpRequest request,
            KyverumWebhookHandler handler,
            CancellationToken ct) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, ct);
            var rawBody = ms.ToArray();

            var signature = request.Headers.TryGetValue(KyverumWebhookVerifier.SignatureHeader, out var sig)
                ? sig.ToString()
                : null;

            var (_, error) = await handler.HandleAsync(new KyverumWebhookInput(validationId, rawBody, signature), ct);
            return error switch
            {
                "cuerpo_invalido" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Cuerpo del webhook inválido."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Validación no encontrada."),
                // AC3: firma inválida ⇒ 401 sin cambios en BD.
                "firma_invalida" => Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Firma del webhook inválida."),
                _ => Results.Ok(new { ok = true }),
            };
        }).WithName("KyverumVerifyWebhook").AllowAnonymous().DisableAntiforgery();

        return app;
    }
}
