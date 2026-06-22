using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class VerifikConductorResultMapperTests
{
    private static VerifikConductorResponse Response(
        string? fullName = null,
        string? firstName = null,
        string? lastName = null,
        string? estadoUsuario = null) =>
        new()
        {
            Data = new VerifikConductorData
            {
                DocumentType = "CC",
                DocumentNumber = "123456789",
                FirstName = firstName,
                LastName = lastName,
                FullName = fullName,
                IdentityValidationAttempts = estadoUsuario is null
                    ? null
                    : new VerifikConductorIdentityValidation { EstadoUsuario = estadoUsuario },
            },
        };

    private static ConsultationCheck Check(ConsultationResult r, string key) =>
        r.Checks.Single(c => c.Key == key);

    [Fact]
    public void PersonaHallada_ProduceOkGreen_YHidrataNombre()
    {
        var result = VerifikConductorResultMapper.Map(
            Response(fullName: "MATEO VERIFIK", firstName: "MATEO", lastName: "VERIFIK"));

        Check(result, "conductor").Status.Should().Be("ok");
        result.Overall.Should().Be("green");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_full_name" && f.ValueText == "MATEO VERIFIK");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_first_name" && f.ValueText == "MATEO");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_last_name" && f.ValueText == "VERIFIK");
    }

    [Fact]
    public void LicenseStatus_SeHidrataCuandoPresente()
    {
        var result = VerifikConductorResultMapper.Map(
            Response(fullName: "MATEO VERIFIK", estadoUsuario: "ACTIVO"));

        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_license_status" && f.ValueText == "ACTIVO");
    }

    [Fact]
    public void SinFullName_ProduceUnknownYellow_SinHidratar()
    {
        var result = VerifikConductorResultMapper.Map(Response(fullName: null));

        Check(result, "conductor").Status.Should().Be("unknown");
        result.Overall.Should().Be("yellow");
        result.HydratedFields.Should().BeEmpty();
    }

    [Fact]
    public void NullData_DoesNotThrow_ProduceUnknown()
    {
        var result = VerifikConductorResultMapper.Map(new VerifikConductorResponse());

        result.Provider.Should().Be("verifik_conductor");
        result.Checks.Should().HaveCount(1);
        Check(result, "conductor").Status.Should().Be("unknown");
        result.HydratedFields.Should().BeEmpty();
    }

    [Fact]
    public void MockSinteticoDeterminista_ProduceNombreEstable()
    {
        // Misma carga sintética que arma el provider en modo mock (sin token). El nombre
        // debe ser estable y obviamente sintético para que el flujo sea demoable.
        var mock = new VerifikConductorResponse
        {
            Data = new VerifikConductorData
            {
                DocumentType = "CC",
                DocumentNumber = "00000000",
                FirstName = "JUAN CARLOS",
                LastName = "PEREZ GOMEZ",
                FullName = "JUAN CARLOS PEREZ GOMEZ",
                IdentityValidationAttempts = new VerifikConductorIdentityValidation { EstadoUsuario = "ACTIVO" },
            },
        };

        var a = VerifikConductorResultMapper.Map(mock);
        var b = VerifikConductorResultMapper.Map(mock);

        a.HydratedFields.Single(f => f.FieldKey == "person_full_name").ValueText
            .Should().Be("JUAN CARLOS PEREZ GOMEZ");
        b.HydratedFields.Single(f => f.FieldKey == "person_full_name").ValueText
            .Should().Be("JUAN CARLOS PEREZ GOMEZ");
        Check(a, "conductor").Status.Should().Be("ok");
    }

    [Fact]
    public void Provider_EsVerifikConductor()
    {
        var result = VerifikConductorResultMapper.Map(Response(fullName: "MATEO VERIFIK"));

        result.Provider.Should().Be("verifik_conductor");
    }
}
