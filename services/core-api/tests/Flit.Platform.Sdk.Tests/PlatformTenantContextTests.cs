using System.Security.Claims;
using Flit.Platform.Sdk.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Platform.Sdk.Tests;

/// <summary>HU #13336 (Epic #13316) — empresa de la petición y filtro que falla cerrado (AC2).</summary>
public sealed class PlatformTenantContextTests
{
    private static readonly Guid EmpresaA = Guid.NewGuid();
    private static readonly Guid EmpresaB = Guid.NewGuid();

    private sealed record Fila(Guid TenantId, string Nombre);

    private static readonly IQueryable<Fila> Filas = new[] { new Fila(EmpresaA, "a1"), new Fila(EmpresaA, "a2"), new Fila(EmpresaB, "b1") }.AsQueryable();

    private static ClaimsPrincipal Usuario(Guid? tenant = null, string? role = null)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (tenant is { } t) claims.Add(new Claim("tenant_id", t.ToString()));
        if (role is not null) claims.Add(new Claim("role", role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static ClaimsPrincipal Servicio() => new(new ClaimsIdentity([new Claim("sub", "svc-consultas")], "Bearer"));

    private static HeaderDictionary Headers(Guid? tenant = null) =>
        tenant is { } t ? new HeaderDictionary { [PlatformTenantContext.Header] = t.ToString() } : new HeaderDictionary();

    private static string[] Nombres(PlatformTenantContext ctx) => [.. Filas.ForTenant(ctx, f => f.TenantId).Select(f => f.Nombre)];

    [Fact]
    public void UsuarioConEmpresa_SoloVeLaSuya_YLaCabeceraNoLaCambia()
    {
        var ctx = PlatformTenantContext.Resolve(Usuario(EmpresaA), Headers(EmpresaB));

        ctx.TenantId.Should().Be(EmpresaA);
        Nombres(ctx).Should().Equal("a1", "a2");
    }

    [Fact]
    public void UsuarioSinEmpresa_NoSuperAdmin_NoRecibeFilas()
    {
        var ctx = PlatformTenantContext.Resolve(Usuario(), Headers(EmpresaA));

        ctx.TenantId.Should().BeNull();
        Nombres(ctx).Should().BeEmpty();
    }

    [Fact]
    public void EmpresaVacia_CuentaComoSinEmpresa() =>
        Nombres(PlatformTenantContext.Resolve(Usuario(Guid.Empty), Headers())).Should().BeEmpty();

    [Fact]
    public void SuperAdmin_VeTodas() =>
        Nombres(PlatformTenantContext.Resolve(Usuario(role: "superadmin"), Headers())).Should().Equal("a1", "a2", "b1");

    [Fact]
    public void Servicio_ActuaPorLaEmpresaDeLaCabecera_YSinCabeceraNoVeNada()
    {
        Nombres(PlatformTenantContext.Resolve(Servicio(), Headers(EmpresaB))).Should().Equal("b1");
        Nombres(PlatformTenantContext.Resolve(Servicio(), Headers())).Should().BeEmpty();
    }

    [Fact]
    public void SinAutenticar_NoVeNada() =>
        Nombres(PlatformTenantContext.Resolve(new ClaimsPrincipal(new ClaimsIdentity()), Headers(EmpresaA))).Should().BeEmpty();
}
