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
            PrimaryFor(ConsultationKind.Conductor, tenantOverride),
            // FEATURE 02 — wizard de traspaso (legado): flag TRASPASO.
            tenantOverride?.OnlyOwnVehicles ?? false,
            new OnlyOwnVehiclesByFamilyConfig(
                tenantOverride?.OnlyOwnVehiclesMatriculas ?? false,
                tenantOverride?.OnlyOwnVehicles ?? false,
                tenantOverride?.OnlyOwnVehiclesOtros ?? false),
            new BlockProcedureFamilyConfig(
                tenantOverride?.BlockProcedureFamilyMatriculas ?? false,
                tenantOverride?.BlockProcedureFamilyTraspaso ?? false,
                tenantOverride?.BlockProcedureFamilyOtros ?? false));
    }

    private string PrimaryFor(ConsultationKind kind, ConsultationTenantOverride? tenantOverride)
    {
        var chain = chainResolver.ResolveChain(kind, tenantOverride);
        return chain.Count > 0 ? chain[0] : string.Empty;
    }
}

/// <summary>Solo vehículos propios por familia de trámite.</summary>
public sealed record OnlyOwnVehiclesByFamilyConfig(bool Matriculas, bool Traspaso, bool Otros);

/// <summary>Bloqueo de creación por familia (<c>true</c> = no permitir crear).</summary>
public sealed record BlockProcedureFamilyConfig(bool Matriculas, bool Traspaso, bool Otros);

/// <summary>
/// Proveedor primario de consulta por tipo, resuelto para el tenant (claves como en la cadena), más
/// flags de radicación por familia (solo vehículos propios + bloqueo de creación).
/// </summary>
public sealed record ConsultationConfigResult(
    string VehicleVin,
    string VehiclePlate,
    string Conductor,
    bool OnlyOwnVehicles,
    OnlyOwnVehiclesByFamilyConfig? OnlyOwnVehiclesByFamily = null,
    BlockProcedureFamilyConfig? BlockProcedureFamily = null);
