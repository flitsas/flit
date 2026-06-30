using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// El resolver de proveedores de identidad es provider-agnostic: resuelve por nombre (case-insensitive)
/// entre los registrados; null si no hay ninguno. Así la cola de envío sirve para cualquier proveedor.
/// </summary>
public sealed class IdentityValidationProviderResolverTests
{
    private static IIdentityValidationProvider Provider(string name)
    {
        var p = Substitute.For<IIdentityValidationProvider>();
        p.Name.Returns(name);
        return p;
    }

    [Fact]
    public void Resuelve_por_nombre_case_insensitive()
    {
        var kyverum = Provider(BiometricProviders.Kyverum);
        var otro = Provider("otro");
        var resolver = new IdentityValidationProviderResolver(new[] { otro, kyverum });

        resolver.Resolve("KYVERUM").Should().BeSameAs(kyverum);
        resolver.Resolve("otro").Should().BeSameAs(otro);
    }

    [Fact]
    public void Devuelve_null_si_no_hay_proveedor_registrado()
    {
        var resolver = new IdentityValidationProviderResolver(new[] { Provider(BiometricProviders.Kyverum) });

        resolver.Resolve("inexistente").Should().BeNull();
    }
}
