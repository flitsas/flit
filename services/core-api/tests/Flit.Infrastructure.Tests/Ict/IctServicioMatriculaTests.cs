using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// El tipo de servicio que ICT declara en una MATRÍCULA (<c>vehicle_service</c>) prevalece sobre el RUNT;
/// en traspaso (y cualquier tipo no matrícula) no se preserva nada: lo hidrata el RUNT.
/// <para>Uso de ejemplo: <c>IctOrchestrationService.ServicioMatriculaIct(request)</c> → <c>"PUBLICO"</c> o <c>null</c>.</para>
/// </summary>
public sealed class IctServicioMatriculaTests
{
    private static CreateDraftFromIctRequest Req(string tipo, string? servicio)
    {
        var r = new CreateDraftFromIctRequest { ProcedureTypeCode = tipo };
        r.FieldValues.Add(new FieldValue { FieldKey = "vin", ValueText = "VIN1" });
        if (servicio is not null)
        {
            r.FieldValues.Add(new FieldValue { FieldKey = "vehicle_service", ValueText = servicio });
        }

        return r;
    }

    [Theory]
    [InlineData("MATRICULA_NUEVA", "PUBLICO", "PUBLICO")]
    [InlineData("MATRICULA_LEASING", " PARTICULAR ", "PARTICULAR")]
    public void Matricula_ConServicio_LoPreserva(string tipo, string servicio, string esperado) =>
        IctOrchestrationService.ServicioMatriculaIct(Req(tipo, servicio)).Should().Be(esperado);

    [Theory]
    [InlineData("TRASPASO_STANDARD", "PUBLICO")]
    [InlineData("MATRICULA_NUEVA", null)]
    [InlineData("MATRICULA_NUEVA", "  ")]
    [InlineData("CANCELACION_MATRICULA", "PUBLICO")]
    public void NoMatriculaOSinServicio_NoPreservaNada(string tipo, string? servicio) =>
        IctOrchestrationService.ServicioMatriculaIct(Req(tipo, servicio)).Should().BeNull();
}
