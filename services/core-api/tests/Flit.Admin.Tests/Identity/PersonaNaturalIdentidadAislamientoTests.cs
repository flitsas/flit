using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cliente = Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14044 (Feature #14023, Épica #14021) — decisión del PO: las validaciones del portal Persona Natural (ancla
/// <c>portal_solicitud_ref</c>) NO se reutilizan entre trámites ni vigencian. Con Postgres real y la fila creada por la
/// fachada gRPC, ninguna consulta de FLIT 2 por documento (vigente, en vuelo, bloqueos, llaves vigentes, representante
/// legal) debe verla; y una fila SIN ancla del mismo tenant y documento sigue viéndose igual que antes.
/// </summary>
public sealed class PersonaNaturalIdentidadAislamientoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scope = "platform.tramites.persona_natural";
    private const string Portal = "svc-personanatural";

    [Fact]
    public async Task UnaValidacionAprobadaDelPortal_NoLaVeNingunaConsultaDeFlit2_PeroUnaSinAnclaSi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var cedulaPortal = $"7{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var cedulaFlit2 = $"6{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var aprobada = await cliente.SolicitarValidacionIdentidadAsync(Pedido($"pn-aisl-{Guid.NewGuid():N}", cedulaPortal), cancellationToken: ct);
        await MarcarAprobadaAsync(host, Guid.Parse(aprobada.ValidacionId), ct);
        var enVuelo = await cliente.SolicitarValidacionIdentidadAsync(Pedido($"pn-aisl-{Guid.NewGuid():N}", cedulaFlit2), cancellationToken: ct);
        // Control: una prevalidación standalone de FLIT 2 (sin ancla) en el MISMO tenant y con la MISMA cédula de la solicitud en vuelo.
        await SembrarSinAnclaAsync(host, cedulaFlit2, BiometricEstados.Enviado, ct);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IProcedureInstanceRepository>();
        var ahora = DateTimeOffset.UtcNow;
        var tenant = PlatformTenants.PersonaNatural;

        // Aprobada del portal: invisible para el gate, el representante legal y las llaves vigentes.
        (await repo.FindVigenteApprovedByDocumentAsync(tenant, "CC", cedulaPortal, ahora, ct)).Should().BeNull();
        (await new RepresentativeIdentityLookup(db).FindVigenteIdentityRefAsync(tenant, "CC", cedulaPortal, ahora, ct)).Should().BeNull();
        (await repo.ListVigenteApprovedIdentityKeysAsync([tenant], ahora, ct))
            .Should().NotContain(k => k.Contains(cedulaPortal, StringComparison.OrdinalIgnoreCase));
        (await repo.ListInFlightByDocumentAsync(tenant, "CC", cedulaPortal, ct)).Should().BeEmpty();
        (await repo.ListInFlightBlockersByDocumentAsync(tenant, "CC", cedulaPortal, ct)).Should().BeEmpty();

        // En vuelo del portal: no cuenta para precedencia de envío ni bloquea; la standalone sin ancla sí (idéntico a antes).
        var candidatos = await repo.ListInFlightByDocumentAsync(tenant, "CC", cedulaFlit2, ct);
        candidatos.Should().ContainSingle().Which.PortalSolicitudRef.Should().BeNull();
        candidatos.Should().NotContain(v => v.Id == Guid.Parse(enVuelo.ValidacionId));
        (await repo.ListInFlightBlockersByDocumentAsync(tenant, "CC", cedulaFlit2, ct))
            .Should().ContainSingle().Which.PortalSolicitudRef.Should().BeNull();

        // Los métodos propios del portal siguen viendo su fila.
        (await db.ProcedureInstanceBiometricValidations.AsNoTracking().CountAsync(v => v.Id == Guid.Parse(aprobada.ValidacionId), ct)).Should().Be(1);
    }

    [Fact]
    public async Task UnaValidacionAprobadaSinAncla_SiSeVeComoAntes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cedula = $"5{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        await SembrarSinAnclaAsync(host, cedula, BiometricEstados.Aprobado, ct);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IProcedureInstanceRepository>();
        var ahora = DateTimeOffset.UtcNow;
        var tenant = PlatformTenants.PersonaNatural;

        (await repo.FindVigenteApprovedByDocumentAsync(tenant, "CC", cedula, ahora, ct)).Should().NotBeNull();
        (await new RepresentativeIdentityLookup(db).FindVigenteIdentityRefAsync(tenant, "CC", cedula, ahora, ct)).Should().NotBeNull();
        (await repo.ListVigenteApprovedIdentityKeysAsync([tenant], ahora, ct))
            .Should().Contain(k => k.Contains(cedula, StringComparison.OrdinalIgnoreCase));
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static SolicitarValidacionIdentidadRequest Pedido(string solicitud, string cedula) => new()
    {
        SolicitudId = solicitud,
        Parte = "vendedor",
        Persona = new PersonaIdentidad { TipoDocumento = "CC", NumeroDocumento = cedula, Nombre = "Persona de Prueba", Correo = "prueba@ejemplo.test", Celular = "3000000000" },
    };

    private WebApplicationFactory<Program> Host() =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("PersonaNatural:Fachada:Habilitada", "true");
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Scopes:0", Scope);
        });

    private static async Task<Cliente> ClienteAsync(WebApplicationFactory<Program> host, CancellationToken ct)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Portal,
            ["client_secret"] = OidcServerTests.ServiceSecret,
            ["scope"] = Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = json.RootElement.GetProperty("access_token").GetString()!;
        var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        return new Cliente(canal.CreateCallInvoker().Intercept(metadata =>
        {
            metadata.Add("authorization", $"Bearer {token}");
            metadata.Add("x-flit-tenant-id", PlatformTenants.PersonaNatural.ToString());
            return metadata;
        }));
    }

    private static async Task MarcarAprobadaAsync(WebApplicationFactory<Program> host, Guid id, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        await db.ProcedureInstanceBiometricValidations.Where(v => v.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(v => v.Status, BiometricEstados.Aprobado)
            .SetProperty(v => v.ValidatedAt, (DateTimeOffset?)ahora)
            .SetProperty(v => v.ValidUntil, (DateTimeOffset?)ahora.AddDays(30)), ct);
    }

    /// <summary>Una validación sin ancla de portal (como una prevalidación standalone de FLIT 2) en el tenant Persona Natural.</summary>
    private static async Task SembrarSinAnclaAsync(WebApplicationFactory<Program> host, string cedula, string estado, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var persona = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = PlatformTenants.PersonaNatural,
            DocumentType = "CC",
            DocumentNumber = cedula,
            FullName = "Persona de FLIT 2",
            Email = "flit2@ejemplo.test",
        };
        db.Add(persona);
        db.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = PlatformTenants.PersonaNatural,
            PersonId = persona.Id,
            Name = "Persona de FLIT 2",
            DocumentType = "CC",
            DocumentNumber = cedula,
            Email = "flit2@ejemplo.test",
            RegisteredEmail = "flit2@ejemplo.test",
            Status = estado,
            Provider = BiometricProviders.Kyverum,
            ValidatedAt = estado == BiometricEstados.Aprobado ? ahora : null,
            ValidUntil = estado == BiometricEstados.Aprobado ? ahora.AddDays(30) : null,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = ahora.AddHours(24),
            CreatedAt = ahora,
        });
        await db.SaveChangesAsync(ct);
    }
}
