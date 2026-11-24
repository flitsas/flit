namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Quién revisó (aprobó o rechazó) una validación de identidad manual: nombre, correo y rol. Se resuelve al LEER
/// (nunca se guarda en la auditoría, que está diseñada sin PII) y solo se expone al Super Admin FLIT.
/// Si el usuario ya no existe, los tres campos son <c>null</c> (no se inventa ni se muestra el id).
/// </summary>
public sealed record ReviewerInfo(string? Name, string? Email, string? Role);

/// <summary>Puerto de lectura de datos de usuarios revisores. Quien lo llama debe haber verificado que el caller es Super Admin.</summary>
public interface IReviewerDirectory
{
    /// <summary>Resuelve cada id a su <see cref="ReviewerInfo"/>; un id inexistente devuelve <c>ReviewerInfo(null, null, null)</c>.</summary>
    Task<IReadOnlyDictionary<Guid, ReviewerInfo>> ResolveAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
