using Flit.Admin.Application.Companies.Domains.Verification;

namespace Flit.Admin.Tests.Companies.Domains.Verification;

/// <summary>
/// Resolutor DNS simulado (HU #12425 AC7, "resolutor de DNS simulado") para tests de
/// <see cref="VerifyDomainHandler"/> y del <c>DomainVerificationSchedulerProcessor</c>: sin red real,
/// resultado programado por host. Uso de ejemplo:
/// <code>
/// var resolver = new FakeDnsTxtResolver();
/// resolver.SetMatch("app.example.com", "tok-123");
/// resolver.SetNotFound("otro.example.com");
/// </code>
/// </summary>
public sealed class FakeDnsTxtResolver : IDnsTxtResolver
{
    private readonly Dictionary<string, DnsTxtLookupResult> _byHost = new(StringComparer.OrdinalIgnoreCase);

    public void SetMatch(string host, string token) => _byHost[host] = DnsTxtLookupResult.Success([token]);

    public void SetValues(string host, params string[] values) => _byHost[host] = DnsTxtLookupResult.Success(values);

    public void SetNotFound(string host) => _byHost[host] = DnsTxtLookupResult.NotFound();

    public void SetError(string host, string error = "DNS_ERROR") => _byHost[host] = DnsTxtLookupResult.Failure(error);

    public Task<DnsTxtLookupResult> LookupAsync(string host, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byHost.TryGetValue(host, out var result) ? result : DnsTxtLookupResult.NotFound());
}
