using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Credenciales;

/// <summary>Cuerpo de <c>PUT /backoffice/empresas/{tenantId}/credenciales</c>. <see cref="ToString"/> no imprime los valores.</summary>
public sealed record CargaCredencialesPeticion(string? ApiKey, string? WebhookSecret)
{
    public override string ToString() => "CargaCredencialesPeticion { [REDACTADO] }";
}

/// <summary>
/// Rutas del Super Admin sobre las credenciales de una compañía (plan §5.1, D-409, EP-CF3c), bajo
/// <see cref="PoliticaBackoffice"/> y con la compañía explícita de la ruta (<see cref="EmpresaExplicita"/>, solo Super Admin):
/// <list type="bullet">
///   <item><c>PUT</c> (<c>impuestos.credenciales.cargar</c>): <c>{apiKey, webhookSecret?}</c> → 204 sin cuerpo; 422
///   <c>CREDENCIAL_INVALIDA</c>; 409 si el estado no admite carga; 503 si el API no permite validar.</item>
///   <item><c>GET</c> (<c>impuestos.backoffice.ver</c>): <c>{tieneApiKey, tieneWebhookSecret, origen, apiKeyPrefijo,
///   actualizadoEn}</c>; la lectura queda auditada.</item>
/// </list>
/// Las respuestas llevan <c>Cache-Control: no-store</c>.
/// </summary>
/// <example>
/// <code>
/// CredencialesBackofficeEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class CredencialesBackofficeEndpoints
{
    public const string Ruta = "/backoffice/empresas/{tenantId}/credenciales";
    public const string Recurso = "credenciales";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var grupo = api.MapGroup(Ruta).RequireAuthorization(PoliticaBackoffice.Nombre).SinCacheEnRespuesta();
        grupo.MapGet(string.Empty, VerAsync).RequirePermiso(Permisos.BackofficeVer).ConEmpresaExplicita(Recurso)
            .WithName("VerCredencialesEmpresaImpuestos");
        grupo.MapPut(string.Empty, CargarAsync).RequirePermiso(Permisos.CredencialesCargar).ConEmpresaExplicita(Recurso).CuerpoAcotado()
            .WithName("CargarCredencialesEmpresaImpuestos");
    }

    private static async Task<IResult> VerAsync(HttpContext http, CredencialesEmpresa credenciales, CancellationToken ct) =>
        Results.Ok(await credenciales.VerAsync(EmpresaExplicita.De(http), ct).ConfigureAwait(false));

    private static async Task<IResult> CargarAsync(
        HttpContext http, CargaCredencialesPeticion peticion, CredencialesEmpresa credenciales, UsuarioActual usuario, CancellationToken ct)
    {
        if (usuario.Id == Guid.Empty)
            throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: la carga no se puede auditar.");
        await credenciales.CargarAsync(EmpresaExplicita.De(http), peticion.ApiKey, peticion.WebhookSecret, usuario.Id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
