using System.Text.Json;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ConfiguracionProto = Flit.Consultas.Grpc.V1.ConfiguracionEmpresa;

namespace Flit.Consultas.Api.Grpc;

/// <summary>
/// <c>flit.consultas.v1.ConsultasAdminService</c> (HU #13344): la configuración de consultas de cada empresa en el
/// esquema de Consultas. La escribe core-api con lo que guarda el SuperAdmin en la pantalla de siempre; aplica a la
/// siguiente consulta. Se guarda con los mismos JSON que <c>admin.tenant_operational_policies</c>.
/// </summary>
internal sealed class ConsultasAdminGrpcService(
    ConsultasDb db, TimeProvider time, Impuesto.CatalogoDepartamentosImpuesto departamentosImpuesto, Configuracion.ContingenciaRuntVigente contingencia)
    : ConsultasAdminService.ConsultasAdminServiceBase
{
    public const string Scope = "platform.consultas.admin";

    /// <summary>Largo máximo del motivo de un cambio de contingencia.</summary>
    internal const int MotivoMaximo = 500;

    /// <summary>Único cliente de servicio que puede escribir la contingencia (core-api).</summary>
    internal const string ClienteQueGuardaContingencia = "svc-tramites";

    /// <summary>
    /// Épica #14644 (HU #14648): los tres tipos con su contingencia, siempre en el mismo orden. Global: no usa la empresa.
    /// </summary>
    public override async Task<ObtenerContingenciaRuntResponse> ObtenerContingenciaRunt(ObtenerContingenciaRuntRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var filas = await db.ContingenciasRunt.AsNoTracking().ToListAsync(context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ObtenerContingenciaRuntResponse();
        foreach (var tipo in Configuracion.ContingenciaRuntReglas.Tipos)
            respuesta.Tipos.Add(AProto(tipo, filas.FirstOrDefault(f => f.Kind == tipo)));
        return respuesta;
    }

    /// <summary>
    /// Activa, cambia o apaga (proveedor vacío) la contingencia de un tipo. El tipo, el proveedor de ese tipo y el motivo se
    /// validan aquí (<c>InvalidArgument</c>) y, si algo falla, nada cambia. No toca la configuración de ninguna empresa.
    /// </summary>
    public override async Task<GuardarContingenciaRuntResponse> GuardarContingenciaRunt(GuardarContingenciaRuntRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        // Defensa en profundidad: solo core-api (svc-tramites, que atiende al Super Admin FLIT) puede cambiar la contingencia.
        if (!string.Equals(PlatformServiceCaller.From(context).ClientId, ClienteQueGuardaContingencia, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Solo core-api puede cambiar la contingencia de proveedor RUNT."));
        var ct = context.CancellationToken;

        var tipo = request.Tipo?.Trim() ?? string.Empty;
        if (!Configuracion.ContingenciaRuntReglas.EsTipo(tipo))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Tipo de consulta desconocido: {tipo}."));

        var proveedor = request.Proveedor?.Trim() ?? string.Empty;
        if (proveedor.Length > 0 && !Configuracion.ContingenciaRuntReglas.ProveedoresDe(tipo).Contains(proveedor, StringComparer.Ordinal))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Ese proveedor no atiende este tipo de consulta."));

        var motivo = request.Motivo?.Trim() ?? string.Empty;
        if (motivo.Length is 0 or > MotivoMaximo)
            throw new RpcException(new Status(StatusCode.InvalidArgument, motivo.Length == 0 ? "Escribe el motivo del cambio." : $"El motivo admite hasta {MotivoMaximo} caracteres."));

        var nombre = request.ActualizadoPor?.Trim() ?? string.Empty;
        if (nombre.Length > 200)
            nombre = nombre[..200];

        // Upsert atómico: dos primeras escrituras concurrentes del mismo tipo no chocan con el índice único (la última gana).
        var ahora = time.GetUtcNow();
        var proveedorFinal = proveedor.Length == 0 ? null : proveedor;
        Guid? actor = Guid.TryParse(request.ActualizadoPorId, out var actorId) ? actorId : null;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO consultas.runt_contingencies (id, kind, provider, reason, updated_by_name, updated_by_id, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {tipo}, {proveedorFinal}::varchar, {motivo}, {nombre}, {actor}::uuid, {ahora}, {ahora})
            ON CONFLICT (kind) DO UPDATE
               SET provider = EXCLUDED.provider, reason = EXCLUDED.reason, updated_by_name = EXCLUDED.updated_by_name,
                   updated_by_id = EXCLUDED.updated_by_id, updated_at = EXCLUDED.updated_at
            """, ct).ConfigureAwait(false);

        contingencia.Invalidar();
        var fila = await db.ContingenciasRunt.AsNoTracking().SingleAsync(c => c.Kind == tipo, ct).ConfigureAwait(false);
        return new GuardarContingenciaRuntResponse { Contingencia = AProto(tipo, fila) };
    }

    private static ContingenciaRunt AProto(string tipo, Persistence.ContingenciaProveedorRunt? fila) => new()
    {
        Tipo = tipo,
        Proveedor = fila?.Provider ?? string.Empty,
        Motivo = fila?.Reason ?? string.Empty,
        ActualizadoPor = fila?.UpdatedByName ?? string.Empty,
        ActualizadoEn = fila is null ? null : Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(fila.UpdatedAt),
    };

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record SeleccionJson(string Primary, List<string> Fallback);

    private sealed record AvaluosJson(string Primary, List<string> Enabled);

    public override async Task<GuardarConfiguracionEmpresaResponse> GuardarConfiguracionEmpresa(GuardarConfiguracionEmpresaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = PlatformServiceCaller.From(context).TenantId;
        var config = request.Configuracion ?? new ConfiguracionProto();

        var fila = await db.ConfiguracionEmpresas.FirstOrDefaultAsync(c => c.TenantId == tenantId, context.CancellationToken).ConfigureAwait(false);
        if (fila is null)
        {
            fila = new Persistence.ConfiguracionEmpresa { TenantId = tenantId };
            db.ConfiguracionEmpresas.Add(fila);
        }

        fila.CadenasJson = config.Cadenas.Count == 0
            ? null
            : JsonSerializer.Serialize(
                config.Cadenas.ToDictionary(c => c.Key, c => new SeleccionJson(c.Value.Principal, [.. c.Value.Respaldo])), WebJson);
        fila.FailoverTimeoutMs = config.FailoverTimeoutMs > 0 ? config.FailoverTimeoutMs : null;
        fila.FuenteMultas = FinesSourceCodes.Normalize(config.FuenteMultas);
        fila.AvaluosJson = config.AvaluosHabilitados.Count == 0
            ? null
            : JsonSerializer.Serialize(new AvaluosJson(
                string.IsNullOrWhiteSpace(config.AvaluoPrincipal) ? "fasecolda" : config.AvaluoPrincipal, [.. config.AvaluosHabilitados]), WebJson);
        fila.ActualizadoEn = time.GetUtcNow();

        await db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        return new GuardarConfiguracionEmpresaResponse();
    }

    public override async Task<ObtenerConfiguracionEmpresaResponse> ObtenerConfiguracionEmpresa(ObtenerConfiguracionEmpresaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = PlatformServiceCaller.From(context).TenantId;
        var fila = await db.ConfiguracionEmpresas.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, context.CancellationToken).ConfigureAwait(false);

        var config = new ConfiguracionProto { FuenteMultas = fila?.FuenteMultas ?? FinesSourceCodes.External, FailoverTimeoutMs = fila?.FailoverTimeoutMs ?? 0 };
        if (!string.IsNullOrWhiteSpace(fila?.CadenasJson)
            && JsonSerializer.Deserialize<Dictionary<string, SeleccionJson>>(fila.CadenasJson, WebJson) is { } cadenas)
        {
            foreach (var (tipo, seleccion) in cadenas)
            {
                var cadena = new CadenaProveedores { Principal = seleccion.Primary };
                cadena.Respaldo.AddRange(seleccion.Fallback ?? []);
                config.Cadenas[tipo] = cadena;
            }
        }

        if (!string.IsNullOrWhiteSpace(fila?.AvaluosJson)
            && JsonSerializer.Deserialize<AvaluosJson>(fila.AvaluosJson, WebJson) is { } avaluos)
        {
            config.AvaluoPrincipal = avaluos.Primary;
            config.AvaluosHabilitados.AddRange(avaluos.Enabled ?? []);
        }

        return new ObtenerConfiguracionEmpresaResponse { Configuracion = config };
    }

    public override async Task<ObtenerConsumoResponse> ObtenerConsumo(ObtenerConsumoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (request.Desde is null || request.Hasta is null || request.Hasta.ToDateTimeOffset() <= request.Desde.ToDateTimeOffset())
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Se necesita un rango de fechas válido (desde < hasta)."));

        var tenantId = PlatformServiceCaller.From(context).TenantId;
        var desde = request.Desde.ToDateTimeOffset();
        var hasta = request.Hasta.ToDateTimeOffset();
        var filas = await db.Consumos.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.OcurridoEn >= desde && c.OcurridoEn < hasta)
            .GroupBy(c => new { c.Producto, c.Fuente })
            .Select(g => new
            {
                g.Key.Producto,
                g.Key.Fuente,
                Total = g.LongCount(),
                DesdeCache = g.LongCount(c => c.DesdeCache),
                Errores = g.LongCount(c => c.Resultado == "error"),
                Latencia = g.Average(c => (double)c.LatenciaMs),
            })
            .OrderBy(x => x.Producto).ThenBy(x => x.Fuente)
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);

        var response = new ObtenerConsumoResponse();
        response.Consumos.AddRange(filas.Select(f => new ConsumoAgregado
        {
            Producto = f.Producto,
            Fuente = f.Fuente,
            Total = f.Total,
            DesdeCache = f.DesdeCache,
            Errores = f.Errores,
            LatenciaPromedioMs = (long)Math.Round(f.Latencia),
        }));
        return response;
    }

    public override async Task<ListarCatalogoDepartamentosImpuestoResponse> ListarCatalogoDepartamentosImpuesto(
        ListarCatalogoDepartamentosImpuestoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = PlatformServiceCaller.From(context);
        var filas = await departamentosImpuesto.ListarTodosAsync(context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ListarCatalogoDepartamentosImpuestoResponse();
        respuesta.Departamentos.AddRange(filas.Select(AProto));
        return respuesta;
    }

    public override async Task<GuardarDepartamentoImpuestoResponse> GuardarDepartamentoImpuesto(
        GuardarDepartamentoImpuestoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _ = PlatformServiceCaller.From(context);
        try
        {
            var fila = await departamentosImpuesto.GuardarAsync(request.Codigo, request.Nombre, request.Activo, context.CancellationToken).ConfigureAwait(false);
            return new GuardarDepartamentoImpuestoResponse { Departamento = AProto(fila) };
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    private static DepartamentoImpuestoCatalogo AProto(Impuesto.DepartamentoHabilitado fila) => new()
    {
        Codigo = fila.Codigo,
        Nombre = fila.Nombre,
        Activo = fila.Activo,
    };
}
