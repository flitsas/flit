namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Comentarios <c>@pii</c> de columna que se repiten en varias tablas (HU #14694, migración <c>R2_Revision</c>). El nivel va al
/// principio del comentario, como en el resto del esquema.
/// </summary>
internal static class ComentariosPii
{
    /// <summary>Id de usuario de la plataforma: un seudónimo, no el nombre.</summary>
    public const string Actor = "@pii:low Seudónimo del usuario (id de usuario de la plataforma).";

    /// <summary><c>creada_por_nombre</c> y <c>cerrada_por_nombre</c>.</summary>
    public const string NombreUsuarioInterno = "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)";

    /// <summary><c>motivo_cierre</c>.</summary>
    public const string TextoLibre = "@pii:low Texto libre del usuario; puede contener datos personales";
}
