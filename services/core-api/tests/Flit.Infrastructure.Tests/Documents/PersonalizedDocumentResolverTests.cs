using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// <see cref="PersonalizedDocumentResolver"/> (HU #11316, Feature #11309, ADR-0042) — adaptador de
/// <see cref="IPersonalizedDocumentResolver"/>. La garantía de PRODUCCIÓN de esta HU: aunque una
/// compañía tenga una versión ACTIVA de un tipo, el resolutor no la usa mientras ese tipo no esté en
/// <c>EnabledTypes</c> (vacío hasta las HUs #11317/#11318) — es la base del oráculo CF-01
/// (invisibilidad total con el canal actual).
/// </summary>
public sealed class PersonalizedDocumentResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly ICompanyPersonalizedDocumentRepository _repo = Substitute.For<ICompanyPersonalizedDocumentRepository>();
    private readonly ICompanyPersonalizedDocumentStorage _storage = Substitute.For<ICompanyPersonalizedDocumentStorage>();
    private readonly IPdfDocumentInspector _inspector = Substitute.For<IPdfDocumentInspector>();

    private PersonalizedDocumentResolver NewResolver() =>
        new(_repo, _storage, _inspector, NullLogger<PersonalizedDocumentResolver>.Instance);

    private static CompanyPersonalizedDocumentRecord ActiveRecord(string documentType) => new(
        Guid.NewGuid(), Tenant, documentType, 1, CompanyPersonalizedDocumentStatusForTest, true,
        $"{documentType}.pdf", "path/x.pdf", "sha-activa", 100, 3, null,
        DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, null, null);

    private const string CompanyPersonalizedDocumentStatusForTest = "activo";

    [Theory]
    [InlineData("mandato")]
    [InlineData("tramite_virtual")]
    public async Task ResolveAsync_ConVersionActiva_PeroTipoNoHabilitado_NoSustituyeNada(string tipo)
    {
        // Aunque el repositorio SÍ tenga una versión activa, EnabledTypes está vacío en esta HU: el
        // pipeline nunca debe leerla ni tocar storage.
        _repo.GetActiveAsync(Tenant, tipo, Arg.Any<CancellationToken>())
            .Returns(ActiveRecord(tipo));

        var resolver = NewResolver();
        var result = await resolver.ResolveAsync(Tenant, [tipo], TestContext.Current.CancellationToken);

        result.Resolved.Should().BeEmpty();
        result.Unavailable.Should().BeEmpty();
        await _repo.DidNotReceiveWithAnyArgs()
            .GetActiveAsync(default, default!, TestContext.Current.CancellationToken);
        await _storage.DidNotReceiveWithAnyArgs()
            .OpenReadAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveAsync_SinTipos_DevuelveVacioInmediatamente()
    {
        var resolver = NewResolver();

        var result = await resolver.ResolveAsync(Tenant, [], TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(PersonalizedDocumentResolution.Empty);
    }
}
