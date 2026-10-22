using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de «Vencer» una identidad aprobada y vigente (HU #13824, Feature #13820, Épica #13817). Como
/// <see cref="ActivarIdentidadManualCommand"/>, NO lleva tenant: solo el Super Admin la ejecuta, sobre cualquier
/// compañía, y el tenant sale de LA FILA (toda escritura y auditoría usa el tenant dueño de la identidad).
/// </summary>
public sealed record VencerIdentidadCommand(Guid ValidationId, Guid VencidaPorUserId);

/// <summary>
/// Resultado de vencer. <see cref="EnlaceEnviado"/> es true cuando se abrió una validación NUEVA en el trámite en borrador
/// más reciente que usaba la identidad. Si no, <see cref="MotivoSinEnlace"/> dice por qué (<c>sin_borradores</c>,
/// <c>validacion_en_curso</c>, <c>proveedor_error</c>, <c>proveedor_no_disponible</c>, <c>datos_incompletos</c>…): la
/// identidad quedó vencida de todos modos.
/// </summary>
/// <param name="EnlaceCaptura">Enlace para copiar (URL de captura del proveedor, o ruta del magic-link con el proveedor mock).
/// Null si no se envió, o si el proveedor estaba caído y la validación quedó encolada (<paramref name="EnCola"/>).</param>
public sealed record VencerIdentidadResult(
    Guid ValidationId,
    Guid TenantId,
    Guid? ProcedureInstanceId,
    DateTimeOffset VencidaAt,
    int BorradoresAfectados,
    bool EnlaceEnviado,
    Guid? NuevaValidacionId,
    Guid? TramiteBorradorId,
    string? TramiteBorradorReferencia,
    string? EnlaceCaptura,
    bool EnCola,
    string? MotivoSinEnlace);

/// <summary>
/// «Vencer» una identidad aprobada y vigente (solo Super Admin; el módulo Validaciones de Identidad lista validaciones de
/// trámite y prevalidaciones standalone, y aplica a ambas). Pasos:
/// <list type="number">
///   <item>La vigencia termina AHORA (<see cref="ProcedureInstanceBiometricValidation.VencerManualmente"/>:
///   <c>valid_until</c> = ahora, que ya es la fuente de verdad de <see cref="BiometricRules.EsAprobadaVigente"/>). No se
///   borra la fila ni el certificado. Los trámites en borrador que la usaban quedan sin firmar para esa persona; los que ya
///   salieron de borrador conservan su firma (<see cref="BiometricRules.EsAprobadaVigenteParaTramite"/>).</item>
///   <item>Se abre una validación NUEVA en el trámite en borrador más reciente que usaba la identidad y se envía el enlace,
///   reutilizando el mismo inicio del trámite (<see cref="IniciarKyverumVerifyHandler"/> /
///   <see cref="IniciarBiometriaHandler"/>, según <see cref="BiometricsProviderOptions"/>): mismas precedencias de envío y
///   mismo guard de validación en curso (HU #11265/#11266). Sin borradores que la usen, vence igual y no envía nada.</item>
///   <item>Queda en el tracking de cada trámite afectado (<see cref="EventoTipo"/>, atómico con el vencimiento) y en la
///   bitácora técnica de identidad (<see cref="IdentityValidationAuditStages.VencidaPorAdmin"/>, correo enmascarado).</item>
/// </list>
/// El vencimiento se confirma ANTES de iniciar la validación nueva (si no, la precedencia de envío vería la identidad todavía
/// vigente y no enviaría); si el inicio falla, la identidad queda vencida y el resultado trae el motivo.
/// <para>Errores: <c>not_found</c> (404), <c>no_aprobada_vigente</c> (409), <c>fuera_de_alcance</c> (422; validación de
/// mandatario).</para>
/// </summary>
public sealed class VencerIdentidadHandler(
    IProcedureInstanceRepository repo,
    BiometricsProviderOptions providerOptions,
    IniciarKyverumVerifyHandler kyverumStart,
    IniciarBiometriaHandler mockStart,
    IIdentityValidationAuditLog audit,
    TimeProvider? clock = null)
{
    /// <summary>Tipo del evento PROPIO del historial del trámite (mismo patrón que el reenvío administrativo).</summary>
    public const string EventoTipo = "vencimiento_identidad_admin";

    public const string NoEncontrada = "not_found";
    public const string NoAprobadaVigente = "no_aprobada_vigente";
    public const string FueraDeAlcance = "fuera_de_alcance";

    public const string MotivoSinBorradores = "sin_borradores";
    public const string MotivoValidacionEnCurso = "validacion_en_curso";

    /// <summary>Mensaje para el Super Admin cuando no hay nada que vencer.</summary>
    public const string MensajeNoAprobadaVigente = "Esta validación no está aprobada y vigente. No hay nada que vencer.";

    public async Task<(VencerIdentidadResult? Result, string? Error)> HandleAsync(
        VencerIdentidadCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Sin filtro de tenant a propósito (ver el comando): la fila trae el tenant dueño y su trámite (si hay).
        var v = await repo.GetBiometricByIdAsync(command.ValidationId, ct).ConfigureAwait(false);
        if (v is null)
            return (null, NoEncontrada);

        // Mandatarios: fuera de alcance (su identidad es exclusiva de la ficha del mandatario).
        if (v.MandateSignerId is not null)
            return (null, FueraDeAlcance);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (!BiometricRules.EsAprobadaVigente(v, now))
            return (null, NoAprobadaVigente);

        // Borradores que usan la identidad (antes de vencerla): el más reciente recibe la validación nueva.
        var usos = await ResolverBorradoresAsync(v, ct).ConfigureAwait(false);
        var destino = usos.Count > 0 ? usos[0] : null;

        var prevista = v.ValidUntil;
        v.VencerManualmente(now);

        // Tracking: el trámite dueño de la validación (si es de trámite) y cada borrador afectado, sin repetir.
        var tramites = new HashSet<Guid>();
        if (v.ProcedureInstanceId is Guid origen)
            tramites.Add(origen);
        foreach (var uso in usos)
            tramites.Add(uso.Instance.Id);

        foreach (var instanciaId in tramites)
        {
            await repo.AddEventAsync(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = v.TenantId,
                ProcedureInstanceId = instanciaId,
                Tipo = EventoTipo,
                Payload = JsonSerializer.Serialize(new
                {
                    validation_id = v.Id,
                    party_role = v.PartyRole,
                    persona = v.Name,
                    tipo_documento = v.DocumentType,
                    numero_documento = v.DocumentNumber,
                    origen_tramite_id = v.ProcedureInstanceId,
                    standalone = v.ProcedureInstanceId is null,
                    tramite_borrador_destino = destino?.Instance.Id,
                    vencida_at = now,
                }),
                CreatedAt = now,
                CreatedBy = command.VencidaPorUserId,
            }, ct).ConfigureAwait(false);
        }

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        // Validación nueva + enlace (solo si hay un borrador que la necesite).
        string? motivo = null;
        Guid? nuevaId = null;
        string? enlace = null;
        var enCola = false;
        if (destino is null)
        {
            motivo = MotivoSinBorradores;
        }
        else
        {
            (nuevaId, enlace, enCola, motivo) = await IniciarAsync(v, destino, ct).ConfigureAwait(false);
        }

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.VencidaPorAdmin, IdentityValidationAuditOutcomes.Ok,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
            KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
            Message: $"Identidad vencida a mano por el usuario {command.VencidaPorUserId}.",
            Detail: $"usuario={command.VencidaPorUserId}; vencida_at={now:O}; valid_until_previo={prevista:O}; "
                + $"documento={MaskDocumento(v.DocumentNumber)}; correo={EditarPrevalidacionHandler.MaskEmail(v.Email)}; "
                + $"borradores_afectados={usos.Count}; tramite_destino={destino?.Instance.Id.ToString() ?? "-"}; "
                + $"nueva_validacion={nuevaId?.ToString() ?? "-"}; encolada={enCola}; motivo_sin_enlace={motivo ?? "-"}"), ct)
            .ConfigureAwait(false);

        var enviado = nuevaId is not null;
        return (new VencerIdentidadResult(
            v.Id, v.TenantId, v.ProcedureInstanceId, now, usos.Count, enviado, nuevaId,
            destino?.Instance.Id, destino?.Instance.ReferenceNumber, enlace, enCola, enviado ? null : motivo), null);
    }

    /// <summary>Un borrador que usa la identidad: el trámite y el actor (comprador/vendedor) cuyo sujeto es la persona.</summary>
    private sealed record UsoBorrador(ProcedureInstance Instance, ProcedureInstanceActor Actor, IdentitySubject Subject);

    /// <summary>
    /// Trámites en borrador del tenant con un actor cuyo SUJETO DE IDENTIDAD (el actor si es natural, su representante legal
    /// si es jurídico) tiene el documento de la validación, del más reciente al más antiguo. Solo cuentan las partes que el
    /// tipo manda a validar identidad (mismo criterio que <see cref="EnsureIdentityHandler"/>).
    /// </summary>
    private async Task<List<UsoBorrador>> ResolverBorradoresAsync(ProcedureInstanceBiometricValidation v, CancellationToken ct)
    {
        var usos = new List<UsoBorrador>();
        var tipo = v.DocumentType.Trim();
        var documento = v.DocumentNumber.Trim();
        if (tipo.Length == 0 || documento.Length == 0)
            return usos;

        var borradores = await repo.ListBorradoresConActorDocumentoAsync(v.TenantId, tipo, documento, ct).ConfigureAwait(false);
        foreach (var instancia in borradores)
        {
            var perfil = ProcedureTypeGateProfile.FromJson(instancia.ProcedureType?.GateProfile);
            var partesQueValidan = PartesDeclaradas.Identidad(perfil);
            foreach (var actor in instancia.Actors.OrderBy(a => a.Ordinal))
            {
                if (!string.Equals(actor.ActorType, BiometricRules.ParteComprador, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(actor.ActorType, BiometricRules.ParteVendedor, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (perfil.BiometricActors.Count > 0
                    && !partesQueValidan.Contains(actor.ActorType, StringComparer.OrdinalIgnoreCase))
                    continue;

                var subject = IdentitySubjectResolver.For(actor);
                if (string.Equals(subject.TipoDocumento?.Trim(), tipo, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(subject.NumeroDocumento?.Trim(), documento, StringComparison.OrdinalIgnoreCase))
                {
                    usos.Add(new UsoBorrador(instancia, actor, subject));
                    break; // un uso por trámite
                }
            }
        }

        return usos;
    }

    /// <summary>
    /// Abre la validación nueva en el borrador destino con el inicio de validación del trámite (provider-aware). Los datos de
    /// contacto salen del actor del trámite; si le faltan, de la fila vencida.
    /// </summary>
    private async Task<(Guid? NuevaId, string? Enlace, bool EnCola, string? Motivo)> IniciarAsync(
        ProcedureInstanceBiometricValidation v, UsoBorrador destino, CancellationToken ct)
    {
        var input = new IniciarBiometriaInput(
            destino.Actor.ActorType.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(destino.Subject.Nombre) ? v.Name : destino.Subject.Nombre!.Trim(),
            destino.Subject.TipoDocumento!.Trim(),
            destino.Subject.NumeroDocumento!.Trim(),
            string.IsNullOrWhiteSpace(destino.Subject.Email) ? v.Email : destino.Subject.Email!.Trim());

        if (providerOptions.IsKyverum)
        {
            var (kr, kerror, _) = await kyverumStart.HandleAsync(destino.Instance.Id, v.TenantId, input, ct).ConfigureAwait(false);
            if (kerror is not null || kr is null)
                return (null, null, false, MotivoDe(kerror));
            return kr.Queued
                ? (kr.Validation.Id, null, true, null)
                : (kr.Validation.Id, string.IsNullOrWhiteSpace(kr.CaptureUrl) ? null : kr.CaptureUrl, false, null);
        }

        var (mr, merror, _) = await mockStart.HandleAsync(destino.Instance.Id, v.TenantId, input, ct).ConfigureAwait(false);
        return merror is not null || mr is null
            ? (null, null, false, MotivoDe(merror))
            : (mr.Validation.Id, mr.MagicLinkPath, false, null);
    }

    /// <summary>Errores del inicio de validación → motivo estable para la UI.</summary>
    private static string MotivoDe(string? error) => error switch
    {
        "biometria_activa" or "enlace_vencido_reenvio" => MotivoValidacionEnCurso,
        null => "error_desconocido",
        _ => error,
    };

    /// <summary>Documento enmascarado para la bitácora técnica: solo los últimos 4 caracteres.</summary>
    private static string MaskDocumento(string? documento)
    {
        var d = (documento ?? string.Empty).Trim();
        return d.Length <= 4 ? new string('*', d.Length) : new string('*', d.Length - 4) + d[^4..];
    }
}
