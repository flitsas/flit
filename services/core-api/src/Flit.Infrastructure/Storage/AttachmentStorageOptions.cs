namespace Flit.Infrastructure.Storage;

/// <summary>
/// Configuración del almacenamiento de adjuntos en disco. Se carga desde la variable
/// de entorno <c>ATTACHMENTS_ROOT</c> (o sección <c>Attachments</c>); si está vacía, se
/// resuelve a <c>{ContentRoot}/uploads/tramites</c>.
/// <para>Deuda prod: almacenamiento local no es multi-instancia (mover a S3/Blob).</para>
/// </summary>
public sealed class AttachmentStorageOptions
{
    public const string SectionName = "Attachments";

    /// <summary>Raíz absoluta o relativa al content root. Vacío ⇒ default uploads/tramites.</summary>
    public string Root { get; set; } = string.Empty;
}
