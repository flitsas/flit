namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// CF-06 (HU #10881) — puerto que resuelve si el Organismo de Tránsito exige el documento de
/// prenda para un tipo de trámite, con INDEPENDENCIA del semáforo de gravámenes que dispara
/// <see cref="Flit.Tramites.Domain.Tramites.Services.PrendaGate"/> (R10, HU #10597). El override lo
/// arma el admin del OT sobre <c>tramites.document_requirement_overrides</c> (HU #10198) para el
/// documento canónico de prenda (<c>inscripcion_prenda</c>).
/// <para>
/// <b>SNAPSHOT (AC2):</b> el override solo aplica a trámites CREADOS después de activarse — un
/// trámite ya en curso no se ve afectado por un override que se activa mientras avanza. La
/// implementación compara la fecha de creación del override contra <paramref name="procedureCreatedAt"/>
/// (no hay resolución "en vivo" para este requisito puntual, a diferencia de la matriz documental
/// general que sí es viva por decisión de HU #10522).
/// </para>
/// </summary>
public interface IPrendaDocumentRequirementPolicy
{
    /// <summary>
    /// <c>true</c> = el OT exige el documento de prenda para este tipo de trámite y ya lo exigía
    /// al momento en que se creó el trámite evaluado. <c>false</c> = sin override activo (o activo
    /// solo después de la creación del trámite): no aplica, sin regresión.
    /// </summary>
    Task<bool> IsRequiredAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        DateTimeOffset procedureCreatedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Implementación permisiva (NUNCA exige) — default seguro para tests que no la ejercitan.</summary>
public sealed class NullPrendaDocumentRequirementPolicy : IPrendaDocumentRequirementPolicy
{
    public static NullPrendaDocumentRequirementPolicy Instance { get; } = new();

    public Task<bool> IsRequiredAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        DateTimeOffset procedureCreatedAt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
