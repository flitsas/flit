using System.Diagnostics;
using System.Text.Json;
using Flit.Modules.Quipux.Domain.Codigos;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Mapeo;
using Flit.Modules.Quipux.Domain.Puertos;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Quipux.Application.UseCases.RegistrarDocumento;

/// <summary>
/// Radica en Quipux una submission pendiente y, si Quipux la acepta (código 81), transiciona el
/// trámite <c>preparado → entregado</c>.
/// </summary>
/// <remarks>
/// <para>Corrige, respecto a FLIT 1.0, cuatro defectos verificados en su código:</para>
/// <list type="number">
///   <item>
///   <b>Trámites huérfanos.</b> En 1.0 el estado se persistía ANTES que el log y sin transacción; si
///   fallaba en medio, el trámite quedaba en estado 5 sin log 81 y la query de seguimiento —que exige
///   ese log vía INNER JOIN— no lo volvía a mirar NUNCA. Aquí el cambio de estado de la submission y
///   su evento van en el MISMO <c>SaveChanges</c> (<see cref="IQuipuxSubmissionRepository.UpdateAsync"/>),
///   y la transición del trámite se hace ANTES de marcar la submission: si el proceso muere entre
///   ambos, la submission sigue <c>pendiente</c>, el siguiente ciclo consulta, ve el 81 y converge.
///   El orden inverso sí dejaría el par (submission <c>registrado</c>, trámite <c>preparado</c>)
///   irrecuperable, porque el sondeo intentaría <c>entregado → aprobado</c> desde <c>preparado</c>.
///   </item>
///   <item>
///   <b>Duplicados en Quipux.</b> El job <c>validateStatusDocumentForTimeOut</c> de 1.0 se ABSORBE
///   aquí: si la submission ya tuvo un intento (<c>attempts &gt; 0</c>), se CONSULTA antes de
///   re-radicar, porque Quipux pudo haber radicado el documento aunque la respuesta HTTP expirara.
///   Es posible únicamente porque el <c>document_name</c> es estable entre reintentos.
///   </item>
///   <item>
///   <b>Corrupción del PDF en S3.</b> La subida ocurre UNA vez y ANTES de radicar. En 1.0 vivía dentro
///   de <c>registerDocument</c>, así que el reintento por 401 re-subía con el payload ya mutado y
///   sobrescribía el PDF bueno con 0 bytes.
///   </item>
///   <item>
///   <b>Tipo de documento no mapeado.</b> Si el documento del propietario no traduce a un código
///   Quipux, NO se radica. En 1.0 se enviaba el string "Se desconoce el tipo de documento" dentro de
///   un campo numérico.
///   </item>
/// </list>
/// <para>
/// Y una regla del repo que 1.0 no cumplía: el <c>detail</c> de la bitácora va SANITIZADO (códigos e
/// ids). 1.0 persistía <c>jsonSend</c>/<c>jsonResponse</c> completos, con PII del propietario dentro.
/// </para>
/// </remarks>
public sealed class RegistrarDocumentoQuipuxHandler
{
    private readonly IQuipuxSettingsRepository _settings;
    private readonly IQuipuxSubmissionRepository _submissions;
    private readonly IQuipuxClient _client;
    private readonly IQuipuxDocumentUploader _uploader;
    private readonly IProcedureInstanceRepository _instances;
    private readonly IProcedureTypeRepository _procedureTypes;
    private readonly ITramiteLifecycleService _lifecycle;
    private readonly IQuipuxAuditLog _audit;
    private readonly ILogger<RegistrarDocumentoQuipuxHandler> _logger;

    public RegistrarDocumentoQuipuxHandler(
        IQuipuxSettingsRepository settings,
        IQuipuxSubmissionRepository submissions,
        IQuipuxClient client,
        IQuipuxDocumentUploader uploader,
        IProcedureInstanceRepository instances,
        IProcedureTypeRepository procedureTypes,
        ITramiteLifecycleService lifecycle,
        IQuipuxAuditLog audit,
        ILogger<RegistrarDocumentoQuipuxHandler> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RegistrarDocumentoQuipuxResult> HandleAsync(
        RegistrarDocumentoQuipuxCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Interruptor maestro. Una fila a medio llenar produciría errores contra Quipux en vez de un
        // no-op: EstaCompleta() lo convierte en inercia. No se usa IsDevelopment() para esto — el
        // compose de PDN corre con ASPNETCORE_ENVIRONMENT=Development.
        var settings = await _settings.GetAsync(cancellationToken);
        if (settings is null || !settings.EstaCompleta())
        {
            return RegistrarDocumentoQuipuxResult.Deshabilitado();
        }

        var submission = await _submissions.GetByIdAsync(command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return RegistrarDocumentoQuipuxResult.NoEncontrado();
        }

        if (!string.Equals(submission.Status, QuipuxSubmissionEstado.Pendiente, StringComparison.Ordinal))
        {
            return RegistrarDocumentoQuipuxResult.EstadoInvalido(submission.Id);
        }

        // --- Absorbe validateStatusDocumentForTimeOut (FLIT 1.0) ---
        if (submission.RequiereConsultaPrevia)
        {
            var previa = await ConsultarAntesDeReintentarAsync(submission, settings, command, cancellationToken);
            if (previa is not null)
            {
                return previa;
            }
        }

        var (contexto, errorContexto) = await ResolverContextoAsync(submission, cancellationToken);
        if (errorContexto is not null)
        {
            return await DeadLetterAsync(
                submission, errorContexto, QuipuxStage.RegistroError, command, cancellationToken);
        }

        // --- Subida a S3: UNA vez, ANTES de radicar (bug 3 de 1.0) ---
        string keyS3;
        try
        {
            keyS3 = await _uploader.UploadAsync(
                submission.AttachmentId, submission.DocumentName, cancellationToken);
        }
        catch (QuipuxUploadException ex)
        {
            await _audit.WriteAsync(
                submission.TenantId, submission.Id, QuipuxStage.S3Subido,
                ex.IsTransient ? QuipuxOutcome.ErrorTransitorio : QuipuxOutcome.ErrorDefinitivo,
                new { motivo = RegistrarDocumentoQuipuxMotivos.SubidaS3Fallida, transitorio = ex.IsTransient },
                command.CorrelationId, cancellationToken);

            RegistrarDocumentoQuipuxLog.SubidaFallida(_logger, ex, submission.Id, ex.IsTransient);

            return ex.IsTransient
                ? await ErrorReintentableAsync(
                    submission, settings, RegistrarDocumentoQuipuxMotivos.SubidaS3Fallida, null,
                    QuipuxStage.S3Subido, command, cancellationToken)
                : await DeadLetterAsync(
                    submission, RegistrarDocumentoQuipuxMotivos.SubidaS3Fallida, QuipuxStage.S3Subido,
                    command, cancellationToken);
        }

        await _audit.WriteAsync(
            submission.TenantId, submission.Id, QuipuxStage.S3Subido, QuipuxOutcome.Ok,
            // La key S3 no es PII: es {prefijo}{document_name}.pdf, ya derivado de datos públicos.
            new { key = keyS3, attachment_id = submission.AttachmentId },
            command.CorrelationId, cancellationToken);

        var request = new QuipuxRegisterRequest
        {
            // El que no aplica va "" (no null): es el contrato de Quipux tal cual lo espera.
            Placa = contexto!.Placa ?? string.Empty,
            Vin = contexto.Vin ?? string.Empty,
            CodigoDivipo = submission.DivipoCode,
            Consumidor = settings.ConsumerCode,
            TipoTramite = submission.QuipuxProcedureType,
            TipoRequisito = submission.QuipuxRequirementType,
            Documento = submission.DocumentName,
            TipoDocumentoPropietario = contexto.TipoDocumentoPropietario,
            NroDocumentoPropietario = contexto.NroDocumentoPropietario,
            // Funcionario que radica: en 1.0 iba hardcodeado en el servicio (tipo 3 = NIT + NIT de
            // FLIT). Aquí sale de admin.quipux_settings, así que cambiarlo es un UPDATE.
            TipoDocumentoFuncionario = settings.OfficerDocumentType,
            NroDocumentoFuncionario = settings.OfficerDocumentNumber,
            // La key S3, no el PDF: el bucket es de Quipux y él lee el documento de ahí.
            ContenidoDocumento = keyS3,
        };

        await _audit.WriteAsync(
            submission.TenantId, submission.Id, QuipuxStage.RegistroEnviado, QuipuxOutcome.Ok,
            new
            {
                tipo_tramite = request.TipoTramite,
                tipo_requisito = request.TipoRequisito,
                codigo_divipo = request.CodigoDivipo,
                usa_placa = !string.IsNullOrEmpty(request.Placa),
                usa_vin = !string.IsNullOrEmpty(request.Vin),
                intento = submission.Attempts,
            },
            command.CorrelationId, cancellationToken);

        // Instrumentación de duración (HU #10793): elapsed de la llamada HTTP de registro, para el
        // LOG QX. Observabilidad pura — no cambia contrato, reintentos ni timeouts. Origen = worker
        // registrador.
        var inicioRegistro = Stopwatch.GetTimestamp();

        QuipuxRegisterResult respuesta;
        try
        {
            respuesta = await _client.RegisterDocumentAsync(request, cancellationToken);
        }
        catch (QuipuxException ex)
        {
            await _audit.WriteAsync(
                submission.TenantId, submission.Id, QuipuxStage.RegistroRespuesta,
                ex.IsTransient ? QuipuxOutcome.ErrorTransitorio : QuipuxOutcome.ErrorDefinitivo,
                new
                {
                    motivo = RegistrarDocumentoQuipuxMotivos.LlamadaRegistroFallida,
                    transitorio = ex.IsTransient,
                    duration_ms = ElapsedMs(inicioRegistro),
                    origen = QuipuxJobNames.Register,
                },
                command.CorrelationId, cancellationToken);

            RegistrarDocumentoQuipuxLog.RegistroFallido(_logger, ex, submission.Id, ex.IsTransient);

            // Aunque el fallo sea "definitivo" para el cliente HTTP, la submission NO se dead-letterea
            // aquí: el POST pudo haber llegado. El siguiente intento CONSULTA primero (RequiereConsultaPrevia),
            // que es justamente la vía segura para descubrirlo sin duplicar.
            return await ErrorReintentableAsync(
                submission, settings, RegistrarDocumentoQuipuxMotivos.LlamadaRegistroFallida,
                QuipuxCodigos.RegistroError, QuipuxStage.RegistroError, command, cancellationToken);
        }

        if (!QuipuxCodigos.EsRegistroExitoso(respuesta.Codigo))
        {
            await _audit.WriteAsync(
                submission.TenantId, submission.Id, QuipuxStage.RegistroRespuesta,
                QuipuxOutcome.ErrorTransitorio,
                new
                {
                    codigo = respuesta.Codigo,
                    duration_ms = ElapsedMs(inicioRegistro),
                    origen = QuipuxJobNames.Register,
                },
                command.CorrelationId, cancellationToken);

            return await ErrorReintentableAsync(
                submission, settings, RegistrarDocumentoQuipuxMotivos.RespuestaNoExitosa,
                respuesta.Codigo, QuipuxStage.RegistroError, command, cancellationToken);
        }

        return await MarcarRegistradoAsync(
            submission, respuesta.Codigo, yaEstabaEnQuipux: false, ElapsedMs(inicioRegistro),
            command, cancellationToken);
    }

    /// <summary>
    /// Consulta el estado ANTES de re-radicar. Devuelve un resultado SOLO si el ciclo debe terminar
    /// aquí (Quipux ya lo tenía, o la consulta no se pudo resolver); null = seguir y radicar.
    /// </summary>
    /// <remarks>
    /// Si la consulta falla, NO se radica: sin saber si Quipux ya tiene el documento, radicar es
    /// arriesgarse al duplicado que 1.0 producía. Se prefiere perder un ciclo de 15 min.
    /// </remarks>
    private async Task<RegistrarDocumentoQuipuxResult?> ConsultarAntesDeReintentarAsync(
        QuipuxSubmission submission,
        QuipuxSettings settings,
        RegistrarDocumentoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(
            submission.TenantId, submission.Id, QuipuxStage.ConsultaEnviada, QuipuxOutcome.Ok,
            new { motivo = "consulta_previa_a_reintento", intento = submission.Attempts },
            command.CorrelationId, cancellationToken);

        // Instrumentación de duración (HU #10793): esta consulta previa la hace el propio worker
        // registrador, así que el origen sigue siendo Register (no el sondeo).
        var inicioConsultaPrevia = Stopwatch.GetTimestamp();

        QuipuxValidateStatusResult consulta;
        try
        {
            consulta = await _client.ValidateStatusAsync(
                new QuipuxValidateStatusRequest
                {
                    // La correlación con Quipux es el nombre del documento: por eso NO se recalcula
                    // entre intentos.
                    Documento = submission.DocumentName,
                    CodigoDivipo = submission.DivipoCode,
                    Consumidor = settings.ConsumerCode,
                },
                cancellationToken);
        }
        catch (QuipuxException ex)
        {
            await _audit.WriteAsync(
                submission.TenantId, submission.Id, QuipuxStage.ConsultaError,
                ex.IsTransient ? QuipuxOutcome.ErrorTransitorio : QuipuxOutcome.ErrorDefinitivo,
                new
                {
                    motivo = RegistrarDocumentoQuipuxMotivos.ConsultaPreviaFallida,
                    transitorio = ex.IsTransient,
                    duration_ms = ElapsedMs(inicioConsultaPrevia),
                    origen = QuipuxJobNames.Register,
                },
                command.CorrelationId, cancellationToken);

            RegistrarDocumentoQuipuxLog.ConsultaPreviaFallida(_logger, ex, submission.Id);

            return await ErrorReintentableAsync(
                submission, settings, RegistrarDocumentoQuipuxMotivos.ConsultaPreviaFallida, null,
                QuipuxStage.ConsultaError, command, cancellationToken);
        }

        var duracionConsultaPrevia = ElapsedMs(inicioConsultaPrevia);

        await _audit.WriteAsync(
            submission.TenantId, submission.Id, QuipuxStage.ConsultaRespuesta, QuipuxOutcome.Ok,
            new
            {
                codigo = consulta.Codigo,
                estado_tramite = consulta.EstadoTramiteCodigo,
                duration_ms = duracionConsultaPrevia,
                origen = QuipuxJobNames.Register,
            },
            command.CorrelationId, cancellationToken);

        if (!QuipuxCodigos.EsRegistroExitoso(consulta.Codigo))
        {
            // Quipux no lo tiene: se puede radicar sin riesgo de duplicado.
            return null;
        }

        RegistrarDocumentoQuipuxLog.YaRegistradoEnQuipux(_logger, submission.Id, submission.Attempts);

        return await MarcarRegistradoAsync(
            submission, consulta.Codigo, yaEstabaEnQuipux: true, duracionConsultaPrevia,
            command, cancellationToken);
    }

    /// <summary>
    /// Camino de éxito: transiciona el trámite y marca la submission + su evento en un solo
    /// <c>SaveChanges</c>.
    /// </summary>
    /// <remarks>
    /// UNA sola transición <c>preparado → entregado</c>. En 1.0 eran dos updates (4 → 5) que replicaban
    /// a mano la cascada del flujo no-Quipux; en 2.0 el estado intermedio "enviado" no existe, así que
    /// el doble update desaparece.
    /// </remarks>
    private async Task<RegistrarDocumentoQuipuxResult> MarcarRegistradoAsync(
        QuipuxSubmission submission,
        int codigo,
        bool yaEstabaEnQuipux,
        long durationMs,
        RegistrarDocumentoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        var (transicionado, errorTransicion) = await TransicionarAsync(
            submission.ProcedureInstanceId, submission.TenantId, TramiteEstado.Entregado,
            reason: null, cancellationToken);

        if (!transicionado)
        {
            // Quipux SÍ tiene el documento, pero el trámite no pudo avanzar. No se marca la submission:
            // dejarla pendiente hace que el próximo ciclo consulte, vea el 81 y reintente la transición.
            // Marcarla sería reproducir el huérfano de 1.0.
            RegistrarDocumentoQuipuxLog.TransicionFallida(
                _logger, submission.ProcedureInstanceId, errorTransicion ?? "desconocido");

            await _audit.WriteAsync(
                submission.TenantId, submission.Id, QuipuxStage.RegistroError, QuipuxOutcome.ErrorTransitorio,
                new
                {
                    motivo = RegistrarDocumentoQuipuxMotivos.TransicionFallida,
                    error = errorTransicion,
                    codigo,
                    duration_ms = durationMs,
                    origen = QuipuxJobNames.Register,
                },
                command.CorrelationId, cancellationToken);

            return RegistrarDocumentoQuipuxResult.ErrorTransitorio(
                submission.Id, RegistrarDocumentoQuipuxMotivos.TransicionFallida, codigo);
        }

        var ahora = DateTimeOffset.UtcNow;
        submission.Status = QuipuxSubmissionEstado.Registrado;
        submission.QxRegisterCode = codigo;
        submission.RegisteredAt = ahora;
        submission.UpdatedAt = ahora;

        // ATOMICIDAD: estado + evento en el MISMO SaveChanges. Es el fix del bug más grave de 1.0.
        await _submissions.UpdateAsync(
            submission,
            NuevoEvento(
                submission, QuipuxStage.RegistroRespuesta, QuipuxOutcome.Ok,
                new
                {
                    codigo,
                    ya_estaba_en_quipux = yaEstabaEnQuipux,
                    duration_ms = durationMs,
                    origen = QuipuxJobNames.Register,
                },
                command.CorrelationId, ahora),
            cancellationToken);

        RegistrarDocumentoQuipuxLog.Registrado(_logger, submission.Id, submission.ProcedureInstanceId, codigo);

        return yaEstabaEnQuipux
            ? RegistrarDocumentoQuipuxResult.YaRegistradoEnQuipux(submission.Id, codigo, transicionado: true)
            : RegistrarDocumentoQuipuxResult.Registrado(submission.Id, codigo, transicionado: true);
    }

    /// <summary>
    /// Fallo reintentable: la submission sigue <c>pendiente</c> hasta agotar <c>max_attempts</c>;
    /// al agotarlos pasa a <c>fallido</c> con evento <c>dead_letter</c> y log <c>Critical</c>.
    /// </summary>
    /// <remarks>
    /// <c>attempts</c> ya viene incrementado por el claim (<c>ClaimNextPendienteAsync</c>), así que
    /// <c>Attempts &gt;= MaxAttempts</c> significa "este era el último intento".
    /// </remarks>
    private async Task<RegistrarDocumentoQuipuxResult> ErrorReintentableAsync(
        QuipuxSubmission submission,
        QuipuxSettings settings,
        string motivo,
        int? codigo,
        string stage,
        RegistrarDocumentoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var agotado = submission.Attempts >= settings.MaxAttempts;

        if (codigo is not null)
        {
            submission.QxRegisterCode = codigo;
        }

        submission.UpdatedAt = ahora;

        if (agotado)
        {
            submission.Status = QuipuxSubmissionEstado.Fallido;
            submission.CompletedAt = ahora;
        }

        await _submissions.UpdateAsync(
            submission,
            NuevoEvento(
                submission,
                agotado ? QuipuxStage.DeadLetter : stage,
                QuipuxOutcome.ErrorTransitorio,
                new { motivo, codigo, intentos = submission.Attempts, max_intentos = settings.MaxAttempts },
                command.CorrelationId,
                ahora),
            cancellationToken);

        if (agotado)
        {
            RegistrarDocumentoQuipuxLog.DeadLetter(
                _logger, submission.Id, submission.ProcedureInstanceId, motivo, submission.Attempts);
            return RegistrarDocumentoQuipuxResult.Fallido(submission.Id, motivo, codigo);
        }

        return RegistrarDocumentoQuipuxResult.ErrorTransitorio(submission.Id, motivo, codigo);
    }

    /// <summary>
    /// Fallo que reintentar NO arregla (dato del trámite o parametrización). Va directo a
    /// <c>fallido</c>: gastar los 5 intentos contra un dato inmutable solo retrasa la visibilidad.
    /// Como <c>fallido</c> queda FUERA del índice único parcial de submission activa, corregir el
    /// dato y volver a encolar crea una submission nueva y limpia.
    /// </summary>
    private async Task<RegistrarDocumentoQuipuxResult> DeadLetterAsync(
        QuipuxSubmission submission,
        string motivo,
        string stage,
        RegistrarDocumentoQuipuxCommand command,
        CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        submission.Status = QuipuxSubmissionEstado.Fallido;
        submission.CompletedAt = ahora;
        submission.UpdatedAt = ahora;

        await _submissions.UpdateAsync(
            submission,
            NuevoEvento(
                submission, QuipuxStage.DeadLetter, QuipuxOutcome.ErrorDefinitivo,
                new { motivo, stage_origen = stage }, command.CorrelationId, ahora),
            cancellationToken);

        RegistrarDocumentoQuipuxLog.DeadLetter(
            _logger, submission.Id, submission.ProcedureInstanceId, motivo, submission.Attempts);

        return RegistrarDocumentoQuipuxResult.ErrorDefinitivo(submission.Id, motivo);
    }

    /// <summary>
    /// Resuelve lo que el payload necesita del trámite: identificador del vehículo y propietario.
    /// </summary>
    private async Task<(RegistroContexto? Contexto, string? Error)> ResolverContextoAsync(
        QuipuxSubmission submission,
        CancellationToken cancellationToken)
    {
        var instance = await _instances.GetByIdWithDetailsAsync(
            submission.ProcedureInstanceId, submission.TenantId, cancellationToken);

        if (instance is null)
        {
            return (null, RegistrarDocumentoQuipuxMotivos.TramiteNoEncontrado);
        }

        var procedureType = await _procedureTypes.GetByIdAsync(instance.ProcedureTypeId, cancellationToken);
        var mapa = QuipuxTipoTramiteMap.Parse(procedureType?.ExternalRefs);
        if (mapa is null)
        {
            return (null, RegistrarDocumentoQuipuxMotivos.TipoSinParametrizacionQuipux);
        }

        var fieldValues = LeerFieldValues(instance);
        var resuelto = mapa.Resolve(fieldValues);

        // Placa y VIN NO son columnas: se proyectan de procedure_instance_field_values.
        string? placa = null;
        string? vin = null;

        if (resuelto.UsaPlaca)
        {
            placa = Leer(fieldValues, resuelto.CampoPlaca!);
        }
        else if (resuelto.UsaVin)
        {
            vin = Leer(fieldValues, resuelto.CampoVin!);
        }

        if (string.IsNullOrWhiteSpace(placa) && string.IsNullOrWhiteSpace(vin))
        {
            return (null, RegistrarDocumentoQuipuxMotivos.SinIdentificadorVehiculo);
        }

        var propietario = ResolverPropietario(instance);
        if (propietario is null || string.IsNullOrWhiteSpace(propietario.DocumentNumber))
        {
            return (null, RegistrarDocumentoQuipuxMotivos.PropietarioNoResuelto);
        }

        // Sin código no se radica. Ver RegistrarDocumentoQuipuxMotivos.TipoDocumentoPropietarioNoMapea.
        if (!QuipuxTipoDocumento.TryMap(propietario.DocumentType, out var tipoDocumento))
        {
            return (null, RegistrarDocumentoQuipuxMotivos.TipoDocumentoPropietarioNoMapea);
        }

        return (new RegistroContexto(placa, vin, tipoDocumento, propietario.DocumentNumber.Trim()), null);
    }

    /// <summary>
    /// Propietario del vehículo AL RADICAR: en traspaso el <c>vendedor</c> (dueño actual, el trámite
    /// aún no se ha aprobado), en matrícula inicial el <c>comprador</c>. Es exactamente el criterio ya
    /// establecido en el repo para el Certificado de Improntas
    /// (<c>GenerarImprontaAttachmentHandler</c>), replicado aquí para no tener dos definiciones de
    /// "propietario" que se desincronicen.
    /// </summary>
    /// <remarks>
    /// No existe un <c>ActorType</c> "owner"/"propietario" en el modelo: los roles reales son
    /// <c>vendedor</c> y <c>comprador</c> (<c>ProcedureInstanceActor.ActorType</c>), así que el
    /// propietario se DERIVA de la modalidad del trámite.
    /// </remarks>
    private static ProcedureInstanceActor? ResolverPropietario(ProcedureInstance instance)
    {
        var rol = EsTraspaso(instance) ? "vendedor" : "comprador";
        return instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ¿El trámite es un traspaso? Prioriza <c>tipologia_codigo</c> y cae a <c>modalidad_entrada</c>
    /// para las instancias antiguas sin tipología persistida (mismo criterio que
    /// <c>TipologiaResolver.ResolveCodigo</c>, que vive en <c>Flit.Tramites.Application</c> y este
    /// módulo no referencia).
    /// </summary>
    private static bool EsTraspaso(ProcedureInstance instance)
    {
        if (!string.IsNullOrEmpty(instance.TipologiaCodigo)
            && TramiteTipologiaCatalog.IsValid(instance.TipologiaCodigo))
        {
            return string.Equals(
                instance.TipologiaCodigo,
                TramiteTipologiaCatalog.CodigoTraspasoStandard,
                StringComparison.OrdinalIgnoreCase);
        }

        return TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
            == TramiteModalidadEntrada.Traspaso;
    }

    /// <summary>
    /// Transición idempotente. Si el trámite YA está en el destino, la transición ocurrió en un ciclo
    /// anterior que murió antes de marcar la submission: no es un error, es la convergencia esperada.
    /// </summary>
    private async Task<(bool Ok, string? Error)> TransicionarAsync(
        Guid instanceId,
        Guid tenantId,
        string destino,
        string? reason,
        CancellationToken cancellationToken)
    {
        var instance = await _instances.GetByIdAsync(instanceId, tenantId, cancellationToken);
        if (instance is null)
        {
            return (false, TramiteEstadoErrores.NoEncontrado);
        }

        if (string.Equals(instance.Status, destino, StringComparison.Ordinal))
        {
            return (true, null);
        }

        var outcome = await _lifecycle.TransitionAsync(
            new TramiteTransitionCommand(instanceId, tenantId, destino, reason, ChangedByUserId: null),
            cancellationToken);

        return outcome.Success ? (true, null) : (false, outcome.ErrorCode);
    }

    private static QuipuxSubmissionEvent NuevoEvento(
        QuipuxSubmission submission,
        string stage,
        string outcome,
        object detalle,
        Guid? correlationId,
        DateTimeOffset ahora) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = submission.TenantId,
            SubmissionId = submission.Id,
            Stage = stage,
            Outcome = outcome,
            Detail = Detalle(detalle),
            CorrelationId = correlationId,
            OccurredAt = ahora,
        };

    /// <summary>
    /// Serializa el detalle de bitácora. Solo se le pasan objetos ya sanitizados a mano (códigos,
    /// ids, banderas) — NUNCA el request/response crudos, que llevan PII del propietario.
    /// </summary>
    private static string Detalle(object detalle) => JsonSerializer.Serialize(detalle);

    /// <summary>Milisegundos transcurridos desde un timestamp de <see cref="Stopwatch"/> (HU #10793).</summary>
    private static long ElapsedMs(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private static string? Leer(Dictionary<string, string?> fieldValues, string campo) =>
        fieldValues.TryGetValue(campo, out var valor) && !string.IsNullOrWhiteSpace(valor)
            ? valor.Trim()
            : null;

    /// <summary>
    /// Field values indexados por clave. Si la instancia trajera dos filas con la misma
    /// <c>field_key</c> (no hay unique en BD), gana la última: <c>ToDictionary</c> lanzaría.
    /// </summary>
    private static Dictionary<string, string?> LeerFieldValues(ProcedureInstance instance)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var fv in instance.FieldValues)
        {
            valores[fv.FieldKey] = fv.ValueText;
        }

        return valores;
    }

    /// <summary>Lo que el payload de radicación necesita del trámite, ya validado.</summary>
    private sealed record RegistroContexto(
        string? Placa,
        string? Vin,
        int TipoDocumentoPropietario,
        string NroDocumentoPropietario);
}

/// <summary>Logging source-generated (CA1848) de la radicación. Sin PII ni payloads: ids y códigos.</summary>
internal static partial class RegistrarDocumentoQuipuxLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux: submission {SubmissionId} radicada (trámite {InstanceId}, código {Codigo}); trámite entregado.")]
    public static partial void Registrado(ILogger logger, Guid submissionId, Guid instanceId, int codigo);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux: la submission {SubmissionId} ya estaba radicada en Quipux tras {Intentos} intento(s); no se re-radica.")]
    public static partial void YaRegistradoEnQuipux(ILogger logger, Guid submissionId, int intentos);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: falló la subida del PDF de la submission {SubmissionId} (transitorio={Transitorio}).")]
    public static partial void SubidaFallida(ILogger logger, Exception ex, Guid submissionId, bool transitorio);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: falló la radicación de la submission {SubmissionId} (transitorio={Transitorio}).")]
    public static partial void RegistroFallido(ILogger logger, Exception ex, Guid submissionId, bool transitorio);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: falló la consulta previa al reintento de la submission {SubmissionId}; no se radica para no duplicar.")]
    public static partial void ConsultaPreviaFallida(ILogger logger, Exception ex, Guid submissionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: el trámite {InstanceId} no pudo transicionar a entregado ({Error}); la submission sigue pendiente y se reintentará.")]
    public static partial void TransicionFallida(ILogger logger, Guid instanceId, string error);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Quipux: submission {SubmissionId} (trámite {InstanceId}) en dead-letter tras {Intentos} intento(s): {Motivo}. Requiere intervención.")]
    public static partial void DeadLetter(
        ILogger logger, Guid submissionId, Guid instanceId, string motivo, int intentos);
}
