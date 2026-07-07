namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Expone al OPERADOR el proveedor primario de consulta resuelto para su tenant, por tipo (HU #10478).
/// El wizard lo usa para adaptar la UI: p. ej. en traspaso, si el proveedor de placa es Kyverum RUNT no
/// pide el tipo de documento del propietario (Kyverum lo resuelve solo y lo devuelve en la respuesta),
/// mientras que con Verifik sí lo necesita. Solo devuelve la clave del PRIMARIO de la cadena (no el
/// fallback), que es lo único que la UI necesita para decidir qué campos mostrar.
/// </summary>
public sealed class GetConsultationConfigHandler(
    IConsultationProviderChainResolver chainResolver,
    IConsultationTenantOverrideProvider overrideProvider)
{
    public async Task<ConsultationConfigResult> HandleAsync(Guid tenantId, CancellationToken ct)
    {
        var tenantOverride = await overrideProvider.GetAsync(tenantId, ct);

        return new ConsultationConfigResult(
            PrimaryFor(ConsultationKind.VehicleVin, tenantOverride),
            PrimaryFor(ConsultationKind.VehiclePlate, tenantOverride),
            PrimaryFor(ConsultationKind.Conductor, tenantOverride));
    }

    private string PrimaryFor(ConsultationKind kind, ConsultationTenantOverride? tenantOverride)
    {
        var chain = chainResolver.ResolveChain(kind, tenantOverride);
        return chain.Count > 0 ? chain[0] : string.Empty;
    }
}

/// <summary>Proveedor primario de consulta por tipo, resuelto para el tenant (claves como en la cadena).</summary>
public sealed record ConsultationConfigResult(string VehicleVin, string VehiclePlate, string Conductor);
