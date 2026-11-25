using Flit.Admin.Application.Companies.RuntPersonOverrides;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.RuntOverrides;

/// <summary>
/// Tests unitarios del validador de overrides de persona RUNT (HU #14642).
/// </summary>
public sealed class TenantRuntPersonOverrideValidatorTests
{
    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateCreate_RequestValida_SinErrores()
    {
        var request = SampleCreateRequest();
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("CC")]
    [InlineData("CE")]
    [InlineData("PAS")]
    [InlineData("TI")]
    [InlineData("PPT")]
    public void ValidateCreate_DocumentTypePermitido_SinError(string docType)
    {
        var request = SampleCreateRequest() with { DocumentType = docType };
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        errors.Should().NotContain(e => e.Field == "documentType");
    }

    [Theory]
    [InlineData("NIT")]
    [InlineData("XX")]
    [InlineData("")]
    public void ValidateCreate_DocumentTypeNoPermitido_ErrorDocumentType(string docType)
    {
        var request = SampleCreateRequest() with { DocumentType = docType };
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        errors.Should().Contain(e => e.Field == "documentType");
    }

    [Fact]
    public void ValidateCreate_SinFirstName_ErrorFirstName()
    {
        var request = SampleCreateRequest() with { FirstName = "" };
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        errors.Should().Contain(e => e.Field == "firstName");
    }

    [Fact]
    public void ValidateCreate_SinFirstLastName_ErrorFirstLastName()
    {
        var request = SampleCreateRequest() with { FirstLastName = "   " };
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        errors.Should().Contain(e => e.Field == "firstLastName");
    }

    [Fact]
    public void ValidateCreate_SinReason_ErrorReason()
    {
        var request = SampleCreateRequest() with { Reason = "" };
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        errors.Should().Contain(e => e.Field == "reason");
    }

    [Fact]
    public void ValidateCreate_TenantIdVacio_ErrorTenantId()
    {
        var request = SampleCreateRequest() with { TenantId = Guid.Empty };
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        errors.Should().Contain(e => e.Field == "tenantId");
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateUpdate_RequestVaciaEnCamposNulos_SinErrores()
    {
        // Todos los campos null = no se modifica nada = válido.
        var request = new UpdateTenantRuntPersonOverrideRequest(
            FirstName: null, SecondName: null, FirstLastName: null, SecondLastName: null,
            CitizenStatus: null, DriverStatus: null, HasActiveLicense: null, LicenseCategories: null,
            HasPendingFines: null, NroPazYSalvo: null, Reason: null, IsActive: null, ExpiresAt: null,
            RequestedBy: null);

        var errors = TenantRuntPersonOverrideValidator.ValidateUpdate(request);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateUpdate_FirstNameVacio_ErrorFirstName()
    {
        var request = new UpdateTenantRuntPersonOverrideRequest(
            FirstName: "", SecondName: null, FirstLastName: null, SecondLastName: null,
            CitizenStatus: null, DriverStatus: null, HasActiveLicense: null, LicenseCategories: null,
            HasPendingFines: null, NroPazYSalvo: null, Reason: null, IsActive: null, ExpiresAt: null,
            RequestedBy: null);

        var errors = TenantRuntPersonOverrideValidator.ValidateUpdate(request);
        errors.Should().Contain(e => e.Field == "firstName");
    }

    [Fact]
    public void ValidateUpdate_ReasonVacio_ErrorReason()
    {
        var request = new UpdateTenantRuntPersonOverrideRequest(
            FirstName: null, SecondName: null, FirstLastName: null, SecondLastName: null,
            CitizenStatus: null, DriverStatus: null, HasActiveLicense: null, LicenseCategories: null,
            HasPendingFines: null, NroPazYSalvo: null, Reason: "   ", IsActive: null, ExpiresAt: null,
            RequestedBy: null);

        var errors = TenantRuntPersonOverrideValidator.ValidateUpdate(request);
        errors.Should().Contain(e => e.Field == "reason");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static CreateTenantRuntPersonOverrideRequest SampleCreateRequest() =>
        new(
            TenantId: Guid.NewGuid(),
            DocumentType: "CC",
            DocumentNumber: "123456789",
            FirstName: "JUAN",
            SecondName: null,
            FirstLastName: "PEREZ",
            SecondLastName: "GARCIA",
            CitizenStatus: "ACTIVA",
            DriverStatus: "ACTIVO",
            HasActiveLicense: true,
            LicenseCategories: "B1",
            HasPendingFines: false,
            NroPazYSalvo: null,
            Reason: "Persona no encontrada en RUNT por cambio de RH reciente",
            ExpiresAt: null,
            RequestedBy: Guid.NewGuid());
}
