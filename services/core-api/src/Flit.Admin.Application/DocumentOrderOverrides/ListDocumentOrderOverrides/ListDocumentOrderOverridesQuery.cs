namespace Flit.Admin.Application.DocumentOrderOverrides.ListDocumentOrderOverrides;

/// <summary>
/// Petición del listado de overrides de una combinación trámite/scope/referencia
/// (HU #10196, AC5). El endpoint resuelve <see cref="ScopeRefId"/> desde
/// <c>transitOfficeId</c> o <c>clienteId</c> según el scope.
/// </summary>
public sealed class ListDocumentOrderOverridesQuery
{
    public required Guid ProcedureTypeId { get; init; }

    /// <summary>Ámbito normalizado (<c>OT</c> | <c>CLIENTE</c>).</summary>
    public required string Scope { get; init; }

    /// <summary>Referencia del ámbito (transitOfficeId o clienteId).</summary>
    public required Guid ScopeRefId { get; init; }
}
