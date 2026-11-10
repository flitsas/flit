namespace Flit.Modules.Security.Application.Auth.BulkInvitation;

/// <summary>
/// Configuración de la invitación masiva (<c>BulkInvitations:*</c>, ADR-0077). Los topes salen del límite de 60 s de nginx
/// y de que <c>CreateInvitationHandler</c> envía el correo en línea: por eso hay un máximo de filas y un deadline propio.
/// </summary>
public sealed class BulkInvitationOptions
{
    public const string SectionName = "BulkInvitations";

    /// <summary>Máximo de filas de datos por archivo; con una más el archivo completo se rechaza (<c>TOO_MANY_ROWS</c>).</summary>
    public int MaxRows { get; set; } = 50;

    /// <summary>Tamaño máximo del .xlsx (1 MB).</summary>
    public int MaxFileBytes { get; set; } = 1_048_576;

    /// <summary>Anti zip-bomb: tope del tamaño DESCOMPRIMIDO del paquete (suma de sus partes).</summary>
    public long MaxUncompressedBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>Invitaciones que el commit crea en paralelo (cada una en su propio scope DI).</summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    /// Segundos que el commit puede tardar en EMPEZAR filas nuevas (50 s &lt; los 60 s de nginx). Pasado el deadline, las
    /// filas sin procesar se reportan <c>not_processed</c>; las que ya estaban en curso terminan.
    /// </summary>
    public int CommitDeadlineSeconds { get; set; } = 50;

    /// <summary>Límite de tasa del dryRun por usuario (frena la enumeración de correos), peticiones por minuto.</summary>
    public int DryRunRequestsPerUserPerMinute { get; set; } = 10;
}

/// <summary>Estructura fija del archivo de invitación masiva: los encabezados son el contrato.</summary>
public static class BulkInvitationTemplateCatalog
{
    public const string Version = "v1";
    public const string SheetName = "Invitaciones";
    public const string GuideSheetName = "Instrucciones";
    public const string ListsSheetName = "Roles";

    public const string FullNameHeader = "Nombre completo";
    public const string EmailHeader = "Correo";
    public const string RoleHeader = "Rol";

    /// <summary>Encabezados en el orden exacto de las columnas A, B y C.</summary>
    public static IReadOnlyList<string> Headers { get; } = [FullNameHeader, EmailHeader, RoleHeader];
}

/// <summary>Códigos ESTABLES de rechazo del archivo completo (no se procesa ninguna fila).</summary>
public static class BulkInvitationFileErrors
{
    /// <summary>No es un .xlsx legible (firma, estructura o zip-bomb).</summary>
    public const string InvalidFile = "INVALID_FILE";

    /// <summary>Faltan o cambiaron las columnas Nombre completo / Correo / Rol.</summary>
    public const string TemplateInvalid = "TEMPLATE_INVALID";

    /// <summary>Más filas de datos que <see cref="BulkInvitationOptions.MaxRows"/>.</summary>
    public const string TooManyRows = "TOO_MANY_ROWS";

    /// <summary>Pesa más que <see cref="BulkInvitationOptions.MaxFileBytes"/>.</summary>
    public const string FileTooLarge = "FILE_TOO_LARGE";

    /// <summary>El archivo no trae ninguna fila de datos.</summary>
    public const string EmptyFile = "EMPTY_FILE";
}

/// <summary>
/// Neutraliza la inyección de fórmulas (OWASP CSV/Formula Injection): un valor que empieza por <c>=</c>, <c>+</c>, <c>-</c> o
/// <c>@</c> (o por tabulación / retorno de carro) se antepone con una comilla simple, así ni Excel ni un CSV posterior lo
/// interpretan como fórmula.
/// </summary>
public static class BulkInvitationFormulaGuard
{
    public static bool IsRisky(string? value) =>
        !string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

    /// <summary>Devuelve el valor inocuo (prefijo <c>'</c> si empezaba como fórmula); <c>null</c> se conserva.</summary>
    public static string? Neutralize(string? value) => IsRisky(value) ? "'" + value : value;
}

/// <param name="RowNumber">Número de fila de Excel de origen (los datos empiezan en la 2).</param>
/// <param name="FullName">Nombre recortado (y neutralizado si empezaba como fórmula).</param>
/// <param name="Email">Correo recortado tal como se escribió (la normalización la hace el servicio).</param>
/// <param name="Role">Nombre o código del rol tal como se escribió.</param>
/// <param name="FormulaNeutralized">Alguna celda empezaba como fórmula (o era una fórmula) y se neutralizó: la fila se rechaza.</param>
public sealed record BulkInvitationParsedRow(
    int RowNumber,
    string FullName,
    string Email,
    string Role,
    bool FormulaNeutralized);

/// <summary>Resultado de leer el archivo: o un error de archivo completo, o las filas de datos.</summary>
public sealed record BulkInvitationParseResult(string? FileError, IReadOnlyList<BulkInvitationParsedRow> Rows)
{
    public static BulkInvitationParseResult Rejected(string fileError) => new(fileError, []);

    public static BulkInvitationParseResult Ok(IReadOnlyList<BulkInvitationParsedRow> rows) => new(null, rows);
}

/// <summary>Lector del archivo de invitación masiva (lectura SAX; implementado en Infraestructura).</summary>
public interface IBulkInvitationXlsxParser
{
    /// <summary>Lee el .xlsx. Nunca lanza por un archivo del usuario: devuelve <see cref="BulkInvitationParseResult.FileError"/>.</summary>
    BulkInvitationParseResult Parse(Stream xlsx);
}

/// <param name="Filename">Nombre sugerido del archivo.</param>
/// <param name="Mimetype">Tipo MIME del .xlsx.</param>
/// <param name="Content">Bytes del archivo.</param>
public sealed record RenderedBulkInvitationTemplate(string Filename, string Mimetype, byte[] Content);

/// <summary>Genera la plantilla de invitación masiva con el catálogo de roles que entrega <see cref="IInvitationScopePolicy"/>.</summary>
public interface IBulkInvitationXlsxTemplate
{
    /// <exception cref="InvitationScopeDeniedException">El alcance del invitador no permite la operación.</exception>
    Task<RenderedBulkInvitationTemplate> BuildAsync(InvitationScopeContext context, CancellationToken cancellationToken);
}
