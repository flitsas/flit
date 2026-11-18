using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// El tipo de servicio (<c>vehicle_service</c>) es obligatorio al radicar una matrícula inicial, y debe ser un
/// código del catálogo (<see cref="VehicleServiceTypeCode"/>). No afecta al traspaso (lo hidrata el RUNT como texto
/// libre) ni a los tipos de matrícula que no eligen servicio (cancelación).
/// </summary>
public sealed class SubmitGateTipoServicioTests
{
    private static readonly HashSet<string> Aprobadas = new(["comprador", "vendedor"], StringComparer.OrdinalIgnoreCase);

    private static ProcedureInstance Tramite(ProcedureType tipo, string? servicio)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = tipo,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000900",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (servicio is not null)
            instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_service", ValueText = servicio, Source = "user" });

        return instance;
    }

    private static IReadOnlyList<string> Evaluar(ProcedureInstance i) =>
        SubmitGate.Evaluate(i, Aprobadas, documentosCompletosOverride: true, improntaObligatoria: false);

    [Fact]
    public void MatriculaSinTipoServicio_BloqueaConCodigoClaro()
    {
        Evaluar(Tramite(ProcedureTypeFixture.Matricula, servicio: null))
            .Should().Contain(SubmitGate.TipoServicioRequerido);
        SubmitGate.TipoServicioRequerido.Should().Be("tipo_servicio_requerido");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("XYZ")]
    [InlineData("AEREO")]
    public void MatriculaConTipoServicioVacioOFueraDelCatalogo_Bloquea(string servicio)
    {
        Evaluar(Tramite(ProcedureTypeFixture.Matricula, servicio))
            .Should().Contain(SubmitGate.TipoServicioRequerido);
    }

    [Theory]
    [InlineData("PARTICULAR")]
    [InlineData("PUBLICO")]
    [InlineData("DIPLOMATICO")]
    [InlineData("OFICIAL")]
    [InlineData("ESPECIAL")]
    [InlineData("OTROS")]
    [InlineData("particular")]
    public void MatriculaConCodigoDelCatalogo_NoBloquea(string servicio)
    {
        Evaluar(Tramite(ProcedureTypeFixture.Matricula, servicio))
            .Should().NotContain(SubmitGate.TipoServicioRequerido);
    }

    [Fact]
    public void MatriculaLeasing_TambienLoExige()
    {
        Evaluar(Tramite(ProcedureTypeFixture.MatriculaLeasing, servicio: null))
            .Should().Contain(SubmitGate.TipoServicioRequerido);
    }

    [Fact]
    public void Traspaso_NoLoExige_LoHidrataElRunt()
    {
        Evaluar(Tramite(ProcedureTypeFixture.Traspaso, servicio: null))
            .Should().NotContain(SubmitGate.TipoServicioRequerido);
        Evaluar(Tramite(ProcedureTypeFixture.Traspaso, "SERVICIO PUBLICO ESPECIAL"))
            .Should().NotContain(SubmitGate.TipoServicioRequerido);
    }

    [Fact]
    public void CancelacionDeMatricula_AunqueSeaDeLaFamiliaMatriculas_NoLoExige()
    {
        Evaluar(Tramite(ProcedureTypeFixture.Cancelacion, servicio: null))
            .Should().NotContain(SubmitGate.TipoServicioRequerido);
    }

    [Fact]
    public void OtrosTramites_NoLoExigen()
    {
        Evaluar(Tramite(ProcedureTypeFixture.CambioColor, servicio: null))
            .Should().NotContain(SubmitGate.TipoServicioRequerido);
    }
}
