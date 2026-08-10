using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// <see cref="PersonalizedDocumentResolver"/> (HU #11316/#11317/#11318, Feature #11309, ADR-0042) —
/// adaptador de <see cref="IPersonalizedDocumentResolver"/>. <c>mandato</c> se habilitó en la HU #11317
/// y <c>tramite_virtual</c> se habilita en ESTA HU (#11318): el resolutor SÍ los sustituye cuando hay
/// versión activa. Ningún otro tipo es personalizable en el Feature — <c>fur</c> ejerce aquí el oráculo
/// CF-01 (invisibilidad TOTAL y PERMANENTE para lo que el Feature nunca sustituye, AC4 de la HU #11318),
/// mismo mecanismo que antes probaba <c>tramite_virtual</c> mientras estaba pendiente.
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

    // ---- AC4 (HU #11318) — tipos EXCLUIDOS: el Feature nunca los sustituye, así el repositorio ----
    // ---- tenga una versión "activa" (imposible en producción, pero probado igual) -----------------

    [Theory]
    [InlineData("fur")]
    [InlineData("compraventa")]
    [InlineData("certificado_identidad")]
    [InlineData("certificado_rues")]
    [InlineData("certificado_rnmc")]
    [InlineData("certificado_soat_rtm")]
    [InlineData("escritura")]
    [InlineData("licencia_transito")]
    [InlineData("consolidado")]
    public async Task ResolveAsync_TipoExcluidoDelFeature_NuncaSustituyeAunqueHayaVersionActiva(string tipo)
    {
        // AC4 — compraventa, FUR, certificados de identidad/RUES/RNMC/SOAT-RTM, escrituras, licencia de
        // tránsito y consolidados siguen generados por FLIT sin sustituir: ninguno está en EnabledTypes
        // (el Feature #11309 solo declara 'mandato' y 'tramite_virtual', y esta HU #11318 cierra el
        // vocabulario). Aunque el repositorio SÍ tenga una versión activa para el tipo, el resolutor
        // nunca debe leerla ni tocar storage.
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
    public async Task ResolveAsync_Mandato_HabilitadoDesdeHU11317_ConVersionActiva_Sustituye()
    {
        // HU #11317 — mandato SÍ está habilitado: con una versión activa y legible, el resolutor
        // devuelve el documento de la compañía listo para sustituir.
        const string tipo = "mandato";
        var active = ActiveRecord(tipo);
        var contenido = "%PDF MANDATO DE LA COMPAÑÍA"u8.ToArray();
        _repo.GetActiveAsync(Tenant, tipo, Arg.Any<CancellationToken>()).Returns(active);
        _storage.OpenReadAsync(active.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(contenido));
        _inspector.Inspect(Arg.Any<byte[]>())
            .Returns(new PdfInspectionResult(IsParseable: true, IsEncrypted: false, PageCount: active.PageCount!.Value));

        var resolver = NewResolver();
        var result = await resolver.ResolveAsync(Tenant, [tipo], TestContext.Current.CancellationToken);

        result.Unavailable.Should().BeEmpty();
        var resolved = result.Resolved.Should().ContainSingle().Subject;
        resolved.Tipo.Should().Be(tipo);
        resolved.PersonalizedDocumentId.Should().Be(active.Id);
        resolved.Version.Should().Be(active.Version);
        resolved.Content.Should().BeEquivalentTo(contenido);
    }

    [Fact]
    public async Task ResolveAsync_TramiteVirtual_HabilitadoDesdeHU11318_ConVersionActiva_Sustituye()
    {
        // HU #11318 — tramite_virtual SÍ está habilitado: con una versión activa y legible, el
        // resolutor devuelve el documento de la compañía listo para sustituir. Cierra el Feature #11309:
        // era el último tipo declarado en EnabledTypes.
        const string tipo = "tramite_virtual";
        var active = ActiveRecord(tipo);
        var contenido = "%PDF SOLICITUD DE TRAMITE VIRTUAL DE LA COMPAÑÍA"u8.ToArray();
        _repo.GetActiveAsync(Tenant, tipo, Arg.Any<CancellationToken>()).Returns(active);
        _storage.OpenReadAsync(active.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(contenido));
        _inspector.Inspect(Arg.Any<byte[]>())
            .Returns(new PdfInspectionResult(IsParseable: true, IsEncrypted: false, PageCount: active.PageCount!.Value));

        var resolver = NewResolver();
        var result = await resolver.ResolveAsync(Tenant, [tipo], TestContext.Current.CancellationToken);

        result.Unavailable.Should().BeEmpty();
        var resolved = result.Resolved.Should().ContainSingle().Subject;
        resolved.Tipo.Should().Be(tipo);
        resolved.PersonalizedDocumentId.Should().Be(active.Id);
        resolved.Version.Should().Be(active.Version);
        resolved.Content.Should().BeEquivalentTo(contenido);
    }

    [Fact]
    public async Task ResolveAsync_SinTipos_DevuelveVacioInmediatamente()
    {
        var resolver = NewResolver();

        var result = await resolver.ResolveAsync(Tenant, [], TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(PersonalizedDocumentResolution.Empty);
    }
}
