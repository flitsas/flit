using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Queries.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>
/// Reportes 2.0 — Consultas personalizadas de Comparendos (CONTRATO §4), con el motor compartido de Trámites.
/// Todas las rutas aceptan <c>?empresa=</c> (SuperAdmin; sin ella ve todas las empresas).
/// </summary>
public static class ConsultasPersonalizadasEndpoints
{
    /// <summary>Cómo se nombra la pantalla en los errores de validación (también la usa el envío programado).</summary>
    internal const string Superficie = "las consultas de comparendos";

    /// <summary>Se registra con una línea en <c>Program.Build</c>; no pide servicios propios en DI.</summary>
    public static WebApplication MapConsultasPersonalizadas(this WebApplication app, string prefijo)
    {
        ArgumentNullException.ThrowIfNull(app);
        var g = app.MapGroup($"{prefijo}/reportes/consultas");
        g.MapGet("/fields", Campos).RequirePermiso(Permisos.ReportesConsultas);
        g.MapPost("/run", Ejecutar).RequirePermiso(Permisos.ReportesConsultas);
        g.MapGet("/saved", Listar).RequirePermiso(Permisos.ReportesConsultas);
        g.MapPost("/saved", Guardar).RequirePermiso(Permisos.ReportesConsultas);
        g.MapDelete("/saved/{id:guid}", Borrar).RequirePermiso(Permisos.ReportesConsultas);
        g.MapGet("/programaciones", ListarProgramaciones).RequirePermiso(Permisos.ReportesProgramar);
        g.MapPost("/programaciones", GuardarProgramacion).RequirePermiso(Permisos.ReportesProgramar);
        g.MapDelete("/programaciones/{id:guid}", BorrarProgramacion).RequirePermiso(Permisos.ReportesProgramar);
        return app;
    }

    /// <summary>El mismo alcance que Reportes (ReportesEndpoints.AlcanceAsync, R2): la empresa del usuario o la que elige el SuperAdmin.</summary>
    internal static async Task<AlcanceConsulta> AlcanceAsync(UsuarioActual usuario, Capacidades capacidades, Guid? empresa, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa, obligatoria: false);
        if (tenant is { } t && !usuario.EsSuperAdmin)
            await capacidades.ExigirAsync(t, Capacidad.Reportes, ct).ConfigureAwait(false);
        return new AlcanceConsulta(tenant);
    }

    private static void Validar(AlcanceConsulta alcance, QueryDefinition? definicion)
    {
        if (QueryConditionValidator.Validate(alcance.Catalogo, definicion?.Condiciones, Superficie) is { } error)
            throw new ReglaException("CONSULTA_INVALIDA", error);
    }

    private static async Task<IResult> Campos(Guid? empresa, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, ConsultaBandeja bandeja, CancellationToken ct)
    {
        var alcance = await AlcanceAsync(usuario, capacidades, empresa, ct).ConfigureAwait(false);
        return Results.Ok(await new ServicioConsultasPersonalizadas(db, bandeja).CamposAsync(alcance, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> Ejecutar(Guid? empresa, int? page, int? pageSize, [FromBody] QueryDefinition? definition, UsuarioActual usuario,
        Capacidades capacidades, ComparendosDb db, ConsultaBandeja bandeja, CancellationToken ct)
    {
        var alcance = await AlcanceAsync(usuario, capacidades, empresa, ct).ConfigureAwait(false);
        Validar(alcance, definition);
        var request = QueryNormalizer.BuildRequest(alcance.Catalogo, definition, page, pageSize);
        ComparendoQueryResult resultado;
        try
        {
            resultado = await new ServicioConsultasPersonalizadas(db, bandeja).EjecutarAsync(alcance, request, ct).ConfigureAwait(false);
        }
        catch (SuperAdminQueryTooBroadException ex)
        {
            throw new ReglaException("CONSULTA_DEMASIADO_AMPLIA", ex.Message);
        }

        // §2.4: el SuperAdmin con «todas» deja anotadas las empresas cuyos comparendos vio.
        foreach (var leida in ServicioConsultasPersonalizadas.EmpresasDe(resultado))
            usuario.AnotarLectura(leida);
        return Results.Ok(resultado);
    }

    private static async Task<IResult> Listar(Guid? empresa, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var alcance = await AlcanceAsync(usuario, capacidades, empresa, ct).ConfigureAwait(false);
        return Results.Ok(await new RepositorioConsultasGuardadas(db, reloj).ListarAsync(alcance, usuario.Id, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> Guardar(Guid? empresa, [FromBody] GuardarConsultaRequest? body, UsuarioActual usuario, Capacidades capacidades,
        ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var alcance = await AlcanceAsync(usuario, capacidades, empresa, ct).ConfigureAwait(false);
        Validar(alcance, body?.Definition);
        var input = SavedQuery.BuildInput(alcance.Catalogo, body?.Nombre, body?.Descripcion, body?.Definition);
        return Results.Ok(await new RepositorioConsultasGuardadas(db, reloj).GuardarAsync(alcance, usuario.Id, body?.Id, input, ct).ConfigureAwait(false));
    }

    // ── Programaciones ────────────────────────────────────────────────────────────────────────

    /// <summary>Programar es operar en nombre de la empresa (envía correos): el SuperAdmin solo lee.</summary>
    private static async Task<Guid> EmpresaProgramacionAsync(UsuarioActual usuario, Capacidades capacidades, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        await capacidades.ExigirAsync(tenant, Capacidad.Reportes, ct).ConfigureAwait(false);
        return tenant;
    }

    private static async Task<IResult> ListarProgramaciones(Guid? empresa, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa, obligatoria: true)!.Value;
        if (!usuario.EsSuperAdmin)
            await capacidades.ExigirAsync(tenant, Capacidad.Reportes, ct).ConfigureAwait(false);
        return Results.Ok(await new RepositorioProgramaciones(db, reloj).ListarAsync(tenant, usuario.EsSuperAdmin ? null : usuario.Id, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> GuardarProgramacion([FromBody] GuardarProgramacionRequest? body, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var tenant = await EmpresaProgramacionAsync(usuario, capacidades, ct).ConfigureAwait(false);
        if (body is null)
            throw new ReglaException("PROGRAMACION_INVALIDA", "Falta la programación.");
        return Results.Ok(await new RepositorioProgramaciones(db, reloj).GuardarAsync(tenant, usuario.Id, body, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> BorrarProgramacion(Guid id, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var tenant = await EmpresaProgramacionAsync(usuario, capacidades, ct).ConfigureAwait(false);
        await new RepositorioProgramaciones(db, reloj).BorrarAsync(tenant, usuario.Id, id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> Borrar(Guid id, Guid? empresa, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var alcance = await AlcanceAsync(usuario, capacidades, empresa, ct).ConfigureAwait(false);
        await new RepositorioConsultasGuardadas(db, reloj).BorrarAsync(alcance, usuario.Id, id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
