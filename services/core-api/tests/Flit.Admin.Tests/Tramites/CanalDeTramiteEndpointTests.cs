using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #14318 (Feature #14315, Épica #14296) AC6 con el pipeline HTTP real y PostgreSQL real — el cliente no impone el canal:
/// <c>POST /api/v1/tramites/instances</c> con un cuerpo que trae <c>channel</c>, <c>channelTransitOfficeId</c>,
/// <c>origin: ict</c> u <c>origin: persona_natural</c> crea el trámite con canal <c>tramites</c> y sin OT del canal. El manejo de
/// <c>origin</c> no cambia (fuera de esta HU): solo se verifica el canal.
/// <para>Uso de ejemplo: <c>POST {"modalidad":"traspaso","channel":"pn_punto","origin":"ict"}</c> → 201 y <c>channel = 'tramites'</c>.</para>
/// Datos sintéticos; la compañía, el usuario y el tipo se retiran al terminar.
/// </summary>
public sealed class CanalDeTramiteEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    public static TheoryData<string> Cuerpos() => new()
    {
        """{ "channel": "pn_web" }""",
        """{ "channel": "pn_punto", "channelTransitOfficeId": "5a5a5a5a-0004-4000-8000-000000014318" }""",
        """{ "channel": "integracion" }""",
        """{ "origin": "ict" }""",
        """{ "origin": "persona_natural" }""",
        """{ "origin": "ict", "channel": "integracion" }""",
        """{ "origin": "persona_natural", "channel": "pn_punto", "channelTransitOfficeId": "5a5a5a5a-0004-4000-8000-000000014318" }""",
    };

    [Theory]
    [MemberData(nameof(Cuerpos))]
    public async Task AC6_UnCuerpoConCanalOOrigin_CreaElTramiteConCanalTramitesYSinOtDelCanal(string extras)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", e.Token());
        var cuerpo = JsonDocument.Parse(extras).RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString()));
        cuerpo["procedureTypeId"] = e.Tipo;

        var respuesta = await client.PostAsJsonAsync("/api/v1/tramites/instances", cuerpo, ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        var id = JsonDocument.Parse(texto).RootElement.GetProperty("id").GetGuid();
        var (canal, ot) = await e.CanalAsync(id, ct);
        canal.Should().Be("tramites", "el canal lo fija el servidor, no el cuerpo");
        ot.Should().BeNull();
    }

    [Fact]
    public async Task AC6_ElCuerpoSinCanalNiOrigin_CreaElTramiteConCanalTramites()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", e.Token());

        var respuesta = await client.PostAsJsonAsync("/api/v1/tramites/instances", new { procedureTypeId = e.Tipo }, ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        var id = JsonDocument.Parse(texto).RootElement.GetProperty("id").GetGuid();
        (await e.CanalAsync(id, ct)).Should().Be(("tramites", (Guid?)null));
    }

    /// <summary>Compañía, gestor y tipo de prueba, con códigos únicos; se retiran al terminar.</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly FlitDbContext _db;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        public Guid Compania { get; } = Guid.NewGuid();

        public Guid Gestor { get; } = Guid.NewGuid();

        public Guid Tipo { get; } = Guid.NewGuid();

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            _db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> factory, CancellationToken ct)
        {
            var e = new Escenario(factory.Services.CreateScope());
            e._db.Tenants.Add(new Tenant
            {
                Id = e.Compania, Code = $"H14318-{e._sufijo}", LegalName = "Compania de prueba 14318", TaxId = TestNit.Unique(),
                TenantType = "RENTING", IsGroupParent = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            await e._db.SaveChangesAsync(ct);
            e._db.Users.Add(new User
            {
                Id = e.Gestor, Email = $"gestor-{e._sufijo.ToLowerInvariant()}@example.test", DisplayName = "Gestor de prueba 14318", Status = "active",
                HomeTenantId = e.Compania, CreatedAt = DateTimeOffset.UtcNow,
            });
            await e._db.SaveChangesAsync(ct);
            await e._db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({e.Tipo}, {"T14318_" + e._sufijo}, {"Tipo de prueba " + e._sufijo}, 'TRASPASO', 1, {"{\"requiresBuyer\":true,\"requiresSeller\":true}"}::jsonb, 'published', now(), true, true, now())
                """, ct);
            return e;
        }

        public string Token() =>
            new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "https://api.flit.co",
                Audience = "flit-api",
                Subject = new ClaimsIdentity(
                [
                    new Claim("sub", Gestor.ToString()), new Claim("role", "AdminCompany"), new Claim("tenant_id", Compania.ToString()),
                ]),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
            });

        public async Task<(string? Canal, Guid? Ot)> CanalAsync(Guid id, CancellationToken ct)
        {
            _db.ChangeTracker.Clear();
            var fila = await _db.Set<ProcedureInstance>().AsNoTracking().SingleAsync(p => p.Id == id, ct);
            return (fila.Channel, fila.ChannelTransitOfficeId);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                await _db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_instances WHERE tenant_id = {Compania}");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = {Tipo}");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.users WHERE id = {Gestor}");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.tenants WHERE id = {Compania}");
                await tx.CommitAsync();
            }
            catch (Exception)
            {
                // Retiro best-effort: la base de la prueba es efímera.
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}
