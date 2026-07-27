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
    string? Name = null,
    string? PartyRole = null,
    string? DocumentType = null,
    string? DocumentNumber = null,
    string? Status = null,
    string? Provider = null,
    int? ScoreMin = null,
    int? ScoreMax = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? MotivoRechazo = null,
    string? VigenciaEstado = null,
    DateTimeOffset? ExpiraDesde = null,
    DateTimeOffset? ExpiraHasta = null,
    int? VenceEnDias = null,
    int Page = 1,
    int PageSize = 20,
    /// <summary>HU #10867 — true = solo standalone (sin trámite); false = solo ligadas; null = todas.</summary>
    bool? Standalone = null)
{
    /// <summary>Filas por página: el cliente elige de 10 en 10 hasta 50 (HU #10347 — paginación).</summary>
    public const int MinPageSize = 10;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;

    /// <summary>Página normalizada (1-based, mínimo 1).</summary>
    public int SafePage() => Page < 1 ? 1 : Page;

    /// <summary>Tamaño de página acotado al rango permitido [10, 50].</summary>
    public int SafePageSize() => Math.Clamp(PageSize, MinPageSize, MaxPageSize);

    private static readonly HashSet<string> ValidEstados = new(StringComparer.OrdinalIgnoreCase)
    {
        BiometricEstados.Enviado,
        BiometricEstados.EnProceso,
        BiometricEstados.Aprobado,
        BiometricEstados.Rechazado,
        BiometricEstados.Expirado,
        BiometricEstados.PendienteEnvio,
        BiometricEstados.ErrorEnvio,
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

    private static readonly HashSet<string> ValidVigenciaEstados = new(StringComparer.OrdinalIgnoreCase)
    {
        BiometricVigenciaEstados.Vigente,
        BiometricVigenciaEstados.PorVencer,
        BiometricVigenciaEstados.Vencida,
    };

    /// <summary>
    /// Valida los parámetros de filtrado. Devuelve un mensaje de error descriptivo (sin PII) o null si es válido.
    /// </summary>
    public string? Validate()
    {
        if (ScoreMin is { } min && ScoreMax is { } max && min > max)
            return "scoreMin no puede ser mayor que scoreMax.";

        if (!string.IsNullOrWhiteSpace(Status) && !ValidEstados.Contains(Status.Trim()))
            return "estado inválido; use enviado, en_proceso, aprobado, rechazado, expirado, pendiente_envio o error_envio.";

        if (!string.IsNullOrWhiteSpace(PartyRole) && !ValidPartes.Contains(PartyRole.Trim()))
            return "parte inválida; use comprador o vendedor.";

        if (!string.IsNullOrWhiteSpace(Provider) && !ValidProviders.Contains(Provider.Trim()))
            return "provider inválido; use mock o kyverum.";

        if (CreatedFrom is { } from && CreatedTo is { } to && from > to)
            return "createdFrom no puede ser posterior a createdTo.";

        if (!string.IsNullOrWhiteSpace(VigenciaEstado) && !ValidVigenciaEstados.Contains(VigenciaEstado.Trim()))
            return "vigenciaEstado inválido; use vigente, por_vencer o vencida.";

        if (ExpiraDesde is { } expDesde && ExpiraHasta is { } expHasta && expDesde > expHasta)
            return "expiraDesde no puede ser posterior a expiraHasta.";

        if (VenceEnDias is { } venceEn && venceEn < 0)
            return "venceEnDias no puede ser negativo.";

        return null;
    }

    /// <summary>Tope de longitud de los términos de búsqueda de texto libre (saneo anti-abuso).</summary>
    public const int TextFilterMaxLength = 100;

    /// <summary>Mapea a <see cref="BiometricValidationListFilter"/> para el repositorio.</summary>
    /// <remarks>
    /// Los campos de texto libre (búsqueda) solo se SANEAN: trim + tope de longitud. No se valida
    /// el conjunto de caracteres (no se bloquea al usuario por buscar parcial); los comodines LIKE
    /// (<c>%</c>, <c>_</c>) se escapan en el repositorio para que se traten como literales.
    /// </remarks>
    public BiometricValidationListFilter ToFilter() => new()
    {
        ReferenceNumber = TrimCap(ReferenceNumber),
        Modalidad = TrimCap(Modalidad),
        Name = TrimCap(Name),
        PartyRole = Trim(PartyRole),
        DocumentType = TrimCap(DocumentType),
        DocumentNumber = TrimCap(DocumentNumber),
        Status = Trim(Status),
        Provider = Trim(Provider),
        ScoreMin = ScoreMin,
        ScoreMax = ScoreMax,
        CreatedFrom = CreatedFrom,
        CreatedTo = CreatedTo,
        MotivoRechazo = TrimCap(MotivoRechazo),
        VigenciaEstado = Trim(VigenciaEstado)?.ToLowerInvariant(),
        ExpiraDesde = ExpiraDesde,
        ExpiraHasta = ExpiraHasta,
        VenceEnDias = VenceEnDias,
        Standalone = Standalone,
    };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trim + tope de longitud para términos de búsqueda de texto libre.</summary>
    private static string? TrimCap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > TextFilterMaxLength ? trimmed[..TextFilterMaxLength] : trimmed;
    }
}
