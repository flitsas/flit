using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Validators;

public sealed class ProcedureTypeValidatorTests
{
    private readonly ProcedureTypeValidator _sut = new();

    private static ProcedureType BuildMatriculasWithVehicle(bool includePlateOrVin, bool locked = true)
    {
        var section = new ProcedureSection
        {
            Id = Guid.NewGuid(),
            Code = "SEC1",
            Title = "Vehicle Data",
            SortOrder = 0,
            Layout = "single",
            CreatedAt = DateTimeOffset.UtcNow,
            FormFields = []
        };

        if (includePlateOrVin)
        {
            section.FormFields.Add(new FormField
            {
                Id = Guid.NewGuid(),
                ProcedureSectionId = section.Id,
                FieldKey = "plate_or_vin",
                Label = "Placa o VIN",
                FieldType = "text",
                IsRequired = true,
                IsLocked = locked,
                SortOrder = 0,
                ValidationSchema = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        var step = new ProcedureStep
        {
            Id = Guid.NewGuid(),
            Code = "STEP1",
            Title = "Step 1",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            Sections = [section]
        };
        section.ProcedureStepId = step.Id;

        var entity = new ProcedureEntity
        {
            Id = Guid.NewGuid(),
            Code = "VEHICLE",
            Name = "Vehículo",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "MAT_TEST",
            Name = "Matrícula Test",
            Family = ProcedureFamilyCodes.Matriculas,
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            Steps = [step],
            ConformationRules =
            [
                new ConformationRule
                {
                    Id = Guid.NewGuid(),
                    IsActive = true,
                    SortOrder = 0,
                    ValidationProfile = "{}",
                    ProcedureEntity = entity,
                    ProcedureEntityId = entity.Id,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        step.ProcedureTypeId = pt.Id;
        return pt;
    }

    [Fact]
    public void Validate_Matriculas_VehicleActive_NoLockedPlateOrVin_Returns_VIN_PLATE_RULE()
    {
        var pt = BuildMatriculasWithVehicle(includePlateOrVin: false);

        var result = _sut.Validate(pt);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "VIN_PLATE_RULE");
    }

    [Fact]
    public void Validate_Matriculas_VehicleActive_WithLockedPlateOrVin_NoError()
    {
        var pt = BuildMatriculasWithVehicle(includePlateOrVin: true, locked: true);

        var result = _sut.Validate(pt);

        result.Errors.Should().NotContain(e => e.Code == "VIN_PLATE_RULE");
    }

    [Fact]
    public void Validate_Matriculas_VehicleActive_PlateOrVin_NotLocked_Returns_VIN_PLATE_RULE()
    {
        var pt = BuildMatriculasWithVehicle(includePlateOrVin: true, locked: false);

        var result = _sut.Validate(pt);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "VIN_PLATE_RULE");
    }

    [Fact]
    public void Validate_Traspaso_NoVinPlateRule_NoError()
    {
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "TRAS_TEST",
            Name = "Traspaso Test",
            Family = ProcedureFamilyCodes.Traspaso,
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ConformationRules = [],
            Steps = []
        };

        var result = _sut.Validate(pt);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NIT_DocumentType_WithoutRuesTemplate_Returns_NIT_PERSON_TYPE()
    {
        var section = new ProcedureSection
        {
            Id = Guid.NewGuid(),
            Code = "SEC_OWNER",
            Title = "Owner",
            SortOrder = 0,
            Layout = "single",
            CreatedAt = DateTimeOffset.UtcNow,
            FormFields =
            [
                new FormField
                {
                    Id = Guid.NewGuid(),
                    FieldKey = "document_type",
                    Label = "Tipo de documento",
                    FieldType = "select",
                    IsRequired = true,
                    SortOrder = 0,
                    Options = "[\"CC\",\"CE\",\"NIT\"]",
                    ValidationSchema = "{}",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var step = new ProcedureStep
        {
            Id = Guid.NewGuid(),
            Code = "STEP_OWNER",
            Title = "Owner Step",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            Sections = [section]
        };
        section.ProcedureStepId = step.Id;

        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "TRAS_NIT",
            Name = "Traspaso NIT",
            Family = ProcedureFamilyCodes.Traspaso,
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ConformationRules = [],
            Steps = [step]
        };
        step.ProcedureTypeId = pt.Id;

        var result = _sut.Validate(pt);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "NIT_PERSON_TYPE");
    }

    [Fact]
    public void Validate_ConsultationTemplate_MissingRequiredKey_Returns_INCOMPLETE_CONSULTATION_FIELDS()
    {
        var template = new ConsultationTemplate
        {
            Id = Guid.NewGuid(),
            Code = "RUNT_VEHICLE",
            Name = "RUNT Vehículo",
            EntityScope = "vehicle",
            RequiredFieldKeys = "[\"plate_or_vin\"]",
            IsActive = true,
            RequestSchema = "{}",
            ExternalRefs = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            ExternalDataSourceId = Guid.NewGuid()
        };

        var section = new ProcedureSection
        {
            Id = Guid.NewGuid(),
            Code = "SEC1",
            Title = "Vehicle",
            SortOrder = 0,
            Layout = "single",
            CreatedAt = DateTimeOffset.UtcNow,
            FormFields =
            [
                new FormField
                {
                    Id = Guid.NewGuid(),
                    FieldKey = "other_field",
                    Label = "Other",
                    FieldType = "text",
                    IsRequired = false,
                    SortOrder = 0,
                    IsLocked = true,
                    ConsultationTemplateId = template.Id,
                    ConsultationTemplate = template,
                    ValidationSchema = "{}",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var step = new ProcedureStep
        {
            Id = Guid.NewGuid(),
            Code = "S1",
            Title = "S1",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            Sections = [section]
        };
        section.ProcedureStepId = step.Id;

        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "TEST_TMPL",
            Name = "Test Template",
            Family = ProcedureFamilyCodes.Otros,
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ConformationRules = [],
            Steps = [step]
        };
        step.ProcedureTypeId = pt.Id;

        var result = _sut.Validate(pt);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "INCOMPLETE_CONSULTATION_FIELDS");
    }

    // ── ADR-0050 / CFD-09: familia, gate_profile y section_type ─────────────────────────────────
    // El validador solo cubría tres reglas y ninguna tocaba estos campos, así que un tipo podía
    // publicarse con una familia fuera de dominio (que el CHECK del DDL rechaza más abajo) o con
    // secciones cuyo section_type cae en el default del evaluador y nunca bloquea.

    private static ProcedureType TipoMinimo(
        string family = ProcedureFamilyCodes.Otros,
        string gateProfile = "{}",
        string sectionType = ProcedureSectionTypes.GenericForm) => new()
    {
        Id = Guid.NewGuid(),
        Code = "TIPO_TEST",
        Name = "Tipo de prueba",
        Family = family,
        GateProfile = gateProfile,
        PublicationStatus = PublicationStatus.Draft,
        CreatedAt = DateTimeOffset.UtcNow,
        Steps =
        [
            new ProcedureStep
            {
                Id = Guid.NewGuid(),
                Code = "PASO",
                Title = "Paso",
                SortOrder = 1,
                IsActive = true,
                Sections =
                [
                    new ProcedureSection
                    {
                        Id = Guid.NewGuid(),
                        Code = "SECCION",
                        Title = "Sección",
                        SortOrder = 1,
                        SectionType = sectionType,
                        FormFields = [],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void TipoMinimoBienFormado_EsValido()
    {
        _sut.Validate(TipoMinimo()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("VEHICULAR")]   // la familia que sembró la migración SeedProcedureTypes
    [InlineData("matriculas ")] // el parser tolera espacios/minúsculas: esto SÍ debe ser válido
    public void Family_SeValidaContraElDominio(string family)
    {
        var result = _sut.Validate(TipoMinimo(family: family));

        if (ProcedureFamilyCodes.IsValid(family))
            result.Errors.Should().NotContain(e => e.Code == "FAMILY_INVALID");
        else
            result.Errors.Should().Contain(e => e.Code == "FAMILY_INVALID");
    }

    [Fact]
    public void GateProfile_EntryModeInvalido_EsError()
    {
        var result = _sut.Validate(TipoMinimo(gateProfile: """{"entryMode":"CHASIS"}"""));

        result.Errors.Should().Contain(e => e.Code == "GATE_PROFILE_ENTRY_MODE_INVALID");
    }

    [Fact]
    public void GateProfile_BiometriaSinActores_EsError()
    {
        // Sin actores el gate biométrico se satisface siempre: la identidad nunca bloquearía.
        var result = _sut.Validate(TipoMinimo(gateProfile: """{"requiresBiometrics":true}"""));

        result.Errors.Should().Contain(e => e.Code == "GATE_PROFILE_BIOMETRIC_ACTORS_MISSING");
    }

    [Fact]
    public void GateProfile_BiometriaConActores_EsValido()
    {
        var result = _sut.Validate(TipoMinimo(
            gateProfile: """{"requiresBiometrics":true,"biometricActors":["BUYER"]}"""));

        result.Errors.Should().NotContain(e => e.Code == "GATE_PROFILE_BIOMETRIC_ACTORS_MISSING");
    }

    [Fact]
    public void SectionType_FueraDelCatalogo_EsError()
    {
        var result = _sut.Validate(TipoMinimo(sectionType: "tabla_dinamica"));

        result.Errors.Should().Contain(e =>
            e.Code == "SECTION_TYPE_INVALID" && e.Path == "steps.PASO.sections.SECCION.sectionType");
    }

    [Fact]
    public void SectionType_DelCatalogo_EsValido()
    {
        var result = _sut.Validate(TipoMinimo(sectionType: ProcedureSectionTypes.PrendaDecision));

        result.Errors.Should().NotContain(e => e.Code == "SECTION_TYPE_INVALID");
    }
}
