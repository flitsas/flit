using Flit.Admin.Application.Companies.Domains.GetDomain;
using Flit.Admin.Application.Companies.Domains.RegisterDomain;
using Flit.Admin.Application.Companies.Domains.RemoveDomain;
using Flit.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Infrastructure.Tests.Domains;

/// <summary>
/// Regresión del Bug #12735 (hijo de la HU #12416): los endpoints de
/// <c>AdminCompaniesDomainEndpoints</c> resuelven sus handlers por <c>[FromServices]</c>, pero
/// <see cref="AdminInfrastructureExtensions.AddAdminInfrastructure"/> nunca registró los handlers de
/// alta/consulta/retiro de dominio (solo los de verificación de la HU #12425). El resultado era un
/// HTTP 500 «No service for type ...Handler has been registered» al usar la consola de "Dominio de
/// la red". Estas pruebas fallan sin el registro y pasan con él; dependen solo de
/// <c>ITenantDomainRepository</c> y <c>DomainOptions</c>, ambos ya registrados por el mismo método.
/// </summary>
public sealed class AdminDomainHandlersRegistrationTests
{
    [Theory]
    [InlineData(typeof(RegisterDomainHandler))]
    [InlineData(typeof(GetDomainHandler))]
    [InlineData(typeof(RemoveDomainHandler))]
    public void AddAdminInfrastructure_RegistraLosHandlersDeDominioDeLaConsolaSuperAdmin(Type handlerType)
    {
        var services = new ServiceCollection();

        services.AddAdminInfrastructure();

        var descriptor = services.LastOrDefault(d => d.ServiceType == handlerType);

        descriptor.Should().NotBeNull(
            $"{handlerType.Name} lo resuelve AdminCompaniesDomainEndpoints por [FromServices] "
            + "y debe estar registrado en el contenedor (Bug #12735)");
        descriptor!.Lifetime.Should().Be(
            ServiceLifetime.Scoped,
            $"{handlerType.Name} depende de ITenantDomainRepository (Scoped) por petición");
    }
}
