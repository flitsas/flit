using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Tipos de adjunto del certificado de identidad Kyverum (cascada FUR → consolidado).
/// <para>
/// Un propietario por lado conserva los códigos históricos (<c>certificado_identidad</c>,
/// <c>certificado_identidad_vendedor</c>). Con 2..4 copropietarios (ADR-0053) el ordinal &gt; 1
/// añade sufijo <c>_{ordinal}</c> para que coexistán en el expediente y
/// <see cref="GenerarConsolidadoHandler.SanitizeConsolidadoParts"/> no los colapse.
/// </para>
/// </summary>
public static class IdentityCertificateAttachmentTipo
{
    public const string CompradorBase = "certificado_identidad";
    public const string Prefijo = "certificado_identidad";

    /// <summary>Tipo de adjunto para el certificado del actor en <paramref name="role"/>.</summary>
    /// <param name="role">Parte del trámite (<c>comprador</c>, <c>vendedor</c>, …).</param>
    /// <param name="ordinal">1 = principal (sin sufijo numérico); 2..4 = copropietario.</param>
    public static string For(string role, int ordinal)
    {
        var baseTipo = BaseForRole(role);
        var n = ordinal < 1 ? 1 : ordinal;
        return n <= 1 ? baseTipo : $"{baseTipo}_{n}";
    }

    /// <summary>Tipo base histórico del rol (sin sufijo de ordinal).</summary>
    public static string BaseForRole(string role) =>
        string.Equals(role, BiometricRules.ParteComprador, StringComparison.OrdinalIgnoreCase)
            ? CompradorBase
            : $"{Prefijo}_{role.Trim().ToLowerInvariant()}";

    public static bool IsIdentityCertificate(string? tipo) =>
        !string.IsNullOrWhiteSpace(tipo)
        && tipo.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Familia de prelación del consolidado: vendedor (y sufijos) vs comprador/otros.
    /// Devuelve el código base que aparece en las listas de orden.
    /// </summary>
    public static string RankKey(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
            return CompradorBase;

        var t = tipo.Trim();
        var vendedorBase = BaseForRole(BiometricRules.ParteVendedor);
        if (t.StartsWith(vendedorBase, StringComparison.OrdinalIgnoreCase))
            return vendedorBase;

        if (t.StartsWith(CompradorBase, StringComparison.OrdinalIgnoreCase))
            return CompradorBase;

        return t;
    }

    /// <summary>Ordinal embebido en el tipo (<c>…_2</c> → 2); base histórica → 1.</summary>
    public static int OrdinalFromTipo(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
            return 1;

        var t = tipo.Trim();
        var underscore = t.LastIndexOf('_');
        if (underscore < 0 || underscore == t.Length - 1)
            return 1;

        var suffix = t[(underscore + 1)..];
        return int.TryParse(suffix, out var n) && n >= 1 ? n : 1;
    }
}
