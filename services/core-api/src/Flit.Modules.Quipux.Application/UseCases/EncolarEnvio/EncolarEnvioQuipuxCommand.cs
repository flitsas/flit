namespace Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;

/// <summary>
/// Orden de encolar la radicación de un trámite en Quipux. Un trámite = una submission activa; el
/// handler es idempotente, así que reintentar la orden no duplica nada.
/// </summary>
public sealed class EncolarEnvioQuipuxCommand
{
    /// <summary>Trámite a radicar. Debe estar en <c>preparado</c>.</summary>
    public required Guid ProcedureInstanceId { get; init; }

    /// <summary>Tenant dueño del trámite (enforcement multi-tenant en la carga).</summary>
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Instante que se estampa en el <c>document_name</c>. Opcional: por defecto <c>UtcNow</c>.
    /// Entra por el comando —y no se lee dentro del handler— porque el nombre se calcula UNA sola
    /// vez y se persiste: quien encola decide el desfase (UTC vs. hora Colombia) y los tests fijan
    /// el reloj sin trucos. Ver <c>QuipuxDocumentNameBuilder</c>.
    /// </summary>
    public DateTimeOffset? Momento { get; init; }

    /// <summary>Correlaciona los eventos de bitácora de este ciclo del worker.</summary>
    public Guid? CorrelationId { get; init; }
}

/// <summary>Desenlace de un encolado. Ninguno es excepcional: el no-elegible es el caso mayoritario.</summary>
public enum EncolarEnvioQuipuxStatus
{
    /// <summary>Submission creada. El registrador la tomará en su próximo ciclo.</summary>
    Encolada,

    /// <summary>El trámite ya tenía una submission activa; se devuelve esa. Idempotencia.</summary>
    YaEncolada,

    /// <summary>El trámite no va a Quipux (o aún no puede). Ver <see cref="EncolarEnvioQuipuxResult.Motivo"/>.</summary>
    NoElegible,

    /// <summary>El trámite no existe, está eliminado o no es del tenant.</summary>
    NoEncontrado,

    /// <summary>El comando llegó mal formado (ids vacíos). Defecto del llamador, no del dato.</summary>
    EntradaInvalida,

    /// <summary>No se pudo asegurar el PDF consolidado maestro. Reintentable en el próximo ciclo.</summary>
    ErrorConsolidado,
}

/// <summary>
/// Motivos estables de no-elegibilidad. Son códigos, no mensajes: viajan a la bitácora y al panel
/// "cola Quipux", y los tests se anclan a ellos. En FLIT 1.0 la no-elegibilidad era invisible — el
/// trámite simplemente no aparecía en <c>listQuipux</c> y nadie sabía por qué.
/// </summary>
public static class EncolarEnvioQuipuxMotivos
{
    /// <summary>El trámite no está en <c>preparado</c>.</summary>
    public const string EstadoNoPreparado = "estado_no_preparado";

    /// <summary>
    /// El <c>procedure_type</c> no tiene bloque <c>quipux</c> en <c>external_refs</c> (o está a medio
    /// llenar). Es el gate natural: ese tipo de trámite no se radica por esta vía.
    /// </summary>
    public const string TipoSinParametrizacionQuipux = "tipo_sin_parametrizacion_quipux";

    /// <summary>El trámite no tiene organismo de tránsito resuelto (<c>transit_office_id</c> nulo).</summary>
    public const string SinOrganismo = "sin_organismo";

    /// <summary>El OT no tiene <c>external_refs-&gt;'quipux'-&gt;&gt;'codigoDivipo'</c>. Sin él Quipux no sabe a qué secretaría radicar.</summary>
    public const string OrganismoSinCodigoDivipo = "organismo_sin_codigo_divipo";

    /// <summary>La parametrización no declara ni <c>campoPlaca</c> ni <c>campoVin</c>: no hay cómo identificar el vehículo.</summary>
    public const string ParametrizacionSinIdentificador = "parametrizacion_sin_identificador";

    /// <summary>El field value del identificador (placa o VIN) está vacío en este trámite.</summary>
    public const string SinIdentificadorVehiculo = "sin_identificador_vehiculo";

    /// <summary>El tenant no tiene razón social; el nombre del documento no se puede formar.</summary>
    public const string TenantSinRazonSocial = "tenant_sin_razon_social";

    /// <summary>La razón social no deja ningún carácter tras sanitizar (ver <c>QuipuxDocumentNameBuilder</c>).</summary>
    public const string RazonSocialNoSanitizable = "razon_social_no_sanitizable";
}

/// <summary>Resultado tipado del encolado. Sin excepciones para el flujo normal.</summary>
public sealed class EncolarEnvioQuipuxResult
{
    public EncolarEnvioQuipuxStatus Status { get; init; }

    /// <summary>Submission creada o reutilizada; null en los desenlaces que no encolan.</summary>
    public Guid? SubmissionId { get; init; }

    /// <summary>Nombre del documento fijado para esta radicación; null si no se encoló.</summary>
    public string? DocumentName { get; init; }

    /// <summary>Código del motivo (ver <see cref="EncolarEnvioQuipuxMotivos"/>); null en el camino feliz.</summary>
    public string? Motivo { get; init; }

    /// <summary>¿Hay una submission activa para este trámite tras ejecutar el caso de uso?</summary>
    public bool TieneSubmission =>
        Status is EncolarEnvioQuipuxStatus.Encolada or EncolarEnvioQuipuxStatus.YaEncolada;

    public static EncolarEnvioQuipuxResult Encolada(Guid submissionId, string documentName) =>
        new()
        {
            Status = EncolarEnvioQuipuxStatus.Encolada,
            SubmissionId = submissionId,
            DocumentName = documentName,
        };

    public static EncolarEnvioQuipuxResult YaEncolada(Guid submissionId, string documentName) =>
        new()
        {
            Status = EncolarEnvioQuipuxStatus.YaEncolada,
            SubmissionId = submissionId,
            DocumentName = documentName,
        };

    public static EncolarEnvioQuipuxResult NoElegible(string motivo) =>
        new() { Status = EncolarEnvioQuipuxStatus.NoElegible, Motivo = motivo };

    public static EncolarEnvioQuipuxResult NoEncontrado() =>
        new() { Status = EncolarEnvioQuipuxStatus.NoEncontrado };

    public static EncolarEnvioQuipuxResult EntradaInvalida(string motivo) =>
        new() { Status = EncolarEnvioQuipuxStatus.EntradaInvalida, Motivo = motivo };

    public static EncolarEnvioQuipuxResult ErrorConsolidado(string motivo) =>
        new() { Status = EncolarEnvioQuipuxStatus.ErrorConsolidado, Motivo = motivo };
}

/// <summary>
/// Puerto hacia el expediente consolidado maestro del trámite.
/// </summary>
/// <remarks>
/// <para>
/// NO reimplementa la generación del PDF: la implementación de Infrastructure delega en
/// <c>GenerarConsolidadoMaestroHandler</c> (<c>Flit.Tramites.Application</c>), que ya reutiliza el
/// adjunto cuando <c>ProcedureInstance.ConsolidadoMaestroVigente</c> es true — el equivalente, mejor
/// hecho, del chequeo <c>dateSentOt == hoy</c> de FLIT 1.0, que además solo evitaba REGENERAR el PDF
/// y no evitaba reenviarlo.
/// </para>
/// <para>
/// Existe como puerto y no como referencia directa para que <c>Flit.Modules.Quipux.Application</c> no
/// dependa del Application de otro módulo (la convención del repo es que los módulos se cruzan por
/// Domain o por Infrastructure, nunca Application → Application).
/// </para>
/// </remarks>
public interface IQuipuxConsolidadoMaestroPort
{
    /// <summary>
    /// Asegura que el trámite tenga un <c>consolidado_maestro</c> vigente y devuelve su adjunto.
    /// Genera solo si hace falta.
    /// </summary>
    Task<QuipuxConsolidadoMaestro> AsegurarAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adjunto <c>consolidado_maestro</c> del trámite.
/// </summary>
/// <param name="AttachmentId">Adjunto a subir al bucket de Quipux; null si hubo error.</param>
/// <param name="Regenerado">
/// <c>false</c> = se reutilizó el PDF vigente. Solo trazabilidad: distingue el evento
/// <c>consolidado_generado/ok</c> del <c>consolidado_generado/omitido</c>.
/// </param>
/// <param name="Error">Código de error de <c>GenerarConsolidadoMaestroHandler</c> (<c>not_found</c>, <c>sin_adjuntos</c>, …); null si fue bien.</param>
public sealed record QuipuxConsolidadoMaestro(Guid? AttachmentId, bool Regenerado, string? Error)
{
    public bool Exitoso => AttachmentId is not null && Error is null;

    public static QuipuxConsolidadoMaestro Generado(Guid attachmentId) => new(attachmentId, true, null);

    public static QuipuxConsolidadoMaestro Reutilizado(Guid attachmentId) => new(attachmentId, false, null);

    public static QuipuxConsolidadoMaestro Fallo(string error) => new(null, false, error);
}

/// <summary>
/// Puerto para leer la parametrización Quipux del organismo de tránsito
/// (<c>catalogs.transit_offices.external_refs-&gt;'quipux'</c>).
/// </summary>
/// <remarks>
/// El catálogo de OT vive fuera de <c>Flit.Tramites.Domain</c>, así que este módulo no puede leerlo
/// directamente sin acoplarse a Admin. Se declara aquí y lo implementa Infrastructure, que sí ve el
/// <c>DbContext</c> completo.
/// </remarks>
public interface IQuipuxOrganismoPort
{
    /// <summary>Código DIVIPO del OT, o null si el OT no existe o no lo tiene parametrizado.</summary>
    Task<string?> ObtenerCodigoDivipoAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Puerto para leer la razón social del cliente (<c>identity.tenants.legal_name</c>), que es la
/// primera parte del <c>document_name</c> de Quipux.
/// </summary>
public interface IQuipuxTenantPort
{
    /// <summary>Razón social del tenant, o null si no existe.</summary>
    Task<string?> ObtenerRazonSocialAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
