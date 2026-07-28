using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

// Mapper del conductor: confirma que TieneMultas='SI'/'NO' hidrata person_has_pending_fines,
// el campo del que depende el paz y salvo (DRIVER) en la fachada gRPC IctConsultationService.
public sealed class ConductorMapperTests
{
    private static VerifikConductorResponse Resp(string tieneMultas) => new()
    {
        Data = new VerifikConductorData
        {
            DocumentType = "CC",
            DocumentNumber = "99991234",
            FullName = "JUAN PEREZ",
            FirstName = "JUAN",
            LastName = "PEREZ",
            DriverStatus = "ACTIVO",
            CitizenStatus = "ACTIVA",
            Licenses = [new VerifikConductorLicense { Status = "ACTIVA" }],
            Infractions = new VerifikConductorInfractions { TieneMultas = tieneMultas, NroPazYSalvo = "" },
        },
    };

    [Fact]
    public void Multas_SI_hidrata_pending_fines_true()
    {
        var r = VerifikConductorResultMapper.Map(Resp("SI"));
        r.HydratedFields.Should().Contain(f => f.FieldKey == "person_has_pending_fines" && f.ValueText == "true");
    }

    [Fact]
    public void Multas_NO_hidrata_pending_fines_false()
    {
        var r = VerifikConductorResultMapper.Map(Resp("NO"));
        r.HydratedFields.Should().Contain(f => f.FieldKey == "person_has_pending_fines" && f.ValueText == "false");
    }
}
