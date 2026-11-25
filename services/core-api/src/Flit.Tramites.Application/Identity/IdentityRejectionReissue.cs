using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Reemisión AUTOMÁTICA del enlace tras un rechazo terminal de Kyverum (intentos agotados): si la persona aún tiene
/// rondas disponibles, se reemite el enlace y la ronda cuenta solo si Kyverum confirma el envío. Nunca lanza ni cambia
/// el veredicto del rechazo.
/// </summary>
public interface IIdentityRejectionReissue
{
    Task TryReissueAsync(ProcedureInstanceBiometricValidation validation, DateTimeOffset now, CancellationToken ct = default);
}
