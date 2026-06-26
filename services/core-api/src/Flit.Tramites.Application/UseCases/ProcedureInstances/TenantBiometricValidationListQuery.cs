using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Parámetros de query string de <c>GET /api/v1/tramites/biometric-validations</c> (HU #10347).
/// Todos los filtros son opcionales.
/// </summary>
public sealed record TenantBiometricValidationListQuery(
    string? ReferenceNumber = null,
    string? Modalidad = null,
    string? Nombre = null,
    string? Parte = null,
    string? TipoDoc = null,
    string? Documento = null,
    string? Estado = null,
    string? Provider = null,
    int? ScoreMin = null,
    int? ScoreMax = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? MotivoRechazo = null)
{
    private static readonly HashSet<string> ValidEstados = new(StringComparer.OrdinalIgnoreCase)
    {
        BiometricEstados.Enviado,
        BiometricEstados.EnProceso,
        BiometricEstados.Aprobado,
        BiometricEstados.Rechazado,
        BiometricEstados.Expirado,
    };

    private static readonly HashSet<string> ValidPartes = new(StringComparer.OrdinalIgnoreCase)
    {
        BiometricRules.ParteComprador,
        BiometricRules.ParteVendedor,
    };

    private static readonly HashSet<string> ValidProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        BiometricProviders.Mock,
        BiometricProviders.Kyverum,
    };

    /// <summary>
    /// Valida los parámetros de filtrado. Devuelve un mensaje de error descriptivo (sin PII) o null si es válido.
    /// </summary>
    public string? Validate()
    {
        if (ScoreMin is { } min && ScoreMax is { } max && min > max)
            return "scoreMin no puede ser mayor que scoreMax.";

        if (!string.IsNullOrWhiteSpace(Estado) && !ValidEstados.Contains(Estado.Trim()))
            return "estado inválido; use enviado, en_proceso, aprobado, rechazado o expirado.";

        if (!string.IsNullOrWhiteSpace(Parte) && !ValidPartes.Contains(Parte.Trim()))
            return "parte inválida; use comprador o vendedor.";

        if (!string.IsNullOrWhiteSpace(Provider) && !ValidProviders.Contains(Provider.Trim()))
            return "provider inválido; use mock o kyverum.";

        if (CreatedFrom is { } from && CreatedTo is { } to && from > to)
            return "createdFrom no puede ser posterior a createdTo.";

        return null;
    }

    /// <summary>Mapea a <see cref="BiometricValidationListFilter"/> para el repositorio.</summary>
    public BiometricValidationListFilter ToFilter() => new()
    {
        ReferenceNumber = Trim(ReferenceNumber),
        Modalidad = Trim(Modalidad),
        Nombre = Trim(Nombre),
        Parte = Trim(Parte),
        TipoDoc = Trim(TipoDoc),
        Documento = Trim(Documento),
        Estado = Trim(Estado),
        Provider = Trim(Provider),
        ScoreMin = ScoreMin,
        ScoreMax = ScoreMax,
        CreatedFrom = CreatedFrom,
        CreatedTo = CreatedTo,
        MotivoRechazo = Trim(MotivoRechazo),
    };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
