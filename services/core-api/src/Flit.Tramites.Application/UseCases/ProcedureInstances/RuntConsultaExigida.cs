using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Qué actores del trámite tienen que pasar por el RUNT y qué documentos se consultaron de verdad.
///
/// <para>El wizard daba la consulta por hecha con solo tener el documento digitado. Con esto el gate
/// distingue "escribí una cédula" de "consulté esa cédula en el RUNT", y solo se lo exige a los
/// actores que el tipo de trámite marca con <c>requiresRunt</c> en su perfil de validación.</para>
/// </summary>
public sealed class RuntConsultaExigida
{
    private readonly HashSet<string> _actoresExigidos;
    private readonly IReadOnlySet<string> _documentosConsultados;

    public RuntConsultaExigida(
        IEnumerable<string> actoresExigidos,
        IReadOnlySet<string> documentosConsultados)
    {
        ArgumentNullException.ThrowIfNull(actoresExigidos);
        _actoresExigidos = actoresExigidos.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _documentosConsultados = documentosConsultados ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Nada exigido: el gate se comporta como antes de esta regla.</summary>
    public static RuntConsultaExigida Ninguna { get; } =
        new([], new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Códigos del catálogo de actores mapeados al <c>actor_type</c> que usa la instancia. El
    /// catálogo no tiene SELLER: quien vende es el propietario registrado.
    /// </summary>
    public static string? ActorTypeDeEntidad(string? entityCode) =>
        entityCode?.Trim().ToUpperInvariant() switch
        {
            "BUYER" => "comprador",
            "OWNER" => "vendedor",
            "LESSEE" => "locatario",
            _ => null,
        };

    public bool Exige(string actorType) => _actoresExigidos.Contains(actorType);

    public bool FueConsultado(string? documentType, string? documentNumber) =>
        _documentosConsultados.Contains(RuntPersonaConsultada.Key(documentType, documentNumber));

    /// <summary>¿Hay al menos un actor con la consulta exigida? Evita trabajo cuando no aplica.</summary>
    public bool Aplica => _actoresExigidos.Count > 0;
}
