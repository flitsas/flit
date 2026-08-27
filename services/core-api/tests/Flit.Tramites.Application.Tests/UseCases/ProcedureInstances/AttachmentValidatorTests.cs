using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10520 (RF08/09/10) — validación de carga por tipo con respaldo a los límites globales.
/// </summary>
public sealed class AttachmentValidatorTests
{
    /// <summary>Catálogo en memoria: devuelve reglas por code exacto.</summary>
    private sealed class FakeCatalog(params DocumentTypeRule[] rules) : IDocumentTypeCatalog
    {
        private readonly Dictionary<string, DocumentTypeRule> _rules =
            rules.ToDictionary(r => r.Code, r => r);

        public Task<DocumentTypeRule?> GetRuleAsync(string tipo, CancellationToken ct = default) =>
            Task.FromResult(_rules.TryGetValue(tipo, out var r) ? r : null);

        public Task<IReadOnlySet<string>> ListSystemGeneratedCodesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static readonly string[] AllMimes =
        ["application/pdf", "image/jpeg", "image/png", "image/webp"];

    // ── Sin catálogo (null) ⇒ comportamiento global actual ────────────────────

    [Fact]
    public async Task NoCatalog_FallsBackToGlobalRules()
    {
        var v = new AttachmentValidator(catalog: null);
        var ct = TestContext.Current.CancellationToken;

        (await v.ValidateAsync("factura", "application/pdf", 1024, ct)).Should().BeNull();
        (await v.ValidateAsync("no_existe", "application/pdf", 1024, ct)).Should().Be("invalid_tipo");
        (await v.ValidateAsync("factura", "text/plain", 1024, ct)).Should().Be("invalid_mime");
        (await v.ValidateAsync("factura", "application/pdf", AttachmentRules.MaxSizeBytes + 1, ct))
            .Should().Be("file_too_large");
        (await v.ValidateAsync("factura", "application/pdf", 0, ct)).Should().Be("missing_file");
    }

    // ── Catálogo con reglas más estrictas por tipo ────────────────────────────

    [Fact]
    public async Task Catalog_TighterMime_RejectsDisallowedMimeForThatType()
    {
        var v = new AttachmentValidator(new FakeCatalog(
            new DocumentTypeRule("factura", ["application/pdf"], 20L * 1024 * 1024)));
        var ct = TestContext.Current.CancellationToken;

        // jpeg estaba permitido globalmente, pero el tipo lo restringe a pdf.
        (await v.ValidateAsync("factura", "image/jpeg", 1024, ct)).Should().Be("invalid_mime");
        (await v.ValidateAsync("factura", "application/pdf", 1024, ct)).Should().BeNull();
    }

    [Fact]
    public async Task Catalog_TighterSize_RejectsOversizeForThatType()
    {
        var v = new AttachmentValidator(new FakeCatalog(
            new DocumentTypeRule("factura", AllMimes, 1_000_000)));
        var ct = TestContext.Current.CancellationToken;

        (await v.ValidateAsync("factura", "application/pdf", 2_000_000, ct)).Should().Be("file_too_large");
        (await v.ValidateAsync("factura", "application/pdf", 500_000, ct)).Should().BeNull();
    }

    // ── Fallback por campos vacíos en la regla ────────────────────────────────

    [Fact]
    public async Task Catalog_EmptyRuleFields_FallBackToGlobal()
    {
        var v = new AttachmentValidator(new FakeCatalog(
            new DocumentTypeRule("factura", MimeTypesAllowed: [], MaxSizeBytes: 0)));
        var ct = TestContext.Current.CancellationToken;

        // Sin mimes ni tamaño en la regla ⇒ límites globales (jpeg permitido, 20 MB).
        (await v.ValidateAsync("factura", "image/jpeg", 1024, ct)).Should().BeNull();
        (await v.ValidateAsync("factura", "application/pdf", AttachmentRules.MaxSizeBytes, ct)).Should().BeNull();
    }

    // ── Aceptación aditiva: tipos del catálogo fuera de la whitelist legacy ────

    [Fact]
    public async Task Catalog_TypeOutsideLegacyWhitelist_IsAccepted()
    {
        // "rues" no está en AttachmentRules.ValidTipos, pero sí en el catálogo.
        AttachmentRules.ValidTipos.Should().NotContain("rues");
        var v = new AttachmentValidator(new FakeCatalog(
            new DocumentTypeRule("rues", AllMimes, 20L * 1024 * 1024)));
        var ct = TestContext.Current.CancellationToken;

        (await v.ValidateAsync("rues", "application/pdf", 1024, ct)).Should().BeNull();
    }

    [Fact]
    public async Task Catalog_UnknownTypeNotInWhitelistNorCatalog_IsRejected()
    {
        var v = new AttachmentValidator(new FakeCatalog());
        var ct = TestContext.Current.CancellationToken;

        (await v.ValidateAsync("inventado", "application/pdf", 1024, ct)).Should().Be("invalid_tipo");
    }

    [Fact]
    public async Task Catalog_LegacyTypeWithoutCatalogRow_StillAcceptedViaGlobal()
    {
        // "compraventa" está en la whitelist legacy; el catálogo no lo tiene ⇒ límites globales.
        var v = new AttachmentValidator(new FakeCatalog());
        var ct = TestContext.Current.CancellationToken;

        (await v.ValidateAsync("compraventa", "image/png", 1024, ct)).Should().BeNull();
    }
}
