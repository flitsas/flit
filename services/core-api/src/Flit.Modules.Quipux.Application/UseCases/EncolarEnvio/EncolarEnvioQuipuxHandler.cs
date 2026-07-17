using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Mapeo;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;

/// <summary>
/// Crea la radicación (submission) de un trámite elegible para Quipux.
/// </summary>
/// <remarks>
/// <para>
/// Es el equivalente 2.0 de <c>listQuipux</c> + la preparación del envío de FLIT 1.0, con tres
/// diferencias de fondo:
/// </para>
/// <list type="number">
///   <item>
///   La elegibilidad NO se decide con un flag hardcodeado (<c>id_parinttrasec_transfer = 2</c>) sino
///   con la parametrización en datos: sin bloque <c>quipux</c> en <c>procedure_types.external_refs</c>
///   el trámite no va a Quipux. Un tipo de trámite nuevo es un UPDATE, no un despliegue.
///   </item>
///   <item>
///   El <c>document_name</c> se calcula UNA vez aquí y se persiste. En 1.0 se regeneraba en cada
///   intento con precisión de minuto, así que un reintento producía un nombre distinto: el trámite
///   quedaba imposible de consultar y podía duplicarse en Quipux.
///   </item>
///   <item>
///   El PDF se asegura vía <see cref="IQuipuxConsolidadoMaestroPort"/>, que reutiliza el consolidado
///   vigente sin regenerar. Es el reemplazo del chequeo <c>dateSentOt == hoy</c>.
///   </item>
/// </list>
/// </remarks>
public sealed class EncolarEnvioQuipuxHandler
{
    private readonly IProcedureInstanceRepository _instances;
    private readonly IProcedureTypeRepository _procedureTypes;
    private readonly IQuipuxSubmissionRepository _submissions;
    private readonly IQuipuxConsolidadoMaestroPort _consolidado;
    private readonly IQuipuxOrganismoPort _organismos;
    private readonly IQuipuxTenantPort _tenants;
    private readonly IQuipuxAuditLog _audit;
    private readonly ILogger<EncolarEnvioQuipuxHandler> _logger;

    public EncolarEnvioQuipuxHandler(
        IProcedureInstanceRepository instances,
        IProcedureTypeRepository procedureTypes,
        IQuipuxSubmissionRepository submissions,
        IQuipuxConsolidadoMaestroPort consolidado,
        IQuipuxOrganismoPort organismos,
        IQuipuxTenantPort tenants,
        IQuipuxAuditLog audit,
        ILogger<EncolarEnvioQuipuxHandler> logger)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _consolidado = consolidado ?? throw new ArgumentNullException(nameof(consolidado));
        _organismos = organismos ?? throw new ArgumentNullException(nameof(organismos));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EncolarEnvioQuipuxResult> HandleAsync(
        EncolarEnvioQuipuxCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ProcedureInstanceId == Guid.Empty)
        {
            return EncolarEnvioQuipuxResult.EntradaInvalida("procedure_instance_id_requerido");
        }

        if (command.TenantId == Guid.Empty)
        {
            return EncolarEnvioQuipuxResult.EntradaInvalida("tenant_id_requerido");
        }

        // Grafo mínimo: FieldValues (placa/VIN + banderas de variante) y Actors (no se usan aquí,
        // pero es la query lean que ya existe y evita añadir una nueva al repositorio).
        var instance = await _instances.GetByIdWithDetailsAsync(
            command.ProcedureInstanceId, command.TenantId, cancellationToken);

        if (instance is null)
        {
            return EncolarEnvioQuipuxResult.NoEncontrado();
        }

        // Solo un trámite ya preparado tiene expediente completo que radicar. El resto de gates
        // (identidad, documentos obligatorios) los aplicó ITramiteLifecycleService al llegar aquí.
        if (!string.Equals(instance.Status, TramiteEstado.Preparado, StringComparison.Ordinal))
        {
            return EncolarEnvioQuipuxResult.NoElegible(EncolarEnvioQuipuxMotivos.EstadoNoPreparado);
        }

        var procedureType = await _procedureTypes.GetByIdAsync(instance.ProcedureTypeId, cancellationToken);
        var mapa = QuipuxTipoTramiteMap.Parse(procedureType?.ExternalRefs);
        if (mapa is null)
        {
            // Gate natural, no error: la inmensa mayoría de tipos de trámite no van a Quipux.
            return EncolarEnvioQuipuxResult.NoElegible(EncolarEnvioQuipuxMotivos.TipoSinParametrizacionQuipux);
        }

        if (instance.TransitOfficeId is not { } transitOfficeId)
        {
            // El OT se resuelve por match de nombre contra el RUNT y puede quedar null. Es un gate
            // correcto, pero debe ser VISIBLE: por eso sale como motivo y no como silencio.
            return EncolarEnvioQuipuxResult.NoElegible(EncolarEnvioQuipuxMotivos.SinOrganismo);
        }

        var codigoDivipo = await _organismos.ObtenerCodigoDivipoAsync(transitOfficeId, cancellationToken);
        if (string.IsNullOrWhiteSpace(codigoDivipo))
        {
            return EncolarEnvioQuipuxResult.NoElegible(EncolarEnvioQuipuxMotivos.OrganismoSinCodigoDivipo);
        }

        // La variante (es_unilateral / es_leasing) sale de los field values del trámite, no de una
        // rama de código: en 1.0 traspaso y matrícula eran servicios distintos que hacían lo mismo.
        var fieldValues = LeerFieldValues(instance);
        var resuelto = mapa.Resolve(fieldValues);

        var (sufijo, motivoSufijo) = ResolverIdentificadorVehiculo(resuelto, fieldValues);
        if (motivoSufijo is not null)
        {
            return EncolarEnvioQuipuxResult.NoElegible(motivoSufijo);
        }

        var razonSocial = await _tenants.ObtenerRazonSocialAsync(command.TenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(razonSocial))
        {
            return EncolarEnvioQuipuxResult.NoElegible(EncolarEnvioQuipuxMotivos.TenantSinRazonSocial);
        }

        string documentName;
        try
        {
            documentName = QuipuxDocumentNameBuilder.Build(
                razonSocial,
                resuelto.Prefijo,
                command.Momento ?? DateTimeOffset.UtcNow,
                sufijo!,
                resuelto.MaxLongitudEmpresa);
        }
        catch (ArgumentException)
        {
            // La razón social no deja ningún carácter ASCII alfanumérico. En 1.0 el catch devolvía
            // '' y el flujo caía al UUID del PDF como nombre del documento — un accidente que
            // producía radicaciones no correlacionables. Aquí es no-elegible explícito.
            EncolarEnvioQuipuxLog.RazonSocialNoSanitizable(_logger, command.ProcedureInstanceId);
            return EncolarEnvioQuipuxResult.NoElegible(EncolarEnvioQuipuxMotivos.RazonSocialNoSanitizable);
        }

        // El PDF se asegura ANTES de encolar: sin adjunto no hay nada que radicar, y encolar una
        // submission que el registrador no puede cumplir solo consumiría intentos hasta el dead-letter.
        var consolidado = await _consolidado.AsegurarAsync(
            command.ProcedureInstanceId, command.TenantId, cancellationToken);

        if (!consolidado.Exitoso)
        {
            EncolarEnvioQuipuxLog.ConsolidadoFallido(
                _logger, command.ProcedureInstanceId, consolidado.Error ?? "desconocido");
            return EncolarEnvioQuipuxResult.ErrorConsolidado(consolidado.Error ?? "consolidado_no_disponible");
        }

        var ahora = DateTimeOffset.UtcNow;
        var nueva = new QuipuxSubmission
        {
            // Id asignado en código a propósito: EnqueueIfAbsentAsync devuelve la submission EXISTENTE
            // cuando ya hay una activa, y comparar ids es la única forma de distinguir "inserté" de
            // "reutilicé" sin añadir un método al repositorio. La implementación de Infrastructure
            // DEBE respetar este Id al insertar (no dejar que lo genere el DEFAULT uuidv7()).
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ProcedureInstanceId = command.ProcedureInstanceId,
            DocumentName = documentName,
            AttachmentId = consolidado.AttachmentId!.Value,
            QuipuxProcedureType = resuelto.TipoTramite,
            QuipuxRequirementType = resuelto.TipoRequisito,
            DivipoCode = codigoDivipo,
            Status = QuipuxSubmissionEstado.Pendiente,
            Attempts = 0,
            CreatedAt = ahora,
        };

        var submission = await _submissions.EnqueueIfAbsentAsync(nueva, cancellationToken);
        var yaExistia = submission.Id != nueva.Id;

        if (yaExistia)
        {
            // Idempotencia: el trámite ya tenía radicación en curso. No se toca el document_name
            // existente — cambiarlo rompería la consulta de estado de lo ya radicado.
            EncolarEnvioQuipuxLog.YaEncolada(_logger, command.ProcedureInstanceId, submission.Id);
            return EncolarEnvioQuipuxResult.YaEncolada(submission.Id, submission.DocumentName);
        }

        await _audit.WriteAsync(
            command.TenantId,
            submission.Id,
            QuipuxStage.ConsolidadoGenerado,
            consolidado.Regenerado ? QuipuxOutcome.Ok : QuipuxOutcome.Omitido,
            // Detalle SANITIZADO: códigos y ids, nunca el request/response ni datos del propietario.
            // 1.0 persistía json_send/json_response completos, con PII dentro. Se pasa el objeto sin
            // serializar: el escritor de bitácora es quien lo convierte a jsonb.
            new
            {
                procedure_instance_id = command.ProcedureInstanceId,
                attachment_id = consolidado.AttachmentId,
                regenerado = consolidado.Regenerado,
                tipo_tramite = resuelto.TipoTramite,
                tipo_requisito = resuelto.TipoRequisito,
                prefijo = resuelto.Prefijo,
                variante_aplicada = resuelto.VarianteAplicada,
            },
            command.CorrelationId,
            cancellationToken);

        EncolarEnvioQuipuxLog.Encolada(
            _logger, command.ProcedureInstanceId, submission.Id, resuelto.TipoTramite);

        return EncolarEnvioQuipuxResult.Encolada(submission.Id, submission.DocumentName);
    }

    /// <summary>
    /// Placa o VIN según la parametrización. Ambos se proyectan de
    /// <c>procedure_instance_field_values</c> — NO son columnas de <c>procedure_instances</c>.
    /// </summary>
    private static (string? Sufijo, string? Motivo) ResolverIdentificadorVehiculo(
        QuipuxTramiteResuelto resuelto,
        Dictionary<string, string?> fieldValues)
    {
        var campo = resuelto.UsaPlaca ? resuelto.CampoPlaca : resuelto.CampoVin;
        if (campo is null)
        {
            return (null, EncolarEnvioQuipuxMotivos.ParametrizacionSinIdentificador);
        }

        if (!fieldValues.TryGetValue(campo, out var valor) || string.IsNullOrWhiteSpace(valor))
        {
            return (null, EncolarEnvioQuipuxMotivos.SinIdentificadorVehiculo);
        }

        return (valor.Trim(), null);
    }

    /// <summary>
    /// Field values indexados por clave. Si la instancia trajera dos filas con la misma
    /// <c>field_key</c> (no hay unique en BD), gana la última: <c>ToDictionary</c> lanzaría.
    /// </summary>
    internal static Dictionary<string, string?> LeerFieldValues(ProcedureInstance instance)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var fv in instance.FieldValues)
        {
            valores[fv.FieldKey] = fv.ValueText;
        }

        return valores;
    }
}

/// <summary>Logging source-generated (CA1848) del encolado. Sin PII: solo ids y códigos.</summary>
internal static partial class EncolarEnvioQuipuxLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux: trámite {InstanceId} encolado como submission {SubmissionId} (tipoTramite {TipoTramite}).")]
    public static partial void Encolada(ILogger logger, Guid instanceId, Guid submissionId, int tipoTramite);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Quipux: el trámite {InstanceId} ya tenía la submission activa {SubmissionId}; no se encola de nuevo.")]
    public static partial void YaEncolada(ILogger logger, Guid instanceId, Guid submissionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: no se pudo asegurar el consolidado maestro del trámite {InstanceId} ({Error}); no se encola.")]
    public static partial void ConsolidadoFallido(ILogger logger, Guid instanceId, string error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: la razón social del tenant del trámite {InstanceId} no deja caracteres válidos tras sanitizar; el trámite no es radicable.")]
    public static partial void RazonSocialNoSanitizable(ILogger logger, Guid instanceId);
}
