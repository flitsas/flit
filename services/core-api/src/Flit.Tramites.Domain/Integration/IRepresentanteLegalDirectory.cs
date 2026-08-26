namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Puerto de <b>precarga</b> contra el directorio de representantes legales de Admin (ADR-0033).
/// Desacopla el módulo de trámites del de Admin, mismo patrón que
/// <see cref="ISignatureVaultPolicy"/> / <see cref="IIdentityValidationPolicy"/>.
///
/// <para><b>Fuente de datos, no de decisiones (HU #11663).</b> Este puerto tuvo un segundo método que
/// respondía si la COMPAÑÍA tenía algún representante "utilizable", y de esa respuesta colgaba el
/// disparo del correo de validación de identidad. Preguntaba por la empresa cuando lo que importaba era
/// la persona elegida en el trámite: bastaba que otro representante acreditado tuviera firma para dejar
/// sin validación al elegido. Esa decisión vive ahora en la precedencia única de envío (ADR-0039), y el
/// directorio se limita a aportar datos.</para>
/// </summary>
public interface IRepresentanteLegalDirectory
{
    /// <summary>
    /// HU #11198 (AC3) — nombre completo del representante ACTIVO de la compañía, como RESPALDO para los
    /// documentos cuando el trámite no lo trajo. Si <paramref name="documentNumber"/> viene, se busca a
    /// esa persona; si no viene y la compañía tiene <b>más de un</b> representante, devuelve <c>null</c>:
    /// adivinar imprimiría el nombre de otra persona en un documento legal.
    /// </summary>
    Task<string?> BuscarNombreRepresentanteAsync(
        Guid tenantId,
        string nitCompania,
        string? documentType,
        string? documentNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación inerte: no aporta nombre de respaldo. Es el default de los tests de
/// dominio/aplicación que no ejercitan el directorio.
/// </summary>
public sealed class NullRepresentanteLegalDirectory : IRepresentanteLegalDirectory
{
    public static NullRepresentanteLegalDirectory Instance { get; } = new();

    public Task<string?> BuscarNombreRepresentanteAsync(
        Guid tenantId,
        string nitCompania,
        string? documentType,
        string? documentNumber,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
