using System;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Tipo de persona del actor de un trámite (HU #10542). Vocabulario alineado con
/// <c>ConsultationTemplate.PersonType</c> y el CHECK de BD existente: <c>natural</c> | <c>juridical</c>.
/// </summary>
public static class ActorPersonTypes
{
    public const string Natural = "natural";
    public const string Juridical = "juridical";

    public static bool IsNatural(string? value) =>
        string.Equals(value?.Trim(), Natural, StringComparison.OrdinalIgnoreCase);

    public static bool IsJuridical(string? value) =>
        string.Equals(value?.Trim(), Juridical, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string? value) => IsNatural(value) || IsJuridical(value);

    /// <summary>Normaliza al vocabulario canónico en minúsculas, o <c>null</c> si no aplica.</summary>
    public static string? Normalize(string? value) =>
        IsNatural(value) ? Natural : IsJuridical(value) ? Juridical : null;

    /// <summary>Documento de persona jurídica. Es el único que define la naturaleza por sí solo.</summary>
    private const string NitDocumentType = "NIT";

    /// <summary>
    /// Resuelve la naturaleza de la persona a partir del DOCUMENTO, que es la fuente canónica:
    /// <c>NIT ⇒ juridical</c>, sin importar lo que declare el cliente. Cualquier otro documento
    /// conserva lo declarado tal cual — <c>null</c> incluido.
    /// </summary>
    /// <remarks>
    /// Cierra la incoherencia que el contrato permitía: <c>person_type</c> era opcional e
    /// independiente del documento, así que nada impedía persistir «NIT + natural» — un actor que
    /// para media plataforma es una empresa y para la otra media una persona (el checklist le pide
    /// cédula, el mandato lo redacta como jurídica, la validación de identidad se le manda a él y no
    /// a su representante legal). El asistente ya lo evitaba por convención; aquí pasa a ser
    /// invariante del dominio, que es lo que cinco de las reglas que leen <c>person_type</c> ya
    /// asumen cuando la columna viene vacía.
    ///
    /// <para>Solo corrige la incoherencia: un documento que NO es NIT conserva lo declarado tal
    /// cual, <c>null</c> incluido. Rellenar ahí el <c>natural</c> que falta cambiaría en silencio lo
    /// que decide <c>ChecklistPersonTypeRules</c> para los clientes que nunca mandan el campo — «sin
    /// declarar» significa hoy «no toques el checklist», y eso se respeta.</para>
    /// </remarks>
    public static string? ResolveForDocument(string? documentType, string? declared) =>
        string.Equals(documentType?.Trim(), NitDocumentType, StringComparison.OrdinalIgnoreCase)
            ? Juridical
            : Normalize(declared);
}
