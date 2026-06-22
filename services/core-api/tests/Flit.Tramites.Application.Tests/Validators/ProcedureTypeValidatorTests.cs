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
            Family = ProcedureFamily.Matriculas,
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
            Family = ProcedureFamily.Traspaso,
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
            Family = ProcedureFamily.Traspaso,
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
            Family = ProcedureFamily.Otros,
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
}
