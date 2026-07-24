namespace Flit.Admin.Application.Companies.Deeds.CreateDeed;

/// <summary>
/// Cuerpo del alta de una escritura (POST). El PDF NO viaja en el request: el backend registra el
/// documento en storage y devuelve una presigned POST policy para que el cliente suba el binario
/// DIRECTO a S3. <c>Sha256</c> es el hash hex (minúsculas) del PDF, calculado por el cliente, que se
/// persiste como integridad del artefacto (ADR-0033). <c>RepresentedCompanyIds</c> es la
/// multi-selección de compañías registradas a las que aplica la escritura.
/// </summary>
public sealed record CreateDeedRequest(
    string? Description,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    string? Sha256,
    IReadOnlyList<Guid>? RepresentedCompanyIds);
