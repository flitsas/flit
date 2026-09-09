namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Contrato interno de CUATRO estados (CF-21) de <c>admin.standalone_documents.status</c>. La UI
/// colapsa <see cref="Pending"/> y <see cref="Processing"/> en «En proceso»; esa traducción es del
/// frontend (<c>status-labels.ts</c>) y no de esta capa.
/// <para>La generación individual solo puede RESPONDER <see cref="Generated"/> o
/// <see cref="Error"/>: los otros dos son estados de tránsito de la fila.</para>
/// </summary>
public static class StandaloneDocumentStatus
{
    public const string Pending = "pending";

    public const string Processing = "processing";

    public const string Generated = "generated";

    public const string Error = "error";
}
