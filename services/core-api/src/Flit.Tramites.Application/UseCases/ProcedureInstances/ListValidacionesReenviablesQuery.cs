using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Una PERSONA del trámite que todavía no firmó (HU #13821, Épica #13817) y la validación propia sobre la
/// que actúa el reenvío administrativo. <see cref="Ordinal"/> distingue copropietarios del mismo rol
/// (ADR-0053). <see cref="EsFlujoManual"/>: la validación está en captura manual (HU #13286) y no se
/// reenvía al proveedor automático — se lista para que la UI explique por qué no se puede marcar.
/// <see cref="ValidationId"/> es <c>null</c> cuando la persona NUNCA tuvo una validación de identidad en
/// este trámite (HU #14627): no hay fila sobre la cual reenviar, pero SÍ se puede enviar por primera vez
/// (<see cref="AdminReenviarValidacionIdentidadHandler"/>), con <see cref="Status"/> en
/// <see cref="ValidacionReenviableEstados.SinIniciar"/>.
/// </summary>
public sealed record ValidacionReenviableDto(
    Guid? ValidationId,
    string PartyRole,
    int Ordinal,
    string? Name,
    string? Email,
    string Status,
    string? CaptureUrl,
    DateTimeOffset? CaptureExpiresAt,
    bool EsFlujoManual);

/// <summary>
/// Etiquetas de <see cref="ValidacionReenviableDto.Status"/> que NO existen en
/// <see cref="BiometricEstados"/> (el enum de la fila real): son estados DERIVADOS de este DTO,
/// calculados por <see cref="ListValidacionesReenviablesHandler"/>.
/// </summary>
public static class ValidacionReenviableEstados
{
    /// <summary>HU #14627 — el actor nunca tuvo una fila de validación de identidad propia en este trámite.</summary>
    public const string SinIniciar = "no_iniciada";
}

public sealed record ValidacionesReenviablesResult(IReadOnlyList<ValidacionReenviableDto> Personas);

/// <summary>
/// "Reenviar validación" por PERSONA (HU #13821, Épica #13817): a quién se le puede reenviar la validación
/// de identidad de un trámite.
///
/// <para><b>Por qué no reutiliza <see cref="ListBiometriaHandler"/>.</b> Ese listado responde «qué identidad
/// representa a cada parte» (vista del expediente): pone primero la aprobada que prevalece, agrega
/// identidades REFERENCIADAS de otros trámites y re-rotula filas. El modal de reenvío lo consumía tal cual y
/// por eso ofrecía primero a quien ya había firmado (con su correo) y escondía a quien faltaba. Aquí la
/// pregunta es la inversa: por cada actor que valida identidad, ¿falta su firma y hay una fila PROPIA del
/// trámite sobre la cual reenviar?</para>
///
/// <para>Un actor NO se lista si (en este orden, el mismo de la precedencia del disparador):
/// <list type="number">
/// <item><description>lo cubre la firma del baúl vigente (<see cref="FirmaBaulCobertura"/>);</description></item>
/// <item><description>tiene una validación PROPIA aprobada y vigente;</description></item>
/// <item><description>tiene una identidad aprobada y vigente en OTRO trámite (referenciada, HU #10350):
/// para el trámite ya firmó;</description></item>
/// <item><description>SÍ tiene filas propias pero TODAS están aprobadas (vencidas, sin otra pendiente): ya
/// hubo una validación, solo que venció — revalidar exige iniciar una nueva, fuera del alcance de "reenviar"
/// (sin cambios de esta HU).</description></item>
/// </list>
/// Si tiene varios intentos, se toma el más reciente sin aprobar (una fila por persona). Si NUNCA tuvo
/// NINGUNA fila propia (no solo "sin pendiente"), se lista igual con
/// <see cref="ValidacionReenviableDto.ValidationId"/> <c>null</c> y
/// <see cref="ValidacionReenviableEstados.SinIniciar"/> (HU #14627): ya no es un caso borde que se descarta,
/// es la persona a la que hay que enviarle la validación por primera vez.</para>
/// </summary>
public sealed class ListValidacionesReenviablesHandler(
    IProcedureInstanceRepository repo,
    ISignatureVaultPolicy? vaultPolicy = null)
{
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    public async Task<(ValidacionesReenviablesResult? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        var partes = PartesDeclaradas.EnOrden(PartesDeclaradas.Identidad(instance));
        // Mismo criterio que ListBiometriaHandler/DeriveIdentityStatus: las filas SIN rol (legado de
        // matrícula) solo se atribuyen cuando la única parte que valida es el comprador.
        var legacySinRol = partes.Length == 1
            && string.Equals(partes[0], BiometricRules.ParteComprador, StringComparison.OrdinalIgnoreCase);

        var personas = new List<ValidacionReenviableDto>();
        foreach (var parte in partes)
        {
            var actores = instance.Actors
                .Where(a => string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Ordinal);

            foreach (var actor in actores)
            {
                var subject = IdentitySubjectResolver.For(actor);
                if (string.IsNullOrWhiteSpace(subject.TipoDocumento) || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
                    continue;
                var tipo = subject.TipoDocumento.Trim();
                var documento = subject.NumeroDocumento.Trim();

                if (FirmaBaulCobertura.Aplica(actor)
                    && await _vaultPolicy.ResolveAsync(instance.TenantId, tipo, documento, ct).ConfigureAwait(false) is not null)
                    continue;

                var propias = instance.BiometricValidations
                    .Where(v => (string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
                                 || (legacySinRol && v.PartyRole is null))
                                && BiometricRules.DocumentoCoincide(v, tipo, documento))
                    .ToList();

                if (propias.Exists(v => BiometricRules.EsAprobadaVigente(v, now)))
                    continue;

                if (await repo.FindVigenteApprovedByDocumentAsync(instance.TenantId, tipo, documento, now, ct)
                        .ConfigureAwait(false) is not null)
                    continue;

                // Una aprobada que ya venció no es reenviable (el reenvío no pisa aprobaciones): se necesita
                // una validación nueva, que no es el alcance de esta acción (eso sigue excluyendo al actor,
                // sin cambios — HU #14627 no toca este caso: SÍ hubo una validación, solo que ya venció).
                var pendiente = propias
                    .Where(v => v.Status != BiometricEstados.Aprobado)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefault();
                if (pendiente is null)
                {
                    if (propias.Count > 0)
                        continue;

                    // HU #14627 — el actor NUNCA tuvo una fila propia (no solo "sin pendiente"): se lista
                    // igual (sin inventar un id) para que el admin pueda enviarle la validación por primera
                    // vez desde el mismo cuadro.
                    personas.Add(new ValidacionReenviableDto(
                        null,
                        parte,
                        actor.Ordinal,
                        subject.Nombre,
                        subject.Email,
                        ValidacionReenviableEstados.SinIniciar,
                        null,
                        null,
                        false));
                    continue;
                }

                personas.Add(new ValidacionReenviableDto(
                    pendiente.Id,
                    parte,
                    actor.Ordinal,
                    string.IsNullOrWhiteSpace(subject.Nombre) ? pendiente.Name : subject.Nombre,
                    pendiente.Email,
                    pendiente.Status,
                    pendiente.CaptureUrl,
                    pendiente.ExpiresAt,
                    string.Equals(pendiente.Provider, BiometricProviders.Manual, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return (new ValidacionesReenviablesResult(personas), null);
    }
}
