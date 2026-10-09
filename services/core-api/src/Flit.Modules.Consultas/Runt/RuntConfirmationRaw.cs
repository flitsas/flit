namespace Flit.Tramites.Domain.RuntConfirmation
{
    /// <summary>Proveedores admitidos para la corrida. Mismas keys que los proveedores de consulta del wizard.</summary>
    public static class RuntConfirmationProviderKeys
    {
        public const string Kyverum = "kyverum_runt";
        public const string Verifik = "verifik";

        public static readonly IReadOnlyList<string> All = [Kyverum, Verifik];

        public static bool IsValid(string? value) =>
            value is not null && All.Contains(value, StringComparer.Ordinal);
    }
}

namespace Flit.Tramites.Application.UseCases.RuntConfirmation
{
    // HU #13348 (Epic #13316): el contrato del RUNT crudo de la Confirmación RUNT pasa al módulo de consultas (su
    // adaptador lo atiende core-consultas); conserva su espacio de nombres.
    public sealed record RuntDocument(string Type, string Number);

    /// <summary>Cómo se consulta al proveedor. Exactamente una de las dos formas.</summary>
    public sealed record RuntRawQuery(string? Vin, string? Plate, RuntDocument? Document)
    {
        public static RuntRawQuery ByVin(string vin) => new(vin, null, null);
        public static RuntRawQuery ByPlate(string plate, RuntDocument document) => new(null, plate, document);
        public string SubjectKey => Vin ?? Plate ?? string.Empty;

        /// <summary>Empresa del trámite: la consulta se mide por empresa en core-consultas (HU #13348).</summary>
        public Guid? TenantId { get; init; }
    }

    public enum RuntRawOutcome
    {
        /// <summary>El proveedor devolvió el vehículo (crudo completo).</summary>
        Found,

        /// <summary>El proveedor dijo que no existe (o que el documento no es del propietario). Hay crudo (real o sintético).</summary>
        NotFound,

        /// <summary>Timeout, 5xx, red, respuesta ilegible: NO es veredicto, no consume intento.</summary>
        Error,
    }

    public sealed record RuntRawQueryResult(RuntRawOutcome Outcome, string? RawJson, string? Message)
    {
        public static RuntRawQueryResult Error(string message) => new(RuntRawOutcome.Error, null, message);
    }

    /// <summary>
    /// Consumidor PROPIO del cliente RUNT según el proveedor configurado. No pasa por
    /// <c>IConsultationProviderChainResolver</c> ni por el override por tenant: la Confirmación RUNT
    /// elige su proveedor en su configuración global, no en la de la empresa.
    /// </summary>
    public interface IRuntVehicleRawClient
    {
        Task<RuntRawQueryResult> ConsultAsync(string providerKey, RuntRawQuery query, CancellationToken ct = default);
    }
}
