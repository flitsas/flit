namespace Flit.Admin.Application.OtClientProcedures.GetOtBandejaHealth;

/// <summary>Consulta de diagnóstico de la bandeja del OT (HU #10540 / R09).</summary>
public sealed class GetOtBandejaHealthQuery
{
    public Guid OtTenantId { get; init; }

    /// <summary>Override de organismo para SuperAdmin (mismo contrato que el listado).</summary>
    public Guid? TransitOfficeId { get; init; }
}

/// <summary>
/// Resultado del diagnóstico: si el tenant no resuelve un organismo, <see cref="TransitOfficeResolved"/>
/// es <c>false</c> y los conteos quedan en cero.
/// </summary>
public sealed class GetOtBandejaHealthResult
{
    public bool TransitOfficeResolved { get; init; }

    public Guid? TransitOfficeId { get; init; }

    public int DeliveredTotal { get; init; }

    public int DeliveredWithGrant { get; init; }

    public int DeliveredWithoutGrant { get; init; }

    /// <summary>Hay trámites entregados que la bandeja no muestra por falta de grant (dispara el banner).</summary>
    public bool HasDeliveredWithoutGrant => DeliveredWithoutGrant > 0;
}
