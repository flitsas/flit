namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Decisión de prenda (gravamen) de un trámite — cimiento IT-3 (Feature #10585). Agregado compañero de la
/// instancia con versionado por <c>Estado</c> (<c>vigente</c>/<c>reemplazada</c>): a lo sumo una fila vigente
/// por instancia y por <see cref="AccionFamilia"/> — hasta dos vigentes simultáneas por instancia (una de
/// constitución y una de levantamiento) desde ADR-0055 (HU #12128); Matrícula/Traspaso siguen operando con
/// una sola vigente en la práctica porque no activan la acción complementaria. Vive en su propia tabla, por
/// lo que NO está sujeta a la inmutabilidad de <c>field_values</c> (permite modificar la elección
/// post-registro, R17, sin relajar esa barrera).
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

    /// <summary>
    /// Entidad u oficina ante la que se levantó el gravamen (notaría, oficina de registro…).
    /// <para>La declara el párrafo 23 del FUR en el trámite de LEVANTAMIENTO de prenda, donde el
    /// levantamiento ES el trámite. El RUNT no la trae: su detalle de gravamen se limita a acreedor,
    /// documento, fecha y estado. Queda vacía en traspaso y matrícula, que conservan su literal.</para>
    /// </summary>
    public string? LevantamientoEntidad { get; set; }

    /// <summary>
    /// Familia de acción del hecho, derivada de <see cref="Decision"/> (ADR-0055):
    /// <c>constitucion</c> (<c>solicitar</c>/<c>registrar</c>) o <c>levantamiento</c>
    /// (<c>levantar</c>); <c>null</c> en <c>omitir</c>/<c>sin_prenda</c> (no son hechos de gravamen).
    /// Sostiene el índice único parcial dual que permite hasta dos filas vigentes por instancia —
    /// una por familia — nunca dos de la misma.
    /// </summary>
    public string? AccionFamilia { get; set; }

    public string Metadata { get; set; } = "{}";
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
