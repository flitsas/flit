using Flit.Analytics.Application.CompanyQueries;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Identity;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Arma la lista <c>actores</c> (validación de identidad por persona) de UN trámite para los reportes de la empresa.
///
/// <para><b>Rol.</b> La BD solo guarda actores «comprador» y «vendedor» (no existe «propietario»). El propietario se
/// DERIVA de la familia: en TRASPASO se emiten comprador y vendedor tal cual; en MATRICULAS y OTROS el actor guardado
/// como «comprador» se emite como «propietario». El locatario no se emite: no es sujeto de identidad.</para>
///
/// <para><b>Enlace actor ↔ validación</b> (no hay FK): mismo trámite, mismo <c>party_role</c> que el actor guardado y
/// documento del SUJETO de identidad (representante legal si el actor es persona jurídica, nunca el NIT). Si ninguna
/// validación coincide por documento y el rol tiene exactamente UN actor, se le atribuyen las del rol. Con 2 o más
/// actores sin coincidencia el actor sale «sin validar»: nunca se atribuye la validación de una persona a otra.</para>
///
/// <para><b>Intentos.</b> Total de resultados recibidos en todos los ciclos de todas las validaciones del actor,
/// contados en <c>tramites.identity_validation_audit</c> (<see cref="ContarIntentos"/>). Sin ninguna fila de auditoría
/// (datos migrados), se suma <c>attempts</c> de las filas de validación. No se usa solo <c>attempts</c> porque se
/// reinicia con cada reenvío.</para>
/// </summary>
public static class ActorValidacionBuilder
{
    /// <summary>
    /// Cuenta los intentos de UNA validación a partir de su bitácora: un resultado aplicado del proveedor
    /// (<c>webhook_applied</c>), una consulta de respaldo que aplicó un resultado final (<c>reconcile</c> con desenlace
    /// aprobado o rechazado) o una captura recibida (<c>manual_captura_recibida</c>).
    /// </summary>
    public static bool CuentaComoIntento(string stage, string outcome) =>
        stage == IdentityValidationAuditStages.WebhookApplied
        || stage == IdentityValidationAuditStages.ManualCapturaRecibida
        || (stage == IdentityValidationAuditStages.Reconcile
            && (outcome == BiometricEstados.Aprobado || outcome == BiometricEstados.Rechazado));

    /// <summary>Intentos de un actor: auditoría de sus validaciones; sin auditoría, <c>attempts</c> de las filas.</summary>
    public static int ContarIntentos(
        IReadOnlyList<ProcedureInstanceBiometricValidation> validaciones,
        IReadOnlyDictionary<Guid, int> intentosAuditoriaPorValidacion,
        IReadOnlySet<Guid> validacionesConAuditoria)
    {
        if (validaciones.Count == 0)
            return 0;
        if (!validaciones.Any(v => validacionesConAuditoria.Contains(v.Id)))
            return validaciones.Sum(v => Math.Max(0, v.Attempts));
        return validaciones.Sum(v => intentosAuditoriaPorValidacion.GetValueOrDefault(v.Id));
    }

    public static IReadOnlyList<ActorValidacionDto> Build(
        string? familia,
        IReadOnlyList<ProcedureInstanceActor> actores,
        IReadOnlyList<ProcedureInstanceBiometricValidation> validaciones,
        IReadOnlyDictionary<Guid, int> intentosAuditoriaPorValidacion,
        IReadOnlySet<Guid> validacionesConAuditoria)
    {
        var esTraspaso = string.Equals(familia, ProcedureFamilyCodes.Traspaso, StringComparison.OrdinalIgnoreCase);
        var resultado = new List<(int OrdenRol, ActorValidacionDto Dto)>();

        foreach (var grupo in actores
                     .Where(a => EsRolEmitible(a.ActorType))
                     .GroupBy(a => a.ActorType.Trim().ToLowerInvariant()))
        {
            var delRol = grupo.OrderBy(a => a.Ordinal).ToList();
            var validacionesDelRol = validaciones
                .Where(v => string.Equals(v.PartyRole, grupo.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var actor in delRol)
            {
                var sujeto = IdentitySubjectResolver.For(actor);
                var propias = validacionesDelRol
                    .Where(v => BiometricRules.DocumentoCoincide(v, sujeto.TipoDocumento, sujeto.NumeroDocumento)
                        || BiometricRules.DocumentoCoincide(v, actor.DocumentType, actor.DocumentNumber))
                    .ToList();
                if (propias.Count == 0 && delRol.Count == 1)
                    propias = validacionesDelRol;

                var rol = grupo.Key == ActorValidacionRoles.Comprador && !esTraspaso
                    ? ActorValidacionRoles.Propietario
                    : grupo.Key;

                resultado.Add((OrdenRol(rol), Armar(rol, actor, sujeto, propias,
                    intentosAuditoriaPorValidacion, validacionesConAuditoria)));
            }
        }

        return resultado.OrderBy(r => r.OrdenRol).ThenBy(r => r.Dto.Ordinal).Select(r => r.Dto).ToList();
    }

    /// <summary>Máximo de actores por rol entre las filas dadas.</summary>
    public static MaxActoresPorRolDto Maximos(IEnumerable<IReadOnlyList<ActorValidacionDto>> filas)
    {
        int comprador = 0, vendedor = 0, propietario = 0;
        foreach (var actores in filas)
        {
            comprador = Math.Max(comprador, actores.Count(a => a.Rol == ActorValidacionRoles.Comprador));
            vendedor = Math.Max(vendedor, actores.Count(a => a.Rol == ActorValidacionRoles.Vendedor));
            propietario = Math.Max(propietario, actores.Count(a => a.Rol == ActorValidacionRoles.Propietario));
        }
        return new MaxActoresPorRolDto(comprador, vendedor, propietario);
    }

    private static ActorValidacionDto Armar(
        string rol,
        ProcedureInstanceActor actor,
        IdentitySubject sujeto,
        List<ProcedureInstanceBiometricValidation> propias,
        IReadOnlyDictionary<Guid, int> intentosAuditoria,
        IReadOnlySet<Guid> conAuditoria)
    {
        var ultima = propias
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.UpdatedAt ?? v.CreatedAt)
            .FirstOrDefault();

        var ultimoRechazo = propias
            .Where(v => v.Status == BiometricEstados.Rechazado)
            .OrderByDescending(v => v.UpdatedAt ?? v.CreatedAt)
            .FirstOrDefault();

        return new ActorValidacionDto(
            Rol: rol,
            Ordinal: actor.Ordinal,
            Nombre: actor.FullName,
            TipoDocumento: NullSiVacio(actor.DocumentType),
            Documento: NullSiVacio(actor.DocumentNumber),
            EstadoValidacion: Estado(ultima),
            MotivoRechazo: ultimoRechazo is null ? null : IdentityRejectionReasonText.For(ultimoRechazo),
            Intentos: ContarIntentos(propias, intentosAuditoria, conAuditoria));
    }

    /// <summary>Estado en código cerrado; todo lo que no se reconoce sale «sin_validacion», nunca un texto crudo.</summary>
    public static string Estado(ProcedureInstanceBiometricValidation? v) => v?.Status switch
    {
        BiometricEstados.Aprobado => ActorValidacionEstados.Aprobado,
        BiometricEstados.Rechazado => ActorValidacionEstados.Rechazado,
        BiometricEstados.EnProceso => ActorValidacionEstados.EnProceso,
        BiometricEstados.Enviado => ActorValidacionEstados.Enviado,
        BiometricEstados.PendienteEnvio => ActorValidacionEstados.PendienteEnvio,
        BiometricEstados.ErrorEnvio => ActorValidacionEstados.ErrorEnvio,
        BiometricEstados.Expirado => ActorValidacionEstados.Expirado,
        BiometricEstados.ManualActivo => ActorValidacionEstados.EsperandoCaptura,
        BiometricEstados.PendienteRevisionManual => ActorValidacionEstados.PendienteRevision,
        _ => ActorValidacionEstados.SinValidacion,
    };

    private static bool EsRolEmitible(string? actorType) =>
        string.Equals(actorType, "comprador", StringComparison.OrdinalIgnoreCase)
        || string.Equals(actorType, "vendedor", StringComparison.OrdinalIgnoreCase);

    private static int OrdenRol(string rol) => rol switch
    {
        ActorValidacionRoles.Comprador => 0,
        ActorValidacionRoles.Vendedor => 1,
        _ => 2,
    };

    private static string? NullSiVacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
