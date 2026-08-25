using System.Globalization;
using Flit.Ict.Api.Authorization;
using Flit.Ict.Domain.Trazabilidad;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Trazabilidad ICT por trámite (Feature #11814). Solo lectura: no toca el pipeline de integración
/// ni la escritura de logs. El Gateway aplica JwtRequired; aquí se verifica <c>ict.logs.read</c>,
/// el mismo permiso que ya gobierna la observabilidad ICT.
/// </summary>
public static class IctTrazabilidadEndpoints
{
    public static IEndpointRouteBuilder MapIctTrazabilidadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ict/trazabilidad");

        // HU #11815 — bandeja de trámites de la integración.
        group.MapGet("/tramites", async (
            HttpContext context,
            ITrazabilidadBandejaQuery query,
            long? numero,
            string? placas,
            Guid? compania,
            int? tipo,
            string? familia,
            int? operacion,
            string? estado,
            DateTime? desde,
            DateTime? hasta,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                // Cuerpo mínimo a propósito: no revela cuántos trámites existen ni de qué compañías.
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var filtro = new TrazabilidadFiltro(
                // El alcance NUNCA viene de la petición. Quien no es SuperAdmin queda atado a su
                // tenant aunque mande el parámetro «compania» de otra empresa: ambos predicados se
                // aplican en AND, así que el suyo sigue mandando.
                TenantId: access.IsSuperAdmin ? null : access.TenantId,
                Numero: numero,
                PlacasOVins: PlacaVinFiltro.Parse(placas),
                CompaniaTenantId: compania,
                TipoTramite: tipo,
                Familia: FamiliaFiltro.Normalizar(familia),
                Operacion: operacion,
                Estado: estado,
                Desde: NormalizarDesde(desde),
                Hasta: NormalizarHasta(hasta),
                Page: page ?? 1,
                PageSize: pageSize ?? 25);

            var resultado = await query.ConsultarAsync(filtro, ct);
            return Results.Ok(resultado);
        });

        // HU #11815 — catálogo del desplegable «tipo de trámite». Va aquí y no en un módulo de
        // parámetros porque depende del alcance de quien pregunta: cada quien ve los tipos que
        // realmente aparecen entre SUS trámites.
        group.MapGet("/tipos", async (
            HttpContext context,
            ITiposTramiteQuery query,
            Guid? compania,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tipos = await query.ConsultarAsync(
                access.IsSuperAdmin ? null : access.TenantId, compania, ct);
            return Results.Ok(tipos);
        });

        // HU #11816 — recorrido de un trámite con los tiempos consumidos en cada etapa.
        group.MapGet("/tramites/{numero:long}/recorrido", async (
            HttpContext context,
            IRecorridoTramiteQuery query,
            long numero,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var recorrido = await query.ConsultarAsync(
                numero, access.IsSuperAdmin ? null : access.TenantId, ct);

            // Un trámite de otra compañía responde 404 igual que uno inexistente. Un 403 delataría que
            // el número existe, y eso ya es información sobre la operación de otro cliente.
            return recorrido is null
                ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(recorrido);
        });

        // HU #11817 — consultas a fuentes externas del trámite.
        group.MapGet("/tramites/{numero:long}/consultas-fuente", async (
            HttpContext context,
            IConsultasFuenteQuery query,
            long numero,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var consultas = await query.ConsultarAsync(
                numero, access.IsSuperAdmin ? null : access.TenantId, ct);

            // Null es «no existe o no es tuyo»; lista vacía es «existe y aún no ha consultado nada».
            // Confundirlos haría parecer roto un trámite que simplemente va por la primera etapa.
            return consultas is null
                ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(consultas);
        });

        // HU #11819 — datos recibidos, agrupados por secciones de negocio.
        group.MapGet("/tramites/{numero:long}/datos", async (
            HttpContext context,
            IDatosTramiteQuery query,
            long numero,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var datos = await query.ConsultarAsync(
                numero, access.IsSuperAdmin ? null : access.TenantId, ct);

            return datos is null
                ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(datos);
        });

        // HU #11819 — log HTTP acotado a las peticiones que tocan a este trámite.
        group.MapGet("/tramites/{numero:long}/log", async (
            HttpContext context,
            ILogTramiteQuery query,
            long numero,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess)
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var eventos = await query.ConsultarAsync(
                numero, access.IsSuperAdmin ? null : access.TenantId, ct);

            return eventos is null
                ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(eventos);
        });

        // HU #11820 — revelado auditado de datos personales. ÚNICO endpoint del módulo que escribe:
        // deja constancia de quién los pidió antes de entregarlos. Es POST y no GET a propósito: no
        // es una consulta idempotente, tiene efecto (el registro de auditoría) y no debe quedar en
        // el historial del navegador ni en una caché intermedia.
        group.MapPost("/tramites/{numero:long}/datos/revelar", async (
            HttpContext context,
            IRevelarDatosPersonalesQuery query,
            long numero,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasIctLogsAccess || !access.HasPiiRevealAccess)
            {
                // Se exige el permiso PROPIO de revelado además del del módulo. Sin ambos no se
                // entrega nada y no queda registro: no hay dato que auditar.
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var revelados = await query.RevelarAsync(
                numero,
                access.IsSuperAdmin ? null : access.TenantId,
                new SolicitanteRevelado(access.Subject, access.Role),
                ct);

            return revelados is null
                ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(revelados);
        });

        return app;
    }

    /// <summary>
    /// Las fechas llegan del selector del navegador como día suelto (<c>2026-08-24</c>) y sin zona.
    /// Se interpretan en UTC y el «hasta» se estira al final del día: sin esto, filtrar «hasta hoy»
    /// deja fuera todo lo ocurrido hoy, que es justo lo que el analista busca.
    /// </summary>
    private static DateTime? NormalizarDesde(DateTime? valor) =>
        valor is null ? null : DateTime.SpecifyKind(valor.Value, DateTimeKind.Utc);

    private static DateTime? NormalizarHasta(DateTime? valor)
    {
        if (valor is null)
        {
            return null;
        }

        var fecha = DateTime.SpecifyKind(valor.Value, DateTimeKind.Utc);
        return fecha.TimeOfDay == TimeSpan.Zero
            ? fecha.AddDays(1).AddTicks(-1)
            : fecha;
    }
}
