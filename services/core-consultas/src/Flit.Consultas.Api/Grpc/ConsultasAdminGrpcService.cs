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
internal sealed class ConsultasAdminGrpcService(ConsultasDb db, TimeProvider time) : ConsultasAdminService.ConsultasAdminServiceBase
{
    public const string Scope = "platform.consultas.admin";

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
}
