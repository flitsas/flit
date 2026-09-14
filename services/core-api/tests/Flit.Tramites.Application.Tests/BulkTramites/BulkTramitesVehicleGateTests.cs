using Flit.Tramites.Application.BulkTramites.Processing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>
/// Regla del PO: vehículo que falla ⇒ NO se crea el trámite. El handler del paso 1 responde SIN
/// error cuando el vehículo no existe o el proveedor se cae —el rojo viaja en los checks—, y la
/// primera versión del lote creaba trámites de vehículos inexistentes. Es la misma regla que
/// <c>hardBlocked</c> en TramiteWizard.tsx.
/// </summary>
public sealed class BulkTramitesVehicleGateTests
{
    private static PreflightCheckDto Check(string key, string status) =>
        new(key, key, status, "kyverum_runt", null);

    [Fact]
    public void VehiculoNoEncontradoEnElRunt_CortaLaFila()
    {
        var codigo = BulkTramitesVehicleGate.Evaluate(
            [Check("vehiculo", "fail"), Check("soat", "unknown")]);

        codigo.Should().Be(BulkTramitesVehicleGate.VehiculoNoEncontrado);
    }

    [Fact]
    public void ProveedorCaido_CortaLaFila_YSeDistingueDelNoEncontrado()
    {
        // Con la consulta caída, «no encontrado» no significa nada: el usuario debe reintentar,
        // no corregir la placa. Por eso el error gana aunque también venga el fail.
        var codigo = BulkTramitesVehicleGate.Evaluate(
            [Check("vehiculo", "fail"), Check("provider", "error")]);

        codigo.Should().Be(BulkTramitesVehicleGate.ConsultaVehiculoFallida);
    }

    [Fact]
    public void SemaforoEnRojoPorOtrosChecks_NoCortaLaFila()
    {
        // Tecnomecánica vencida o SOAT sin dato ponen el semáforo en rojo, pero el vehículo EXISTE y
        // el wizard deja crear el trámite: la carga masiva no puede ser más estricta que el wizard.
        var codigo = BulkTramitesVehicleGate.Evaluate(
            [Check("tecnomecanica", "fail"), Check("soat", "unknown")]);

        codigo.Should().BeNull();
    }

    [Fact]
    public void SinChecks_NoCortaLaFila()
    {
        BulkTramitesVehicleGate.Evaluate(null).Should().BeNull();
        BulkTramitesVehicleGate.Evaluate([]).Should().BeNull();
    }
}
