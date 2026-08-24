using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0050 — la portada del expediente consolidado rotula el NOMBRE del tipo.
/// <para>Antes humanizaba <c>modalidad_entrada</c>, que solo tenía dos valores: la portada de un
/// blindaje o de un levantamiento de prenda decía "Matricula inicial". El expediente afirmaba ser un
/// trámite que no era, y es la primera hoja que lee el organismo de tránsito.</para>
/// </summary>
public sealed class PortadaTipoTramiteTests
{
    private static ProcedureInstance Instancia(string code, string name, string family) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000123",
        ProcedureType = new ProcedureType { Code = code, Name = name, Family = family },
    };

    [Theory]
    [InlineData("MATRICULA_NUEVA", "Matrícula inicial", ProcedureFamilyCodes.Matriculas)]
    [InlineData("TRASPASO_STANDARD", "Traspaso", ProcedureFamilyCodes.Traspaso)]
    [InlineData("BLINDAJE", "Blindaje", ProcedureFamilyCodes.Otros)]
    [InlineData("LEVANTAMIENTO_PRENDA", "Levantamiento de prenda", ProcedureFamilyCodes.Otros)]
    [InlineData("DUPLICADO_TARJETA", "Duplicado de tarjeta", ProcedureFamilyCodes.Otros)]
    public void LaPortadaNombraElTipoReal(string code, string name, string family)
    {
        var info = ExpedienteCoverInfoBuilder.FromInstance(Instancia(code, name, family), null);

        info.TipoTramite.Should().Be(name);
    }

    [Fact]
    public void DosTramitesDeLaMismaFamiliaSeDistinguenEnLaPortada()
    {
        // Con la modalidad ambos decían lo mismo: era imposible saber de qué trámite era el expediente.
        var blindaje = ExpedienteCoverInfoBuilder.FromInstance(
            Instancia("BLINDAJE", "Blindaje", ProcedureFamilyCodes.Otros), null);
        var duplicado = ExpedienteCoverInfoBuilder.FromInstance(
            Instancia("DUPLICADO_PLACA", "Duplicado de placa", ProcedureFamilyCodes.Otros), null);

        blindaje.TipoTramite.Should().NotBe(duplicado.TipoTramite);
    }

    [Fact]
    public void ConservaElNumeroDeRadicadoYLaPlaca()
    {
        // El resto de la portada no cambia con ADR-0050.
        var info = ExpedienteCoverInfoBuilder.FromInstance(
            Instancia("BLINDAJE", "Blindaje", ProcedureFamilyCodes.Otros), null);

        info.CodigoTramite.Should().Be("TRM-2026-000123");
    }
}
