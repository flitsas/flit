using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.Download;
using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Admin.Application.GeneracionDocumental.List;
using Flit.Admin.Application.GeneracionDocumental.Prefill;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Generación documental autónoma (Feature #12201, ADR-0056-generacion-documental-standalone):
/// emisión de documentos SIN abrir un trámite. Este archivo cubre el Certificado RUES, el historial,
/// la descarga presignada y el prellenado standalone (CF-25); la transferencia A/B/C y los lotes
/// llegan en HUs posteriores.
///
/// <para><b>Autorización POR PERMISO</b> (<c>generacion-documental.generate</c>), nunca por una
/// policy de grupo de SuperAdmin: eso dejaría fuera a AdminCompany, que es justamente el usuario del
/// módulo. Hay un test de contrato que escanea este archivo y falla si aparece esa policy —de ahí
/// que ni siquiera se la nombre aquí.</para>
///
/// <para><b>La generación NUNCA devuelve el binario</b> (decisión del PO): responde
/// <c>application/json</c> con <c>{ id, status }</c> con cualquier <c>Accept</c>, y el PDF se baja
/// después por <c>GET /{id}/download</c>. <c>status</c> solo puede valer <c>generated</c> o
/// <c>error</c> (CF-21).</para>
/// </summary>
public static class AdminGeneracionDocumentalEndpoints
{
    public static IEndpointRouteBuilder MapAdminGeneracionDocumentalEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/generacion-documental")
            .WithTags("Admin · Generación documental");

        group.MapPost("/rues/preview", PreviewRuesAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalPreviewRues")
            .WithSummary("Consulta el RUES por NIT para revisión previa, sin persistir nada")
            .WithDescription("Consulta EN VIVO el RUES por NIT y devuelve los campos mercantiles "
                + "para que el usuario los revise antes de emitir (CF-04). No crea ninguna fila en "
                + "admin.standalone_documents ni escribe archivo alguno. Requiere el permiso "
                + "generacion-documental.generate.")
            .Produces<PreviewRuesCompanyResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/rues/generate", GenerateRuesAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalGenerateRues")
            .WithSummary("Genera el Certificado RUES standalone (sin trámite)")
            .WithDescription("Consulta el RUES EN VIVO por NIT, congela el snapshot inmutable, "
                + "renderiza el PDF con el mismo generador del expediente y lo persiste en storage. "
                + "NO devuelve el PDF: responde { id, status } en application/json y la descarga va "
                + "siempre por GET /{id}/download. La cabecera Idempotency-Key, repetida dentro del "
                + "mismo tenant, devuelve el documento existente sin consultar al proveedor ni "
                + "escribir un archivo nuevo (CF-16). El documento queda en el tenant del JWT, "
                + "también para SuperAdmin. Requiere el permiso generacion-documental.generate.")
            .Produces<StandaloneDocumentGenerateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // Transferencia de dominio (CF-06/CF-07/CF-08/CF-09/CF-24/CF-26, HU #12207 y #12208). Los
        // tres escenarios del anexo: A traspaso ordinario, B unilateral de leasing y C a tercero.
        group.MapPost("/transferencia/generate", GenerateTransferenciaAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalGenerateTransferencia")
            .WithSummary("Genera el Documento de Transferencia de Dominio (escenarios A, B y C)")
            .WithDescription("Emite el instrumento privado de transferencia de dominio SIN abrir un "
                + "tramite, conforme al anexo normativo docs/plantilla-transferencia-dominio.md. El "
                + "escenario es obligatorio y unico: cero o mas de uno responde 422 con el codigo "
                + "VB-05. VB-07 es un GATE PREVIO: mientras no se declare que ninguna de las once "
                + "condiciones especiales de traspaso de los arts. 5.3.2.3 a 5.3.2.13 aplica, la "
                + "generacion se rechaza con 422 y el codigo VB-07; declarar cualquiera de las once "
                + "tambien rechaza, con el articulo citado en el mensaje. El art. 5.3.2.14 "
                + "(expedicion de la nueva licencia) NO es una condicion especial y no bloquea. Las "
                + "validaciones bloqueantes (VB-02, VB-06, VB-07, VB-A-04, VB-A-06, VB-A-07, "
                + "VB-B-01..05, VB-C-01) responden 422 con codigo, campo y mensaje, y NUNCA reflejan "
                + "el valor capturado. El escenario B no admite precio ni contraprestacion: si el "
                + "cuerpo los trae, 422 con VB-B-05. Las prevalidaciones advisory (VB-01, VB-03, "
                + "VB-04, VB-A-01..03, VB-A-05, VB-A-08..10, VB-B-06, VB-C-02..06, VB-C-08..10) "
                + "viajan en la respuesta 200 como aviso y no bloquean nunca. El modo "
                + "de firma es MANUSCRITA fijo: el PDF lleva lineas de firma con nombre y documento "
                + "y no contiene leyenda de firma electronica ni sello alguno. NO devuelve el PDF: "
                + "responde { id, status, advisories } en application/json y la descarga va por "
                + "GET /{id}/download. Requiere el permiso generacion-documental.generate.")
            .Produces<StandaloneTransferGenerateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // Prellenado (CF-25, HU #12206). Tres rutas que SOLO consultan: ninguna crea fila en
        // admin.standalone_documents ni escribe archivo. Van con el permiso .generate porque gastan
        // consultas de pago al proveedor, igual que /rues/preview.
        group.MapPost("/prefill/vehiculo", PrefillVehiculoAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalPrefillVehiculo")
            .WithSummary("Prellena el bloque de vehiculo a partir de la placa (RUNT)")
            .WithDescription("Consulta la placa en la cadena de proveedores RUNT y devuelve 12 de "
                + "las 13 variables de vehiculo del anexo normativo, cada una con su fuente. La "
                + "restante (no_licencia_transito) no la entrega ninguna consulta y se captura a "
                + "mano. Un tramite ACTIVO sobre la placa NO bloquea: no se evalua duplicidad ni "
                + "gate de organismo, y no se reutiliza el preflight del wizard. Una placa sin "
                + "antecedente responde 200 con found=false, nunca 404. No persiste nada. Requiere "
                + "el permiso generacion-documental.generate.")
            .Produces<PrefillResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/prefill/persona-juridica", PrefillPersonaJuridicaAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalPrefillPersonaJuridica")
            .WithSummary("Prellena una parte juridica por NIT (directorio primero, RUES de respaldo)")
            .WithDescription("Precedencia: el directorio de representantes legales de la compania "
                + "PRIMERO y el RUES solo si el directorio no responde. La respuesta declara la "
                + "fuente efectiva por campo. El digito de verificacion se CALCULA (modulo 11 DIAN) "
                + "y viaja con fuente CALCULADO: no se consulta ni se captura. Sin coincidencia en "
                + "ninguna fuente responde 200 con found=false. No persiste nada. Requiere el "
                + "permiso generacion-documental.generate.")
            .Produces<PrefillResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/prefill/persona-natural", PrefillPersonaNaturalAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalPrefillPersonaNatural")
            .WithSummary("Prellena una parte natural por documento (RUNT primero, contacto de respaldo)")
            .WithDescription("Precedencia: la cadena RUNT persona PRIMERO —sin instancia, con "
                + "guardado de cache de instancia nula— y contact-lookup como respaldo. El "
                + "domicilio siempre sale de contact-lookup: el RUNT no lo entrega. La respuesta "
                + "declara la fuente efectiva por campo. Sin coincidencia responde 200 con "
                + "found=false, nunca 404. No persiste nada. Requiere el permiso "
                + "generacion-documental.generate.")
            .Produces<PrefillResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status502BadGateway);

        // Historial: la RAIZ del grupo, no una sub-ruta /documentos. Es lo que declara el diseno
        // 7.1 y lo que consume el frontend (GENERACION_DOCUMENTAL_API_BASE).
        group.MapGet("", ListDocumentosAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalList")
            .WithSummary("Historial paginado de documentos generados por la compania")
            .WithDescription("Devuelve la metadata minima del historial (tipo, escenario, empresa, "
                + "usuario, fecha y resultado) filtrable por tipo, rango de fechas, usuario y "
                + "estado (CF-17/CF-18). NUNCA expone document_snapshot ni rues_snapshot (PII alta) "
                + "ni rutas de storage ni URLs firmadas. El parametro opcional tenantId solo lo "
                + "honra un SuperAdmin y aun asi devuelve metadata, nunca contenido (CF-20). "
                + "Requiere el permiso generacion-documental.read.")
            .Produces<StandaloneDocumentsPageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Lotes XLSX (CF-11/CF-12/CF-13/CF-16, HU #12210). El seguimiento por polling y la descarga
        // ZIP del lote NO viven aqui: son de la HU siguiente.
        group.MapGet("/lotes/plantilla", DescargarPlantillaLoteAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalPlantillaLote")
            .WithSummary("Descarga la plantilla XLSX v1 de carga masiva")
            .WithDescription("Devuelve el XLSX de la plantilla v1 con TODAS las columnas declaradas "
                + "como texto. Que sean texto no es cosmetica: es lo que evita que Excel guarde una "
                + "fecha como serial numerico, que el parser rechaza en vez de convertir. El "
                + "encabezado de este archivo es exactamente el que exige POST /lotes. Requiere el "
                + "permiso generacion-documental.read.")
            .Produces<byte[]>(
                StatusCodes.Status200OK,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/lotes", CrearLoteAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalCrearLote")
            .WithSummary("Carga un XLSX de hasta 100 filas y encola el lote")
            .WithDescription("Recibe multipart/form-data con el campo file. Responde 202 con "
                + "{ batchId, status: queued, total } y el procesamiento ocurre en segundo plano: "
                + "cada fila se genera con el handler de su tipo (certificado_rues o "
                + "transferencia_dominio_generada con su escenario). Una fila invalida NO cancela el "
                + "lote: queda en error con validation_errors (codigo, campo y mensaje, nunca el "
                + "valor capturado) y el lote termina en partial_failure. Una fila que declare "
                + "cualquiera de las once condiciones de los arts. 5.3.2.3 a 5.3.2.13 queda en error "
                + "con el codigo VB-07 y el articulo citado, y el resto del lote continua. Rechazos "
                + "del archivo completo (422, y el archivo NO se persiste): too_many_rows con mas de "
                + "100 filas, template_invalid si el encabezado no es el de la v1 e invalid_file si "
                + "el contenido real no es un XLSX. La cabecera Idempotency-Key repetida dentro del "
                + "mismo tenant devuelve el lote existente sin crear lote, ni filas, ni archivo "
                + "fuente nuevo. Requiere el permiso generacion-documental.generate.")
            .Produces<StandaloneBatchCreateResponse>(StatusCodes.Status202Accepted)
            .Produces<StandaloneBatchCreateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .DisableAntiforgery();

        // Seguimiento del lote y descarga ZIP (CF-13/CF-14/CF-15, HU #12211). Las tres rutas van
        // DESPUES de /lotes/plantilla y no chocan con ella: la restriccion :guid del segmento hace
        // que "plantilla" no encaje aqui.
        group.MapGet("/lotes/{batchId:guid}", ObtenerEstadoLoteAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalEstadoLote")
            .WithSummary("Avance de un lote para el seguimiento in-app (CF-14)")
            .WithDescription("Devuelve el estado del lote, el total de filas y cuantas van "
                + "generadas y en error. Los contadores se cuentan sobre las filas ya "
                + "materializadas, no se leen de la cabecera: esta solo los escribe al cerrar el "
                + "lote y el progreso quedaria clavado en cero durante todo el procesamiento. El "
                + "campo isTerminal viaja explicito porque es la senal con la que el cliente "
                + "detiene el polling. No hay correo ni notificacion push: el avance se consulta, "
                + "no se empuja. Un lote de otra compania responde 404 sin revelar su existencia. "
                + "Requiere el permiso generacion-documental.read.")
            .Produces<StandaloneBatchStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/lotes/{batchId:guid}/items", ListarItemsLoteAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalItemsLote")
            .WithSummary("Filas de un lote con el detalle de sus errores (CF-13)")
            .WithDescription("Listado paginado de las filas del lote, ordenado por numero de fila. "
                + "Cada fila trae numero, tipo, escenario, estado y sus validationErrors con "
                + "codigo, campo y mensaje. El mensaje NUNCA refleja el valor capturado que produjo "
                + "el error: la fila puede traer una cedula o una direccion. Ojo con documentType: "
                + "en una fila cuyo tipo el usuario tecleo mal, los CHECK de la tabla no permiten "
                + "persistir el valor real y la columna dice certificado_rues; el error verdadero "
                + "esta en validationErrors, que es lo que la interfaz debe mostrar. No expone "
                + "snapshots ni rutas de storage. Un lote de otra compania responde 404. Requiere "
                + "el permiso generacion-documental.read.")
            .Produces<StandaloneBatchItemsPageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/lotes/{batchId:guid}/zip", DescargarZipLoteAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalZipLote")
            .WithSummary("Descarga por streaming los documentos generados del lote (CF-15)")
            .WithDescription("Devuelve application/zip armado al vuelo sobre el cuerpo de la "
                + "respuesta, con UNA entrada por fila en estado generated: un lote de nueve "
                + "generadas y una en error produce nueve entradas. El ZIP no se persiste en "
                + "storage ni en base de datos, y la memoria del proceso sostiene un PDF a la vez "
                + "porque cada binario se abre, se copia por bloques y se cierra antes del "
                + "siguiente. Un lote sin ninguna fila generada responde 409 con una explicacion "
                + "explicita, nunca un ZIP vacio. Un lote de otra compania responde 404 sin "
                + "revelar su existencia ni entregar presigned URLs. Requiere el permiso "
                + "generacion-documental.read.")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/zip")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/download", DownloadDocumentoAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalDownload")
            .WithSummary("Presigned URL de descarga del PDF ya generado y auditoria (CF-19)")
            .WithDescription("Devuelve una presigned URL de vida corta (ADR-0029) del PDF ya "
                + "generado y, en la misma operacion, incrementa download_count y actualiza "
                + "downloaded_at con un unico UPDATE atomico. No regenera nada: no se invoca ningun "
                + "generador ni proveedor externo y storage_sha256 no cambia. Un id de otra "
                + "compania responde 404 sin revelar su existencia (tambien para SuperAdmin), y un "
                + "documento que no este en estado generated responde 409 sin URL. "
                + "Requiere el permiso generacion-documental.read.")
            .Produces<StandaloneDocumentDownloadResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// Cuerpo de <c>POST /transferencia/generate</c>.
    ///
    /// <para><b><see cref="Escenarios"/> es una lista</b> para que VB-05 sea comprobable de verdad:
    /// con un solo campo escalar, "mas de un escenario" seria irrepresentable y ese 422 no podria
    /// probarse nunca. <see cref="Escenario"/> se admite como atajo de un solo valor; si llegan los
    /// dos, se suman y la politica rechaza el conjunto.</para>
    /// </summary>
    public sealed record TransferenciaRequest(
        IReadOnlyList<string>? Escenarios,
        string? Escenario,
        TransferenciaVehiculoRequest? Vehiculo,
        TransferenciaParteRequest? Transferente,
        TransferenciaParteRequest? Adquirente,
        TransferenciaNegocioRequest? Negocio,
        TransferenciaGravamenRequest? Gravamen,
        TransferenciaRegimenRequest? RegimenAplicable,
        TransferenciaLeasingRequest? Leasing);

    /// <summary>Las 13 variables de vehiculo del anexo normativo (seccion 5.1).</summary>
    public sealed record TransferenciaVehiculoRequest(
        string? Placa,
        string? Marca,
        string? Linea,
        string? ModeloAnio,
        string? ClaseVehiculo,
        string? TipoCarroceria,
        string? Color,
        string? NoMotor,
        string? NoChasis,
        string? NoSerie,
        string? Servicio,
        string? NoLicenciaTransito,
        string? OrganismoTransito);

    /// <summary>
    /// Una parte del negocio. El digito de verificacion se admite pero se RECALCULA en el servidor
    /// cuando el documento es un NIT: el PDF no puede llevar un NIT y un DV que no se correspondan.
    /// </summary>
    public sealed record TransferenciaParteRequest(
        string? TipoPersona,
        string? NombreRazonSocial,
        string? TipoDoc,
        string? NumeroDoc,
        string? DigitoVerificacion,
        string? Domicilio,
        string? RepresentanteLegal,
        string? CcRepresentanteLegal);

    /// <summary>
    /// Variables del negocio (seccion 5.4), incluidas las tres fiscales que imprime la clausula
    /// SEXTA. El modo de firma NO se captura: es MANUSCRITA fijo.
    /// </summary>
    public sealed record TransferenciaNegocioRequest(
        string? TituloJuridico,
        string? DescripcionTitulo,
        string? PrecioLetras,
        string? PrecioNumeros,
        string? ContraprestacionDescripcion,
        string? FormaPago,
        string? AsumeRetencionFuente,
        string? AsumeDerechosTramite,
        string? AsumeImpuestoVehiculo,
        string? CiudadFirma,
        DateOnly? FechaFirma);

    /// <summary>
    /// Antecedente de leasing (seccion 5.5 del anexo). Obligatorio en el escenario B; en el C es
    /// opcional y solo alimenta VB-C-01 —comparar al adquirente con el locatario historico—.
    ///
    /// <para>No hay campo de precio: el acto del art. 5.3.2.2 es unilateral y no declara precio
    /// entre las partes del instrumento (VB-B-05).</para>
    /// </summary>
    public sealed record TransferenciaLeasingRequest(
        bool TransferenteEsEntidadFinanciera,
        string? NoContratoLeasing,
        string? TipoOpcionCompra,
        DateOnly? FechaTerminacion,
        string? LocatarioNombre,
        string? LocatarioTipoDoc,
        string? LocatarioNoDoc);

    /// <summary>Declaracion de gravamen del usuario (VB-A-04). FLIT no consulta el registro de garantias.</summary>
    public sealed record TransferenciaGravamenRequest(
        bool GravamenActivo,
        bool TieneLevantamientoOAutorizacion);

    /// <summary>
    /// Declaracion de regimen aplicable (seccion 4.0, CF-24). Es el GATE previo a la seleccion de
    /// escenario y lo evalua VB-07: se rechaza tanto declarar una de las once condiciones de los
    /// arts. 5.3.2.3 a 5.3.2.13 como no responder. La declaracion y su fecha quedan en
    /// input_summary, sin PII.
    /// </summary>
    public sealed record TransferenciaRegimenRequest(
        bool? NingunaAplica,
        IReadOnlyList<string>? CondicionesDeclaradas,
        DateTimeOffset? DeclaredAt);

    /// <summary>Codigo, campo y mensaje. Nunca el valor capturado (requisito de PII).</summary>
    public sealed record TransferValidationIssueResponse(string Code, string Field, string Message);

    /// <summary>
    /// Respuesta de la generacion de transferencia: id, estado y las prevalidaciones advisory que
    /// la interfaz debe mostrar como AVISO. Sin PDF y sin snapshot.
    /// </summary>
    public sealed record StandaloneTransferGenerateResponse(
        Guid Id,
        string Status,
        IReadOnlyList<TransferValidationIssueResponse> Advisories);

    /// <summary>Cuerpo de ambas rutas del Certificado RUES.</summary>
    public sealed record RuesRequest(string? Nit);

    /// <summary>
    /// Contrato de respuesta de la generación: solo id y estado. Sin PDF, sin snapshot y sin PII.
    /// </summary>
    public sealed record StandaloneDocumentGenerateResponse(Guid Id, string Status);

    /// <summary>
    /// Fila del historial (CF-17). Deliberadamente SIN snapshot, sin ruta de storage y sin hash:
    /// lo que no esta en el contrato no se puede filtrar por descuido desde el frontend.
    /// </summary>
    public sealed record StandaloneDocumentListResponse(
        Guid Id,
        string DocumentType,
        string? Scenario,
        string Status,
        string? ErrorCode,
        string? Filename,
        string? CompanyName,
        Guid CreatedByUserId,
        string? CreatedByUserName,
        DateTimeOffset CreatedAt);

    public sealed record StandaloneDocumentsPageResponse(
        IReadOnlyList<StandaloneDocumentListResponse> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>Respuesta de la descarga: URL firmada y su vencimiento. Nada mas.</summary>
    public sealed record StandaloneDocumentDownloadResponse(string Url, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Respuesta de la carga de un lote. Sin nombre de archivo ni ruta de storage: el XLSX fuente
    /// contiene los datos completos de las partes y no se refleja en el contrato.
    /// </summary>
    public sealed record StandaloneBatchCreateResponse(Guid BatchId, string Status, int Total);

    /// <summary>
    /// Avance de un lote (CF-14). <c>isTerminal</c> es explicito para que el cliente detenga el
    /// polling sin tener que conocer la lista de estados terminales.
    /// </summary>
    public sealed record StandaloneBatchStatusResponse(
        Guid BatchId,
        string Status,
        int Total,
        int Generated,
        int Errors,
        int Processed,
        bool IsTerminal,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt);

    /// <summary>
    /// Una fila del lote en el seguimiento (CF-13). <c>ValidationErrors</c> llega como JSON crudo de
    /// la columna —<c>[{code, field, message}]</c>—; el mensaje NUNCA trae el valor capturado.
    /// </summary>
    public sealed record StandaloneBatchItemResponse(
        Guid Id,
        int? RowNumber,
        string DocumentType,
        string? Scenario,
        string Status,
        string? ErrorCode,
        string? ErrorField,
        IReadOnlyList<StandaloneBatchItemErrorResponse> ValidationErrors,
        string? Filename,
        DateTimeOffset CreatedAt);

    /// <summary>Error de una fila: codigo, campo y mensaje. Sin el valor que lo produjo.</summary>
    public sealed record StandaloneBatchItemErrorResponse(string? Code, string? Field, string? Message);

    public sealed record StandaloneBatchItemsPageResponse(
        IReadOnlyList<StandaloneBatchItemResponse> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>Cuerpo del prellenado de vehiculo. La placa manda; el documento del propietario es opcional.</summary>
    public sealed record PrefillVehiculoRequest(string? Placa, string? OwnerDocumentType, string? OwnerDocumentNumber);

    /// <summary>Cuerpo del prellenado de una parte juridica.</summary>
    public sealed record PrefillPersonaJuridicaRequest(string? Nit);

    /// <summary>Cuerpo del prellenado de una parte natural.</summary>
    public sealed record PrefillPersonaNaturalRequest(string? DocumentType, string? DocumentNumber);

    /// <summary>Un campo prellenado con su FUENTE. La fuente es por campo, no por respuesta.</summary>
    public sealed record PrefillFieldResponse(string Key, string? Value, string Source);

    /// <summary>Intento contra una fuente: deja ver que una se cayo sin invalidar el resto.</summary>
    public sealed record PrefillAttemptResponse(string Source, string Outcome, string? ErrorCode);

    /// <summary>
    /// Respuesta comun de los tres prellenados. Sin coincidencia: <c>found = false</c> y
    /// <c>fields</c> vacio, con 200. Nunca trae id de documento porque no se crea ninguno.
    /// </summary>
    public sealed record PrefillResponse(
        bool Found,
        string? Source,
        IReadOnlyList<PrefillFieldResponse> Fields,
        IReadOnlyList<PrefillAttemptResponse> Attempts);

    // internal (no private): Flit.Admin.Tests verifica el contrato de la respuesta invocando el
    // delegate directamente (que sea application/json y nunca application/pdf).
    internal static async Task<IResult> PreviewRuesAsync(
        HttpContext httpContext,
        RuesRequest request,
        [FromServices] PreviewRuesCompanyHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token inválido: falta claim tenant_id");
        }

        var result = await handler
            .HandleAsync(tenantId, request?.Nit, cancellationToken)
            .ConfigureAwait(false);

        return result.Error switch
        {
            null => Results.Ok(result),
            "invalid_request" => Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest),
            "provider_not_found" => Results.Json(new { error = "provider_not_found" }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(new { error = "provider_unavailable" }, statusCode: StatusCodes.Status502BadGateway),
        };
    }

    internal static async Task<IResult> GenerateRuesAsync(
        HttpContext httpContext,
        RuesRequest request,
        [FromServices] GenerateRuesDocumentHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token inválido: falta claim tenant_id");
        }

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized("Token inválido: falta claim sub");
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler
            .HandleAsync(
                new GenerateRuesDocumentCommand(
                    tenantId,
                    userId.Value,
                    request?.Nit,
                    string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            // 200 { id, status } en application/json, con cualquier Accept. Nunca application/pdf.
            GenerateRuesDocumentOutcome.Generated =>
                Results.Ok(new StandaloneDocumentGenerateResponse(result.Id!.Value, result.Status!)),

            GenerateRuesDocumentOutcome.InvalidRequest =>
                Results.Json(
                    new { error = result.ErrorCode, field = "nit" },
                    statusCode: StatusCodes.Status400BadRequest),

            GenerateRuesDocumentOutcome.RuesNotFound =>
                Results.Json(
                    new { error = result.ErrorCode, field = "nit", id = result.Id },
                    statusCode: StatusCodes.Status422UnprocessableEntity),

            GenerateRuesDocumentOutcome.ProviderNotFound =>
                Results.Json(
                    new { error = result.ErrorCode, id = result.Id },
                    statusCode: StatusCodes.Status503ServiceUnavailable),

            _ => Results.Json(
                new { error = result.ErrorCode, id = result.Id },
                statusCode: StatusCodes.Status502BadGateway),
        };
    }

    internal static async Task<IResult> GenerateTransferenciaAsync(
        HttpContext httpContext,
        TransferenciaRequest request,
        [FromServices] GenerateTransferenciaHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized("Token invalido: falta claim sub");
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();

        List<string> escenarios = [.. request?.Escenarios ?? []];
        if (!string.IsNullOrWhiteSpace(request?.Escenario))
        {
            escenarios.Add(request.Escenario);
        }

        var result = await handler
            .HandleAsync(
                new GenerateTransferenciaCommand(
                    tenantId,
                    userId.Value,
                    escenarios,
                    ToVehiculo(request?.Vehiculo),
                    ToParte(request?.Transferente),
                    ToParte(request?.Adquirente),
                    ToNegocio(request?.Negocio),
                    request?.Gravamen is { } gravamen
                        ? new TransferEncumbranceInput(
                            gravamen.GravamenActivo, gravamen.TieneLevantamientoOAutorizacion)
                        : null,
                    request?.RegimenAplicable is { } regimen
                        ? new RegimenDeclarationInput(
                            regimen.NingunaAplica, regimen.CondicionesDeclaradas, regimen.DeclaredAt)
                        : null,
                    request?.Leasing is { } leasing
                        ? new TransferLeasingInput(
                            leasing.TransferenteEsEntidadFinanciera,
                            leasing.NoContratoLeasing,
                            leasing.TipoOpcionCompra,
                            leasing.FechaTerminacion,
                            leasing.LocatarioNombre,
                            leasing.LocatarioTipoDoc,
                            leasing.LocatarioNoDoc)
                        : null,
                    string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            // 200 { id, status, advisories } en application/json. Nunca application/pdf.
            GenerateTransferenciaOutcome.Generated => Results.Ok(new StandaloneTransferGenerateResponse(
                result.Id!.Value,
                result.Status!,
                [.. result.Advisories.Select(ToIssueResponse)])),

            // 422 con la lista COMPLETA de VB bloqueantes: corregir de a un error por viaje es
            // inaceptable en un formulario de 35 campos.
            GenerateTransferenciaOutcome.ValidationFailed => Results.Json(
                new
                {
                    error = "validation_failed",
                    errors = result.Errors.Select(ToIssueResponse).ToArray(),
                },
                statusCode: StatusCodes.Status422UnprocessableEntity),

            GenerateTransferenciaOutcome.ScenarioNotImplemented => Results.Json(
                new
                {
                    error = GenerateTransferenciaHandler.ErrorScenarioNotImplemented,
                    field = result.ErrorField,
                },
                statusCode: StatusCodes.Status422UnprocessableEntity),

            // Cuerpo incompleto: no es una validacion normativa, es un contrato roto.
            _ => Results.Json(
                new { error = GenerateTransferenciaHandler.ErrorInvalidRequest, field = result.ErrorField },
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static TransferValidationIssueResponse ToIssueResponse(TransferValidationIssue issue) =>
        new(issue.Code, issue.Field, issue.Message);

    private static TransferVehicleInput? ToVehiculo(TransferenciaVehiculoRequest? v) => v is null
        ? null
        : new TransferVehicleInput(
            v.Placa, v.Marca, v.Linea, v.ModeloAnio, v.ClaseVehiculo, v.TipoCarroceria, v.Color,
            v.NoMotor, v.NoChasis, v.NoSerie, v.Servicio, v.NoLicenciaTransito, v.OrganismoTransito);

    private static TransferPartyInput? ToParte(TransferenciaParteRequest? p) => p is null
        ? null
        : new TransferPartyInput(
            p.TipoPersona, p.NombreRazonSocial, p.TipoDoc, p.NumeroDoc, p.DigitoVerificacion,
            p.Domicilio, p.RepresentanteLegal, p.CcRepresentanteLegal);

    private static TransferBusinessInput? ToNegocio(TransferenciaNegocioRequest? n) => n is null
        ? null
        : new TransferBusinessInput(
            n.TituloJuridico, n.DescripcionTitulo, n.PrecioLetras, n.PrecioNumeros,
            n.ContraprestacionDescripcion, n.FormaPago, n.AsumeRetencionFuente,
            n.AsumeDerechosTramite, n.AsumeImpuestoVehiculo, n.CiudadFirma, n.FechaFirma);

    internal static async Task<IResult> PrefillVehiculoAsync(
        HttpContext httpContext,
        PrefillVehiculoRequest request,
        [FromServices] PrefillVehiculoHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var result = await handler
            .HandleAsync(
                new PrefillVehiculoCommand(
                    tenantId,
                    request?.Placa,
                    request?.OwnerDocumentType,
                    request?.OwnerDocumentNumber),
                cancellationToken)
            .ConfigureAwait(false);

        return ToPrefillResult(result, "placa");
    }

    internal static async Task<IResult> PrefillPersonaJuridicaAsync(
        HttpContext httpContext,
        PrefillPersonaJuridicaRequest request,
        [FromServices] PrefillPersonaJuridicaHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var result = await handler
            .HandleAsync(new PrefillPersonaJuridicaCommand(tenantId, request?.Nit), cancellationToken)
            .ConfigureAwait(false);

        return ToPrefillResult(result, "nit");
    }

    internal static async Task<IResult> PrefillPersonaNaturalAsync(
        HttpContext httpContext,
        PrefillPersonaNaturalRequest request,
        [FromServices] PrefillPersonaNaturalHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var result = await handler
            .HandleAsync(
                new PrefillPersonaNaturalCommand(tenantId, request?.DocumentType, request?.DocumentNumber),
                cancellationToken)
            .ConfigureAwait(false);

        return ToPrefillResult(result, "documento");
    }

    /// <summary>
    /// Traduccion unica de los tres prellenados. <c>found = false</c> es un 200 con cuerpo vacio de
    /// campos —degradar, no fallar—; el 502 queda para cuando NINGUNA fuente contesto.
    /// </summary>
    private static IResult ToPrefillResult(PrefillResult result, string field) => result.Error switch
    {
        null => Results.Ok(new PrefillResponse(
            result.Found,
            result.Source,
            [.. result.Fields.Select(f => new PrefillFieldResponse(f.Key, f.Value, f.Source))],
            [.. result.Attempts.Select(a => new PrefillAttemptResponse(a.Source, a.Outcome, a.ErrorCode))])),

        "invalid_request" => Results.Json(
            new { error = "invalid_request", field },
            statusCode: StatusCodes.Status400BadRequest),

        _ => Results.Json(
            new { error = result.Error, field },
            statusCode: StatusCodes.Status502BadGateway),
    };

    /// <summary>
    /// Plantilla XLSX v1 (CF-11). Se genera al vuelo desde la MISMA lista de encabezados que valida
    /// la carga: la plantilla entregada y la exigida no pueden divergir.
    /// </summary>
    internal static IResult DescargarPlantillaLoteAsync(
        [FromServices] IStandaloneDocumentXlsxTemplate template)
    {
        var archivo = template.Build();
        return Results.File(archivo.Content, archivo.Mimetype, archivo.Filename);
    }

    /// <summary>
    /// Carga de lote (CF-11/CF-16). El archivo se lee del multipart a mano —y no como parametro
    /// IFormFile— para no arrastrar el binder de formularios a un grupo que por lo demas es JSON.
    /// </summary>
    internal static async Task<IResult> CrearLoteAsync(
        HttpContext httpContext,
        [FromServices] CreateBatchHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized("Token invalido: falta claim sub");
        }

        if (!httpContext.Request.HasFormContentType)
        {
            return Results.Json(
                new { error = "invalid_request", field = "file" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await httpContext.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files["file"] ?? (form.Files.Count > 0 ? form.Files[0] : null);

        if (file is null || file.Length == 0)
        {
            return Results.Json(
                new { error = "invalid_request", field = "file" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();

        await using var content = file.OpenReadStream();

        var result = await handler
            .HandleAsync(
                new CreateBatchCommand(
                    tenantId,
                    userId.Value,
                    file.FileName,
                    content,
                    string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            CreateBatchOutcome.Accepted => Results.Json(
                new StandaloneBatchCreateResponse(result.BatchId!.Value, result.Status!, result.Total),
                statusCode: StatusCodes.Status202Accepted),

            // CF-16 — replay: el lote que ya existia, sin crear nada nuevo.
            CreateBatchOutcome.AlreadyExists => Results.Ok(
                new StandaloneBatchCreateResponse(result.BatchId!.Value, result.Status!, result.Total)),

            // 422 con el codigo del rechazo. El archivo NO llego a storage.
            CreateBatchOutcome.Rejected => Results.Json(
                new { error = result.ErrorCode, field = "file" },
                statusCode: StatusCodes.Status422UnprocessableEntity),

            _ => Results.Json(
                new { error = result.ErrorCode ?? "invalid_request", field = "file" },
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    /// <summary>
    /// Avance del lote (CF-14). Es la ruta que el frontend sondea cada 4 segundos: no consulta
    /// storage, no firma nada y no toca ningun proveedor externo.
    /// </summary>
    internal static async Task<IResult> ObtenerEstadoLoteAsync(
        HttpContext httpContext,
        Guid batchId,
        [FromServices] GetBatchStatusHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var estado = await handler.HandleAsync(tenantId, batchId, cancellationToken).ConfigureAwait(false);

        // 404 escueto e identico para "no existe" y "es de otra compania" (CF-20/R3).
        return estado is null
            ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(new StandaloneBatchStatusResponse(
                estado.BatchId,
                estado.Status,
                estado.Total,
                estado.Generated,
                estado.Errors,
                estado.Processed,
                estado.IsTerminal,
                estado.CreatedAt,
                estado.CompletedAt));
    }

    /// <summary>Filas del lote con el detalle de sus errores (CF-13 en la interfaz).</summary>
    internal static async Task<IResult> ListarItemsLoteAsync(
        HttpContext httpContext,
        Guid batchId,
        [FromServices] ListBatchItemsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var q = httpContext.Request.Query;
        var page = int.TryParse(q["page"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1;
        var pageSize = int.TryParse(q["pageSize"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ps)
            ? ps
            : 20;

        var pagina = await handler
            .HandleAsync(tenantId, batchId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (pagina is null)
        {
            return Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(new StandaloneBatchItemsPageResponse(
            [.. pagina.Items.Select(ToBatchItemResponse)],
            pagina.Page,
            pagina.PageSize,
            pagina.Total));
    }

    /// <summary>
    /// ZIP del lote por streaming (CF-15).
    ///
    /// <para><b>Primero se decide, despues se escribe.</b> El plan (404 / 409 / lista de entradas)
    /// se resuelve ANTES de <c>Results.Stream</c>: en cuanto empieza el cuerpo, el status ya viajo y
    /// un fallo posterior solo puede cortar la conexion.</para>
    ///
    /// <para><b>AllowSynchronousIO</b>: <c>ZipArchive</c> no tiene API asincrona para cerrar el
    /// directorio central y Kestrel prohibe por defecto la escritura sincrona sobre el cuerpo de la
    /// respuesta. Sin esta linea la descarga muere en el <c>Dispose</c> del archivo con "Synchronous
    /// operations are disallowed". Se habilita SOLO en esta peticion y no afecta la cota de memoria:
    /// el contenido se sigue copiando por bloques, un binario a la vez.</para>
    /// </summary>
    internal static async Task<IResult> DescargarZipLoteAsync(
        HttpContext httpContext,
        Guid batchId,
        [FromServices] DownloadBatchZipHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var plan = await handler.PrepareAsync(tenantId, batchId, cancellationToken).ConfigureAwait(false);

        if (plan.Outcome == StandaloneBatchZipOutcome.NotFound)
        {
            return Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
        }

        if (plan.Outcome == StandaloneBatchZipOutcome.NoDocuments)
        {
            // Respuesta EXPLICITA: el lote existe y no tiene nada descargable. Un ZIP vacio pasaria
            // por descarga corrupta y no explicaria que todas las filas quedaron en error.
            return Results.Json(
                new
                {
                    error = "no_documents",
                    message = "El lote no tiene documentos generados para descargar.",
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        var cuerpo = httpContext.Features.Get<IHttpBodyControlFeature>();
        if (cuerpo is not null)
        {
            cuerpo.AllowSynchronousIO = true;
        }

        var entradas = plan.Entries;

        return Results.Stream(
            stream => handler.WriteAsync(entradas, stream, cancellationToken),
            "application/zip",
            plan.Filename);
    }

    /// <summary>
    /// Traduce una fila del lote al contrato HTTP. <c>validation_errors</c> se parsea con
    /// <c>JsonDocument</c> —sin serializador reflexivo— y un JSON corrupto degrada a lista vacia:
    /// una columna mal escrita no puede tumbar el seguimiento del lote entero.
    /// </summary>
    private static StandaloneBatchItemResponse ToBatchItemResponse(StandaloneDocumentBatchItem item) => new(
        item.Id,
        item.RowNumber,
        item.DocumentType,
        item.Scenario,
        item.Status,
        item.ErrorCode,
        item.ErrorField,
        ParseValidationErrors(item.ValidationErrors),
        item.Filename,
        item.CreatedAt);

    private static List<StandaloneBatchItemErrorResponse> ParseValidationErrors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var documento = JsonDocument.Parse(json);
            if (documento.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var errores = new List<StandaloneBatchItemErrorResponse>();
            foreach (var elemento in documento.RootElement.EnumerateArray())
            {
                if (elemento.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                errores.Add(new StandaloneBatchItemErrorResponse(
                    Texto(elemento, "code"),
                    Texto(elemento, "field"),
                    Texto(elemento, "message")));
            }

            return errores;
        }
        catch (JsonException)
        {
            return [];
        }

        static string? Texto(JsonElement elemento, string propiedad) =>
            elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String
                ? valor.GetString()
                : null;
    }

    internal static async Task<IResult> ListDocumentosAsync(
        HttpContext httpContext,
        [FromServices] ListStandaloneDocumentsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var q = httpContext.Request.Query;

        if (!TryParseDate(q["dateFrom"], out var dateFrom) || !TryParseDate(q["dateTo"], out var dateTo))
        {
            return Results.Json(
                new { error = "invalid_request", field = "date" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        // "hasta el 9" debe incluir el 9 completo: una fecha sin hora se convierte en el limite
        // superior EXCLUSIVO del dia siguiente.
        if (dateTo is { } hasta && !q["dateTo"].ToString().Contains('T', StringComparison.Ordinal))
        {
            dateTo = hasta.AddDays(1);
        }

        Guid? requestedTenantId = Guid.TryParse(q["tenantId"], out var otroTenant) ? otroTenant : null;
        // Solo el literal "true" activa el listado global: cualquier otro valor —incluido el
        // parametro presente pero vacio— se lee como false. Ver todas las companias no puede
        // depender de que una cadena rara se interprete como afirmativa.
        var allTenants = bool.TryParse(q["allTenants"], out var todas) && todas;
        Guid? userId = Guid.TryParse(q["userId"], out var autor) ? autor : null;
        // CF-18 en I3: filtro por lote. Convive con los de tipo, fecha, usuario y estado (AND).
        Guid? batchId = Guid.TryParse(q["batchId"], out var lote) ? lote : null;
        var documentType = q["documentType"].ToString();

        var page = await handler
            .HandleAsync(
                new ListStandaloneDocumentsQuery
                {
                    TenantId = tenantId,
                    IsSuperAdmin = IsSuperAdmin(httpContext.User),
                    RequestedTenantId = requestedTenantId,
                    AllTenants = allTenants,
                    DocumentType = string.IsNullOrWhiteSpace(documentType) ? null : documentType,
                    Statuses = [.. q["status"].Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!)],
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    CreatedByUserId = userId,
                    BatchId = batchId,
                    Page = int.TryParse(q["page"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1,
                    PageSize = int.TryParse(q["pageSize"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ps) ? ps : 20,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new StandaloneDocumentsPageResponse(
            [.. page.Items.Select(ToResponse)],
            page.Page,
            page.PageSize,
            page.Total));
    }

    internal static async Task<IResult> DownloadDocumentoAsync(
        HttpContext httpContext,
        Guid id,
        [FromServices] GetStandaloneDocumentDownloadHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        // SIEMPRE el tenant del JWT: no hay parametro que permita descargar de otra compania.
        var result = await handler
            .HandleAsync(tenantId, id, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            StandaloneDocumentDownloadOutcome.Ok =>
                Results.Ok(new StandaloneDocumentDownloadResponse(result.Link!.Url, result.Link.ExpiresAt)),

            // 409: existe en MI tenant pero no esta generado. Se puede decir el estado porque el
            // documento es mio; no hay URL, ni snapshot, ni PII.
            StandaloneDocumentDownloadOutcome.NotGenerated =>
                Results.Json(
                    new { error = "conflict", status = result.Status },
                    statusCode: StatusCodes.Status409Conflict),

            // 404 escueto: no existe, es de otro tenant o perdio su binario. Un cuerpo distinto por
            // caso permitiria distinguirlos, y con eso enumerar documentos ajenos.
            _ => Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound),
        };
    }

    private static StandaloneDocumentListResponse ToResponse(StandaloneDocumentListItem item) => new(
        item.Id,
        item.DocumentType,
        item.Scenario,
        item.Status,
        item.ErrorCode,
        item.Filename,
        item.CompanyName,
        item.CreatedByUserId,
        item.CreatedByUserName,
        item.CreatedAt);

    private static bool TryParseDate(string? value, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date))
        {
            parsed = date;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rol del JWT. Solo habilita el filtro OPCIONAL tenantId del listado (metadata global, CF-20);
    /// no concede descarga cruzada, no da acceso a snapshots y no protege ninguna ruta: la
    /// autorizacion de este modulo es SIEMPRE por permiso.
    /// </summary>
    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole);

    private static IResult Unauthorized(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status401Unauthorized);

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
