using Flit.Admin.Domain.Companies.LegalRepresentatives;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests de los agregados del directorio de representantes legales (HU #10900, ADR-0033): invariantes
/// de datos, exclusividad firma/identidad y validación de vigencia de escrituras.
/// </summary>
public sealed class LegalRepresentativeAggregateTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000bb01");
    private static readonly Guid CompanyId = Guid.Parse("77777777-0000-4000-8000-00000000bb02");

    [Fact]
    public void RepresentedCompany_Create_RequiresNitAndName()
    {
        var act = () => RepresentedCompany.Create(Tenant, documentNumber: " ", name: "ACME");
        act.Should().Throw<ArgumentException>();

        var actName = () => RepresentedCompany.Create(Tenant, "900000000-1", name: " ");
        actName.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RepresentedCompany_Create_TrimsAndDefaultsNit()
    {
        var company = RepresentedCompany.Create(Tenant, " 900000000-1 ", " ACME S.A.S. ");

        company.DocumentType.Should().Be("NIT");
        company.DocumentNumber.Should().Be("900000000-1");
        company.Name.Should().Be("ACME S.A.S.");
    }

    [Fact]
    public void LegalRepresentative_LinkSignature_ClearsIdentity_AndViceVersa()
    {
        var rep = LegalRepresentative.Create(
            Tenant, CompanyId, "CC", "123456789", "Perez", "Juan Perez");
        rep.HasSignatureOrIdentity.Should().BeFalse();

        var identityRef = Guid.NewGuid();
        rep.LinkIdentity(identityRef);
        rep.IdentityValidationRef.Should().Be(identityRef);
        rep.SignatureVaultId.Should().BeNull();

        // Al vincular la firma se limpia la identidad (excluyentes, precedencia baúl).
        var signatureId = Guid.NewGuid();
        rep.LinkSignature(signatureId);
        rep.SignatureVaultId.Should().Be(signatureId);
        rep.IdentityValidationRef.Should().BeNull();

        rep.ClearSignatureAndIdentity();
        rep.HasSignatureOrIdentity.Should().BeFalse();
    }

    [Fact]
    public void LegalRepresentative_Deactivate_IsLogicalAndReversible()
    {
        var rep = LegalRepresentative.Create(Tenant, CompanyId, "CC", "123456789", "Perez", "Juan Perez");
        rep.IsActive.Should().BeTrue();

        rep.Deactivate();
        rep.IsActive.Should().BeFalse();

        rep.Reactivate();
        rep.IsActive.Should().BeTrue();
    }

    [Fact]
    public void LegalRepresentative_Create_RequiresMandatoryData()
    {
        var act = () => LegalRepresentative.Create(Tenant, CompanyId, "CC", " ", "Perez", "Juan Perez");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LegalRepresentative_Create_AllowsPersonWithoutCompany()
    {
        var rep = LegalRepresentative.Create(
            Tenant, representedCompanyId: null, "CC", "123456789", "Perez", "Juan Perez");
        rep.RepresentedCompanyId.Should().BeNull();

        var fromEmpty = LegalRepresentative.Create(
            Tenant, Guid.Empty, "CC", "123456789", "Perez", "Juan Perez");
        fromEmpty.RepresentedCompanyId.Should().BeNull();
    }

    [Fact]
    public void Deed_Create_RejectsInvertedVigencia()
    {
        var act = () => Deed.Create(
            Tenant, "Escritura 123", "path", "sha256",
            new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("2026-07-23", true)]   // dentro de [desde, hasta]
    [InlineData("2026-01-01", true)]   // límite inferior inclusive
    [InlineData("2026-12-31", true)]   // límite superior inclusive
    [InlineData("2025-12-31", false)]  // antes de desde
    [InlineData("2027-01-01", false)]  // después de hasta
    public void Deed_EstaVigente_RespectsInclusiveRange(string todayText, bool expected)
    {
        var deed = Deed.Create(
            Tenant, "Escritura 123", "path", "sha256",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        deed.EstaVigente(DateOnly.Parse(todayText)).Should().Be(expected);
    }

    [Fact]
    public void Deed_Deactivate_MakesItNotVigente()
    {
        var deed = Deed.Create(
            Tenant, "Escritura 123", "path", "sha256",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        deed.Deactivate();

        deed.IsActive.Should().BeFalse();
        deed.EstaVigente(new DateOnly(2026, 7, 23)).Should().BeFalse();
    }
}
