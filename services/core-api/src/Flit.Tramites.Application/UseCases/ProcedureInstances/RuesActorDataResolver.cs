using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #10990 (Feature #10972) — resuelve los datos del RUES de un NIT en el momento de generar el
/// expediente, cuando <c>field_values</c> no los tiene para ESE actor.
/// <para>Invierte la dependencia que rompía el certificado: hasta ahora dependía de que el wizard
/// hubiera alcanzado a consultar, y eso fallaba por tres vías (la precarga del directorio de
/// representantes legales cortaba la consulta, fuera de borrador no se persistía, y las llaves
/// <c>rues_*</c> son de instancia y no de actor).</para>
/// </summary>
public interface IRuesActorDataResolver
{
    /// <summary>
    /// Datos del RUES para <paramref name="nit"/>, o <c>null</c> si no se pudieron obtener.
    /// NUNCA lanza: un fallo del proveedor no puede tumbar la generación del expediente.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>?> ResolveAsync(
        Guid instanceId, Guid tenantId, string nit, CancellationToken ct = default);
}

/// <summary>Resolutor nulo: nunca resuelve. Default seguro para los tests que no lo ejercitan.</summary>
public sealed class NullRuesActorDataResolver : IRuesActorDataResolver
{
    public static readonly NullRuesActorDataResolver Instance = new();

    public Task<IReadOnlyDictionary<string, string?>?> ResolveAsync(
        Guid instanceId, Guid tenantId, string nit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string?>?>(null);
}

/// <summary>
/// Implementación productiva: consulta EN VIVO al proveedor <c>verifik_rues</c>.
/// <para><b>Deliberadamente NO lee la caché de reúso.</b> HU #10955 estableció que el estado en RUES
/// de una persona jurídica se consulta siempre en vivo, y un certificado no debe dar fe de un payload
/// cacheado. Sí se ESCRIBE la caché con el resultado fresco, para no romper a los demás consumidores
/// de ese mecanismo.</para>
/// <para>El costo está acotado porque este resolutor es el <i>segundo</i> intento: el camino normal
/// usa las <c>rues_*</c> que el wizard ya persistió (ver <c>GenerarFurHandler</c>).</para>
/// </summary>
public sealed class RuesActorDataResolver(
    IConsultationProviderRegistry registry,
    ExternalQueryCacheService cacheService,
    ILogger<RuesActorDataResolver> logger) : IRuesActorDataResolver
{
    private const string ProviderKey = "verifik_rues";
    private const string SourceCode = "RUES";
    private const string DocumentTypeNit = "NIT";
    private const string TemplateCode = "RUES_ACTOR_JURIDICAL";

    public async Task<IReadOnlyDictionary<string, string?>?> ResolveAsync(
        Guid instanceId, Guid tenantId, string nit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nit))
            return null;

        var provider = registry.Resolve(ProviderKey);
        if (provider is null)
            return null;

        try
        {
            var ctx = new ConsultationContext(
                instanceId,
                tenantId,
                TemplateCode,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nit"] = nit,
                    ["documentNumber"] = nit,
                });

            var result = await provider.ConsultAsync(ctx, ct);
            if (result.HydratedFields.Count == 0)
                return null;

            // Best-effort: refrescar la caché beneficia a otros consumidores, pero si falla no puede
            // costarnos el certificado que ya tenemos resuelto.
            try
            {
                await cacheService.SavePersonResultAsync(
                    tenantId, SourceCode, DocumentTypeNit, nit, instanceId,
                    result.HydratedFields, DateTimeOffset.UtcNow, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RuesResolverLog.CacheNoActualizada(logger, ex, instanceId);
            }

            return result.HydratedFields.ToDictionary(
                f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RuesResolverLog.ConsultaFallo(logger, ex, instanceId);
            return null;
        }
    }
}

/// <summary>Logging source-generated (CA1848). No incluye el NIT: es dato de la contraparte.</summary>
internal static partial class RuesResolverLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo consultar el RUES al generar el expediente (instancia {InstanceId}); se omite el certificado sin bloquear el FUR.")]
    public static partial void ConsultaFallo(ILogger logger, Exception ex, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo refrescar la caché del RUES (instancia {InstanceId}); el certificado se emite igual.")]
    public static partial void CacheNoActualizada(ILogger logger, Exception ex, Guid instanceId);
}
