using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ProcedureFieldValuesTests
{
    [Fact]
    public void EnsurePlaca_FieldPlateGanaSobreColumna()
    {
        var instance = Base();
        instance.Plate = "COL999";
        instance.FieldValues.Add(Fv(instance, "plate", "ABC123"));
        var fv = ProcedureFieldValues.ToDictionary(instance);

        ProcedureFieldValues.EnsurePlaca(fv, instance);

        fv["plate"].Should().Be("ABC123");
    }

    [Fact]
    public void EnsurePlaca_SinField_UsaColumnaDenormalizada()
    {
        var instance = Base();
        instance.Plate = " XYZ789 ";
        var fv = ProcedureFieldValues.ToDictionary(instance);

        ProcedureFieldValues.EnsurePlaca(fv, instance);

        fv["plate"].Should().Be("XYZ789");
    }

    [Fact]
    public void EnsurePlaca_ClavePlaca_SeNormalizaAPlate()
    {
        var instance = Base();
        instance.FieldValues.Add(Fv(instance, "placa", "MUE123"));
        var fv = ProcedureFieldValues.ToDictionary(instance);

        ProcedureFieldValues.EnsurePlaca(fv, instance);

        fv["plate"].Should().Be("MUE123");
    }

    [Fact]
    public void EnsurePlaca_SinNingunValor_NoInventaTexto()
    {
        var instance = Base();
        var fv = ProcedureFieldValues.ToDictionary(instance);

        ProcedureFieldValues.EnsurePlaca(fv, instance);

        fv.ContainsKey("plate").Should().BeFalse();
    }

    private static ProcedureInstance Base() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ReferenceNumber = "TRM-1",
        ProcedureTypeId = Guid.NewGuid(),
    };

    private static ProcedureInstanceFieldValue Fv(ProcedureInstance instance, string key, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = key,
            ValueText = value,
        };
}
