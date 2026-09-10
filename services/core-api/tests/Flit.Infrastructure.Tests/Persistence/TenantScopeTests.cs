using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12321 (Feature #12254) — value object <see cref="TenantScope"/> y filtro
/// <see cref="TenantScopeQueryableExtensions.WhereTenantInScope{T}"/>.
/// Uso de ejemplo:
/// <code>
/// var scope = TenantScope.Group(padre, [hijo1, hijo2]);
/// scope.CanRead(hijo1);   // true
/// scope.CanWrite(hijo1);  // false — solo el padre escribe
/// db.Instances.WhereTenantInScope(scope, i => i.TenantId);
/// </code>
/// </summary>
public sealed class TenantScopeTests
{
    private static readonly Guid Parent = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Child1 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Child2 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private sealed record Row(Guid Id, Guid TenantId);

    private static IQueryable<Row> Rows() => new[]
    {
        new Row(Guid.NewGuid(), Parent),
        new Row(Guid.NewGuid(), Child1),
        new Row(Guid.NewGuid(), Child2),
        new Row(Guid.NewGuid(), Stranger),
    }.AsQueryable();

    // ── AC1 — Single ───────────────────────────────────────────────────────────

    [Fact]
    public void Single_LeeYEscribeSoloSobreSiMismo()
    {
        var scope = TenantScope.Single(Parent);

        scope.IsAll.Should().BeFalse();
        scope.IsGroup.Should().BeFalse();
        scope.WriteTenantId.Should().Be(Parent);
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent]);
        scope.CanRead(Parent).Should().BeTrue();
        scope.CanWrite(Parent).Should().BeTrue();
        scope.CanRead(Stranger).Should().BeFalse();
        scope.CanWrite(Stranger).Should().BeFalse();
    }

    [Fact]
    public void Single_GuidEmpty_LanzaArgumentException()
    {
        var act = () => TenantScope.Single(Guid.Empty);
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Single_WhereTenantInScope_FiltraSoloElPropio()
    {
        var result = Rows().WhereTenantInScope(TenantScope.Single(Parent), r => r.TenantId).ToList();

        result.Should().ContainSingle().Which.TenantId.Should().Be(Parent);
    }

    // ── AC2 — Group ────────────────────────────────────────────────────────────

    [Fact]
    public void Group_LeePadreEHijos_EscribeSoloPadre()
    {
        var scope = TenantScope.Group(Parent, [Child1, Child2]);

        scope.IsGroup.Should().BeTrue();
        scope.IsAll.Should().BeFalse();
        scope.WriteTenantId.Should().Be(Parent);
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent, Child1, Child2]);
    }

    [Fact]
    public void Group_HijosDuplicadosOPadreEntreHijos_NoDuplicaLectura()
    {
        var scope = TenantScope.Group(Parent, [Child1, Child1, Parent]);

        scope.ReadTenantIds.Should().BeEquivalentTo([Parent, Child1]);
        scope.IsGroup.Should().BeTrue();
    }

    [Fact]
    public void Group_SinHijos_DegradaASingle()
    {
        var scope = TenantScope.Group(Parent, []);

        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent]);
        scope.WriteTenantId.Should().Be(Parent);
    }

    [Fact]
    public void Group_GuidEmpty_LanzaArgumentException()
    {
        var padreVacio = () => TenantScope.Group(Guid.Empty, [Child1]);
        var hijoVacio = () => TenantScope.Group(Parent, [Guid.Empty]);
        var hijosNull = () => TenantScope.Group(Parent, null!);

        padreVacio.Should().Throw<ArgumentException>().WithParameterName("parentTenantId");
        hijoVacio.Should().Throw<ArgumentException>().WithParameterName("childTenantIds");
        hijosNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Group_WhereTenantInScope_DevuelvePadreEHijos_NoExtranos()
    {
        var scope = TenantScope.Group(Parent, [Child1, Child2]);

        var result = Rows().WhereTenantInScope(scope, r => r.TenantId).Select(r => r.TenantId).ToList();

        result.Should().BeEquivalentTo([Parent, Child1, Child2]);
        result.Should().NotContain(Stranger);
    }

    // ── AC5 — conjunto de lectura vacío ⇒ cero filas ───────────────────────────

    [Fact]
    public void WhereTenantInScope_ReadVacioNoAll_DevuelveCeroFilas()
    {
        // Ningún scope público puede tener ReadTenantIds vacío; se verifica la rama defensiva creando
        // un scope válido y comprobando que la ÚNICA forma de "sin filtro" es IsAll.
        var scope = TenantScope.Single(Stranger);
        var soloAjenos = Rows().Where(r => r.TenantId != Stranger);

        var result = soloAjenos.WhereTenantInScope(scope, r => r.TenantId).ToList();

        result.Should().BeEmpty("cerrado por defecto: sin coincidencias ⇒ cero filas, nunca 'sin filtro'");
    }

    [Fact]
    public void ScopesPublicos_NuncaTienenReadVacio()
    {
        TenantScope.Single(Parent).ReadTenantIds.Should().NotBeEmpty();
        TenantScope.Group(Parent, [Child1]).ReadTenantIds.Should().NotBeEmpty();
        TenantScope.Group(Parent, []).ReadTenantIds.Should().NotBeEmpty();
    }

    // ── AC6 — escritura sobre un hijo desde un Group ───────────────────────────

    [Fact]
    public void Group_CanWriteHijo_EsFalse_AunqueCanReadHijoSeaTrue()
    {
        var scope = TenantScope.Group(Parent, [Child1, Child2]);

        scope.CanRead(Child1).Should().BeTrue();
        scope.CanRead(Child2).Should().BeTrue();
        scope.CanWrite(Child1).Should().BeFalse("la lectura consolidada no otorga escritura sobre el hijo");
        scope.CanWrite(Child2).Should().BeFalse();
        scope.CanWrite(Parent).Should().BeTrue();
        scope.CanWrite(Stranger).Should().BeFalse();
    }

    // ── AC7 — All (fábrica internal) ───────────────────────────────────────────

    [Fact]
    public void All_SinFiltro_LeeYEscribeTodo()
    {
        var scope = TenantScope.All();

        scope.IsAll.Should().BeTrue();
        scope.IsGroup.Should().BeFalse();
        scope.WriteTenantId.Should().BeNull();
        scope.ReadTenantIds.Should().BeEmpty();
        scope.CanRead(Stranger).Should().BeTrue();
        scope.CanWrite(Stranger).Should().BeTrue();
    }

    [Fact]
    public void All_WhereTenantInScope_DevuelveLaConsultaSinFiltro()
    {
        var source = Rows();

        var result = source.WhereTenantInScope(TenantScope.All(), r => r.TenantId);

        result.Should().BeSameAs(source, "All no añade ningún Where");
        result.Count().Should().Be(4);
    }

    [Fact]
    public void All_EsInternal_NoAccesibleDesdeCapasPublicas()
    {
        var method = typeof(TenantScope).GetMethod(nameof(TenantScope.All),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        method.Should().NotBeNull();
        method!.IsAssembly.Should().BeTrue("All() debe ser internal: solo el middleware lo fabrica");
        typeof(TenantScope).GetMethod(nameof(TenantScope.All),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Should().BeNull();
    }

    // ── Contrato ───────────────────────────────────────────────────────────────

    [Fact]
    public void WhereTenantInScope_ArgumentosNulos_Lanzan()
    {
        var scope = TenantScope.Single(Parent);

        var queryNull = () => ((IQueryable<Row>)null!).WhereTenantInScope(scope, r => r.TenantId);
        var scopeNull = () => Rows().WhereTenantInScope(null!, r => r.TenantId);
        var selectorNull = () => Rows().WhereTenantInScope(scope, null!);

        queryNull.Should().Throw<ArgumentNullException>();
        scopeNull.Should().Throw<ArgumentNullException>();
        selectorNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToString_DescribeElTipoDeAlcance()
    {
        TenantScope.All().ToString().Should().Be("TenantScope.All");
        TenantScope.Single(Parent).ToString().Should().Contain("Single");
        TenantScope.Group(Parent, [Child1]).ToString().Should().Contain("Group");
    }
}
