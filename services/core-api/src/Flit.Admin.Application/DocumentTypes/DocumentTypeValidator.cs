using System.Text.RegularExpressions;
using Flit.Admin.Application.Common;

namespace Flit.Admin.Application.DocumentTypes;

/// <summary>
/// Validación de los campos persistibles de un tipo de documento (POST/PUT, HU #10193).
/// Devuelve el primer mensaje de error encontrado o <c>null</c> si el payload es válido.
/// El endpoint traduce un error a HTTP 422 <c>{ error: "..." }</c>.
/// </summary>
public static partial class DocumentTypeValidator
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 500;

    /// <summary>Códigos: letras, números y guiones (alfanumérico + guion).</summary>
    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex CodeRegex();

    /// <summary>Valida nombre y descripción ya recortados (el código lo genera el sistema).</summary>
    public static string? ValidateNameAndDescription(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "El nombre es obligatorio.";
        }

        if (name.Length > NameMaxLength)
        {
            return $"El nombre no puede superar {NameMaxLength} caracteres.";
        }

        if (TextFieldPatterns.ValidateReadableName(name, "El nombre") is { } nameError)
        {
            return nameError;
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            return $"La descripción no puede superar {DescriptionMaxLength} caracteres.";
        }

        if (description is { Length: > 0 } && !TextFieldPatterns.FreeTextNoAngleBrackets().IsMatch(description))
        {
            return "La descripción no permite los caracteres < ni >.";
        }

        return null;
    }

    /// <summary>
    /// Valida código, nombre y descripción ya recortados (trim). Asume que la capa
    /// de aplicación pasó cadenas vacías cuando el campo venía nulo/espacios.
    /// </summary>
    public static string? Validate(string code, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "El código es obligatorio.";
        }

        if (code.Length > CodeMaxLength)
        {
            return $"El código no puede superar {CodeMaxLength} caracteres.";
        }

        if (!CodeRegex().IsMatch(code))
        {
            return "El código solo permite letras, números y guiones.";
        }

        if (!TextFieldPatterns.HasLetterOrDigit().IsMatch(code))
        {
            return "El código debe contener al menos una letra o número.";
        }

        return ValidateNameAndDescription(name, description);
    }
}
