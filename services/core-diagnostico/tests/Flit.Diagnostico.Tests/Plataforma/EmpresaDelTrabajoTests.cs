using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Platform.Sdk.Tenancy;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13727 (ADR-0075) — <see cref="EmpresaDelTrabajo"/>: la empresa del scope. Sin nada fijado es la de la petición (el
/// accessor HTTP del SDK, que decora); un trabajo en segundo plano (el procesador de la cola, más adelante el consumidor de
/// resultados) la fija una vez con <see cref="EmpresaDelTrabajo.Fijar"/> y desde ahí el <c>DiagnosticoDb</c> y la outbox del
/// mismo scope filtran y escriben con ella. No admite la empresa cero ni cambiar a otra empresa en el mismo scope.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EmpresaDelTrabajoTests
/// </code>
/// </example>
public sealed class EmpresaDelTrabajoTests
{
    [Fact]
    public void Sin_nada_no_hay_empresa()
    {
        new EmpresaDelTrabajo(new EmpresaFija(null)).Current.Should().Be(PlatformTenantContext.None);
    }

    [Fact]
    public void Sin_fijar_es_la_empresa_de_la_peticion()
    {
        var empresa = Guid.NewGuid();

        new EmpresaDelTrabajo(new EmpresaFija(empresa)).Current.TenantId.Should().Be(empresa);
    }

    [Fact]
    public void Fijada_es_esa_empresa_como_servicio_aunque_la_peticion_traiga_otra()
    {
        var empresa = Guid.NewGuid();
        var accessor = new EmpresaDelTrabajo(new EmpresaFija(Guid.NewGuid()));

        accessor.Fijar(empresa);

        accessor.Current.Should().Be(new PlatformTenantContext(empresa, IsSuperAdmin: false, IsService: true));
        accessor.Fijar(empresa);
        accessor.Current.TenantId.Should().Be(empresa, "volver a fijar la misma empresa no cambia nada");
    }

    [Fact]
    public void No_fija_la_empresa_cero()
    {
        var fijar = () => new EmpresaDelTrabajo(new EmpresaFija(null)).Fijar(Guid.Empty);

        fijar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void No_cambia_a_otra_empresa_en_el_mismo_scope()
    {
        var empresa = Guid.NewGuid();
        var accessor = new EmpresaDelTrabajo(new EmpresaFija(null));
        accessor.Fijar(empresa);

        var otra = () => accessor.Fijar(Guid.NewGuid());

        otra.Should().Throw<InvalidOperationException>();
        accessor.Current.TenantId.Should().Be(empresa);
    }
}
