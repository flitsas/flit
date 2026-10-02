using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Bug #13194 (P4, D2) — «NO SE PERMITE ENVIAR AL OT TRÁMITES SIN FIRMAR», siempre.
///
/// <para><b>Firmado</b> = cada parte que debe firmar tiene identidad APROBADA y VIGENTE según
/// <see cref="IdentityApprovalResolver"/> en el MISMO tenant del trámite: persona jurídica por firma del
/// baúl vigente de su representante legal (con el interruptor «Baúl de firmas activo» de la compañía
/// encendido, <see cref="ISignatureVaultPolicy"/>) o por la validación de identidad vigente del RL;
/// persona natural por su validación de identidad vigente.</para>
///
/// <para><b>Quién firma.</b> Las partes que el tipo declara para identidad
/// (<see cref="PartesDeclaradas.Identidad(ProcedureInstance)"/>: matrícula y el resto, comprador;
/// traspaso, comprador y vendedor; el traspaso unilateral solo convoca al propietario, DDL 94; el locatario del leasing no firma, DDL 88), acotadas a
/// comprador/vendedor, que son las únicas que el resolutor acredita. Es la MISMA lista de la columna
/// «Firmado» del listado, así que el gate bloquea exactamente lo que el listado no pinta firmado.</para>
///
/// <para><b>Qué NO cambia.</b> B12 (HU #10661, ADR-0028): la firma del contrato de compraventa no se
/// exige. Las escrituras siguen afectando solo la exención de Cámara de Comercio. La configuración del OT
/// «validación de identidad deshabilitada» ya no relaja este gate (antes lo hacía en el de preparación).</para>
/// </summary>
public static class FirmaGate
{
    private static readonly string[] PartesAcreditables =
        [BiometricRules.ParteComprador, BiometricRules.ParteVendedor];

    /// <summary>Partes del trámite que deben firmar (comprador/vendedor), en orden canónico.</summary>
    public static IReadOnlyList<string> PartesQueFirman(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // Sin partes declaradas por el tipo (tipo no cargado, gate_profile vacío o sin biometricActors ni
        // parte vendedora) manda la regla de SubmitGate: traspaso, comprador y vendedor; el resto, comprador.
        var perfil = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        if (perfil.BiometricActors.Count == 0 && !perfil.RequiresSeller)
        {
            return ProcedureFamilyCodes.FromCodeOrOtros(instance.ProcedureType?.Family) == ProcedureFamily.Traspaso
                ? [BiometricRules.ParteComprador, BiometricRules.ParteVendedor]
                : [BiometricRules.ParteComprador];
        }

        var declaradas = PartesDeclaradas.Identidad(perfil);
        var partes = PartesAcreditables
            .Where(p => declaradas.Any(d => string.Equals(d, p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Un perfil que solo declara partes que el resolutor no acredita (p. ej. solo locatario) no
        // puede dejar el trámite sin firmante: el comprador firma siempre.
        return partes.Count > 0 ? partes : [BiometricRules.ParteComprador];
    }

    /// <summary>
    /// Partes que deben firmar y aún no tienen identidad aprobada y vigente. Vacío = firmado. No
    /// solicita validaciones nuevas: solo consulta lo ya validado (baúl → fila propia → identidad
    /// vigente de la persona en el mismo tenant).
    /// </summary>
    public static async Task<IReadOnlyList<string>> PartesSinFirmaAsync(
        IProcedureInstanceRepository repo,
        ProcedureInstance instance,
        ISignatureVaultPolicy vaultPolicy,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(instance);

        var aprobadas = await IdentityApprovalResolver
            .ResolveApprovedPartiesAsync(repo, instance, now, ct, vaultPolicy)
            .ConfigureAwait(false);

        return PartesQueFirman(instance)
            .Where(p => !aprobadas.Contains(p))
            .ToList();
    }

    /// <summary>
    /// Notifica (correo de validación) cada parte sin firma. Nunca lanza salvo cancelación: una excepción
    /// del notificador se informa por <paramref name="alFallar"/> (tipo de excepción y parte, sin PII) y la
    /// parte queda en <see cref="FirmaNotificacionEstados.Fallida"/>. Sin notificador:
    /// <see cref="FirmaNotificacionEstados.NoConfigurada"/>. Compartido por el ciclo de vida y por «Enviar
    /// al OT», que evalúa la firma ANTES de consultar el RUNT (review PR #510, MENOR-3).
    /// </summary>
    public static async Task<IReadOnlyList<ParteSinFirma>> NotificarAsync(
        IFirmaPendienteNotifier? notifier,
        Guid instanceId,
        Guid tenantId,
        IReadOnlyList<string> partes,
        Action<string, string>? alFallar,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(partes);
        var resultado = new List<ParteSinFirma>(partes.Count);
        foreach (var parte in partes)
        {
            string estado;
            if (notifier is null)
            {
                estado = FirmaNotificacionEstados.NoConfigurada;
            }
            else
            {
                try
                {
                    estado = await notifier.NotificarAsync(instanceId, tenantId, parte, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    alFallar?.Invoke(ex.GetType().Name, parte);
                    estado = FirmaNotificacionEstados.Fallida;
                }
            }

            resultado.Add(new ParteSinFirma(parte, estado));
        }

        return resultado;
    }

    /// <summary>Mensaje para el usuario con las partes que faltan por firmar (sin PII).</summary>
    public static string Detalle(IReadOnlyList<string> partesSinFirma)
    {
        ArgumentNullException.ThrowIfNull(partesSinFirma);
        return "No se permite enviar al organismo de tránsito un trámite sin firmar. Falta la firma "
            + "(identidad aprobada y vigente) de: " + string.Join(", ", partesSinFirma) + ".";
    }

    /// <summary>
    /// Mismo mensaje con el estado de la notificación por parte, en formato estable:
    /// <c>… de: comprador (notificación: enviada), vendedor (notificación: fallida).</c>
    /// </summary>
    public static string Detalle(IReadOnlyList<ParteSinFirma> partesSinFirma) =>
        "No se permite enviar al organismo de tránsito un trámite sin firmar. Falta la firma "
        + "(identidad aprobada y vigente) de: " + PartesConNotificacion(partesSinFirma) + ".";

    /// <summary><c>comprador (notificación: enviada), vendedor (notificación: fallida)</c>.</summary>
    public static string PartesConNotificacion(IReadOnlyList<ParteSinFirma> partesSinFirma)
    {
        ArgumentNullException.ThrowIfNull(partesSinFirma);
        return string.Join(", ", partesSinFirma.Select(p => $"{p.Parte} (notificación: {p.Notificacion})"));
    }

    /// <summary>
    /// ¿La transición exige el gate de firma? Toda llegada a <c>preparado</c>, <c>preasignacion</c> o
    /// <c>entregado</c> pedida por el gestor o el sistema (incluidas re-radicaciones y «Enviar al OT»).
    /// Quedan fuera las del OT (liberar placa: el trámite ya está en el organismo) y la de Quipux (el
    /// documento ya quedó registrado en la secretaría; bloquearla dejaría un huérfano —ver
    /// <c>RegistrarDocumentoQuipuxHandler</c>—; el gate de Quipux corre al ENCOLAR, vía
    /// <see cref="ITramiteFirmaGate"/>).
    /// </summary>
    public static bool Aplica(string toStatus, TramiteActor actor) =>
        toStatus is TramiteEstado.Preparado or TramiteEstado.Preasignacion or TramiteEstado.Entregado
        && actor is not (TramiteActor.Ot or TramiteActor.Quipux);
}

/// <summary>
/// Bug #13194 (P4, D2) — adaptador de <see cref="IFirmaPendienteNotifier"/> sobre
/// <see cref="EnsureIdentityAndNotifyHandler"/>: asegura la identidad de la parte y dispara el correo de
/// validación solo si hace falta (idempotente: una validación en curso responde <c>ya_en_curso</c>).
/// Con 2+ actores en el rol notifica al principal (documento null), el default del handler.
/// </summary>
public sealed class EnsureIdentityFirmaPendienteNotifier(EnsureIdentityAndNotifyHandler handler)
    : IFirmaPendienteNotifier
{
    public async Task<string> NotificarAsync(
        Guid instanceId, Guid tenantId, string parte, CancellationToken ct = default)
    {
        var (result, _) = await handler.HandleAsync(instanceId, tenantId, parte, documento: null, ct)
            .ConfigureAwait(false);
        return result?.Notificacion ?? FirmaNotificacionEstados.Fallida;
    }
}

/// <summary>
/// Implementación de <see cref="ITramiteFirmaGate"/> sobre <see cref="FirmaGate"/> (Bug #13194, D2).
/// </summary>
public sealed class TramiteFirmaGate(
    IProcedureInstanceRepository repo,
    ISignatureVaultPolicy? vaultPolicy = null) : ITramiteFirmaGate
{
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    public async Task<IReadOnlyList<string>> PartesSinFirmaAsync(
        Guid instanceId, Guid tenantId, CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return [];

        return await FirmaGate
            .PartesSinFirmaAsync(repo, instance, _vaultPolicy, DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);
    }
}
