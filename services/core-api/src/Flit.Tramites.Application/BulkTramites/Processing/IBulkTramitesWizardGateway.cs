using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Tramites.Application.BulkTramites.Processing;

/// <summary>
/// Los tres pasos del wizard que la carga masiva recorre por fila (HU #12523), detrás de un puerto
/// estrecho.
///
/// <para>Existe por una razón concreta: los casos de uso reales
/// (<c>RunPreflightPreviewHandler</c>, <c>CreateProcedureInstanceFromConsultaHandler</c>,
/// <c>PutActorsHandler</c>) son clases con constructores de 6-9 dependencias y métodos no virtuales
/// — no se pueden sustituir en un test. Sin este puerto, la regla que de verdad importa (vehículo
/// que falla ⇒ NO se crea el trámite; actor que falla ⇒ el trámite SÍ se crea, marcado para
/// retomar) quedaría sin prueba. El adaptador que lo implementa no decide nada: solo traduce.</para>
/// </summary>
public interface IBulkTramitesWizardGateway
{
    /// <summary>
    /// Paso 1: consulta el vehículo SIN crear nada. Devuelve el token de la consulta para
    /// encadenarla con la creación, o el código de error del proveedor.
    /// </summary>
    Task<(string? PreviewToken, string? Error)> PreviewVehicleAsync(
        BulkTramitesRowContext context, CancellationToken ct);

    /// <summary>Paso 2: crea el trámite reutilizando la consulta del paso 1.</summary>
    Task<(Guid? ProcedureInstanceId, string? Error)> CreateTramiteAsync(
        BulkTramitesRowContext context, string? previewToken, CancellationToken ct);

    /// <summary>
    /// Consulta de persona (RUNT conductor) por documento, con el MISMO caso de uso que el botón
    /// «Consultar RUNT» del paso de actores. Devuelve el nombre completo que reporta el RUNT, o el
    /// código de error: <c>conductor_no_encontrado</c> cuando el proveedor responde pero no conoce
    /// el documento, <c>unsupported_document_type</c> para los tipos que el RUNT conductor no
    /// consulta (NIT), o el código del proveedor si la consulta falla.
    /// </summary>
    Task<(string? FullName, string? Error)> LookupPersonAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken ct);

    /// <summary>
    /// Consulta de persona JURÍDICA por NIT (HU #12538): RUES para la razón social y el directorio
    /// de representantes legales de la empresa para el firmante, igual que el botón «Consultar» del
    /// paso de actores cuando el documento es NIT. Devuelve la empresa resuelta, o el código de
    /// error: <c>empresa_no_encontrada</c> cuando RUES responde pero no conoce el NIT, o
    /// <c>consulta_empresa_fallida</c> si la consulta se cae. El directorio va en el resultado (puede
    /// venir null): decidir qué hacer sin representante es del procesador, no de este puerto.
    /// </summary>
    Task<(BulkTramitesCompanyLookup? Company, string? Error)> LookupCompanyAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        string nit,
        CancellationToken ct);

    /// <summary>
    /// Paso de actores: guarda las partes con el MISMO caso de uso del wizard, de modo que el envío
    /// de validación de identidad se dispara solo, sin lógica propia de la carga masiva. Devuelve el
    /// código de error, o null si guardó.
    /// </summary>
    Task<string?> SaveActorsAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        IReadOnlyList<ActorInput> actors,
        CancellationToken ct);
}

/// <summary>
/// Empresa resuelta para un actor con NIT: la razón social tal como la entrega RUES (el recorte de
/// cláusulas societarias lo hace el procesador, igual que el wizard) y, si el tenant la tiene en su
/// directorio, la entrada con sus representantes.
/// </summary>
public sealed record BulkTramitesCompanyLookup(
    string RazonSocial,
    BulkTramitesCompanyDirectoryEntry? Directorio);
