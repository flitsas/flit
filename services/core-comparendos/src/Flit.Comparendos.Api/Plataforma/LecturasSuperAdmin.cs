using System.Text.Json;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>
/// §2.4: cada lectura del SuperAdmin sobre los datos de una gestora queda en la auditoría (una fila por empresa y
/// petición, con la ruta leída). Se escribe al final de la petición, en un contexto propio, y un fallo al auditar no
/// tumba la respuesta: se registra en el log.
/// </summary>
public sealed partial class LecturasSuperAdmin(RequestDelegate siguiente, ILogger<LecturasSuperAdmin> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext contexto, UsuarioActual usuario, IServiceScopeFactory scopes, Reloj reloj)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(usuario);
        await siguiente(contexto).ConfigureAwait(false);
        if (usuario.EmpresasLeidas.Count == 0 || contexto.Response.StatusCode >= 400)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            var ruta = $"{contexto.Request.Method} {contexto.Request.Path}{contexto.Request.QueryString}";
            foreach (var empresa in usuario.EmpresasLeidas)
            {
                db.AuditoriasConfiguracion.Add(new AuditoriaConfiguracion
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = empresa,
                    Entidad = "lectura_superadmin",
                    EntidadId = contexto.Request.Path.Value is { Length: > 100 } p ? p[..100] : contexto.Request.Path.Value ?? string.Empty,
                    Accion = "LEER",
                    Cambios = JsonSerializer.Serialize(new[] { new { campo = "ruta", anterior = (string?)null, nuevo = ruta } }, Json),
                    UsuarioId = usuario.Id,
                    UsuarioNombre = usuario.Nombre,
                    RegistradoEn = reloj.Ahora,
                });
            }

            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFallo(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudo auditar una lectura del SuperAdmin.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}
