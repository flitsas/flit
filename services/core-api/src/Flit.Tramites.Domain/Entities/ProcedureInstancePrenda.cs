namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Decisión de prenda (gravamen) de un trámite — cimiento IT-3 (Feature #10585). Agregado compañero de la
/// instancia con versionado por <c>Estado</c> (<c>vigente</c>/<c>reemplazada</c>): a lo sumo una fila vigente
/// por instancia. Vive en su propia tabla, por lo que NO está sujeta a la inmutabilidad de <c>field_values</c>
/// (permite modificar la elección post-registro, R17, sin relajar esa barrera).
/// </summary>
public sealed class ProcedureInstancePrenda
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Decisión (<see cref="Tramites.ValueObjects.PrendaDecision"/>).</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>Estado de versionado (<see cref="Tramites.ValueObjects.PrendaEstado"/>).</summary>
    public string Estado { get; set; } = Tramites.ValueObjects.PrendaEstado.Vigente;

    /// <summary>Beneficiario del gravamen (para reflejarlo en el FUR).</summary>
    public string? AcreedorNombre { get; set; }
    public string? AcreedorDocumento { get; set; }

    public string Metadata { get; set; } = "{}";
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
