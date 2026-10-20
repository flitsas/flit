using Flit.Comparendos.Api.Plataforma;

namespace Flit.Comparendos.Api.Modulos;

/// <summary>
/// Lo que la app necesita al abrir: quién es el usuario, su empresa, sus permisos y las capacidades de la empresa (para
/// armar el menú). El SuperAdmin no tiene empresa propia en Comparendos: elige una gestora en cada pantalla.
/// </summary>
internal static class Sesion
{
    public static async Task<IResult> Obtener(UsuarioActual usuario, Capacidades capacidades, CancellationToken ct)
    {
        object? empresa = null;
        if (!usuario.EsSuperAdmin && usuario.EmpresaId is { } empresaId)
        {
            var config = await capacidades.DeAsync(empresaId, ct, usuario.EmpresaNombre).ConfigureAwait(false);
            empresa = new
            {
                id = empresaId,
                nombre = config.EmpresaNombre ?? usuario.EmpresaNombre,
                capacidades = new
                {
                    derechosPeticion = config.HasRightPetitionModule,
                    consultaContraventor = config.AllowsContravenerConsultation,
                    cambioContraventor = config.AllowsContravenerChange,
                    ocr = config.HasOcrAccess,
                    reportes = config.HasReportesModule,
                },
            };
        }

        return Results.Ok(new
        {
            usuario = new { id = usuario.Id, email = usuario.Email, esSuperAdmin = usuario.EsSuperAdmin },
            permisos = Permisos.Modulos.SelectMany(m => m.Permisos).Select(p => p.Slug).Where(usuario.Tiene).ToArray(),
            empresa,
        });
    }
}
