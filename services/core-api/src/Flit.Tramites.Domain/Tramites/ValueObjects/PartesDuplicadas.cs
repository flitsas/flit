namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Resultado de comparar dos partes (paridad <c>partesTraspasoDuplicadas</c> de Johan).</summary>
public sealed record PartesDuplicadas(bool MismoDocumento, bool MismoEmail);

/// <summary>Fila mínima de validación biométrica para resolver la vigente por documento.</summary>
public sealed record ValidacionRow(
    long Id,
    string? Documento,
    string? Parte,
    string Estado);
