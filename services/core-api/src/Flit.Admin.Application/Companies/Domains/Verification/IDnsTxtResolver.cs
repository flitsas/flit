namespace Flit.Admin.Application.Companies.Domains.Verification;

/// <summary>
/// Resultado de consultar el registro TXT de <c>_flit-verify.&lt;host&gt;</c> (HU #12425 AC1/AC2/AC7).
/// <see cref="Values"/> nunca se loguea íntegro fuera de este resultado (auditoría de DnsClient,
/// condición 5): los consumidores solo registran <see cref="Found"/> y el host.
/// </summary>
public sealed record DnsTxtLookupResult(bool Found, IReadOnlyList<string> Values, string? Error)
{
    public static DnsTxtLookupResult Success(IReadOnlyList<string> values) => new(true, values, null);

    public static DnsTxtLookupResult NotFound() => new(false, [], null);

    public static DnsTxtLookupResult Failure(string error) => new(false, [], error);
}

/// <summary>
/// Punto único de resolución DNS para la comprobación de titularidad (HU #12425 AC1, AC2, AC7).
/// Implementación real en <c>Flit.Infrastructure.Domains.DnsClientTxtResolver</c> (paquete
/// <c>DnsClient</c> 1.8.0, auditoría <c>.claude/state/marca-blanca/auditoria-dnsclient.md</c>);
/// implementación de pruebas <c>FakeDnsTxtResolver</c> (inyectable, AC7 "resolutor de DNS simulado").
/// </summary>
public interface IDnsTxtResolver
{
    /// <summary><paramref name="host"/> es el dominio del CLIENTE (ya normalizado); el resolutor consulta el nombre TXT <c>_flit-verify.&lt;host&gt;</c> por sí mismo.</summary>
    Task<DnsTxtLookupResult> LookupAsync(string host, CancellationToken cancellationToken = default);
}
