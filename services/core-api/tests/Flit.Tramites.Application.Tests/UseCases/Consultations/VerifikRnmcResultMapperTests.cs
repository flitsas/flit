using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class VerifikRnmcResultMapperTests
{
    // Construye una respuesta con la forma del doc §3.1.
    private static VerifikRnmcResponse Response(
        List<VerifikRnmcCorrectiveMeasure>? measures = null,
        string? fullName = null) =>
        new()
        {
            Data = new VerifikRnmcData
            {
                CorrectiveMeasures = measures ?? [],
                Records = [],
                ArrayName = ["TITULAR", "MOCK"],
                DocumentType = "CC",
                DocumentNumber = "123456789",
                FullName = fullName,
            },
        };

    private static VerifikRnmcCorrectiveMeasure Measure(string? measure = "Multa General Tipo 2") =>
        new()
        {
            Attribution = "INSPECTOR DE POLICÍA",
            Address = "KR 12 26 25",
            Status = "IMPONER O RATIFICAR MEDIDA",
            Measure = measure,
            ReferredTo = "INSPECCION PERMANENTE TURNO 3",
        };

    private static ConsultationCheck Check(ConsultationResult r, string key) =>
        r.Checks.Single(c => c.Key == key);

    [Fact]
    public void SinMedidas_ProduceOkGreen()
    {
        var result = VerifikRnmcResultMapper.Map(Response());

        Check(result, "medidas_correctivas").Status.Should().Be("ok");
        result.Overall.Should().Be("green");
    }

    [Fact]
    public void ConMedidas_ProduceWarnYellow()
    {
        // RNMC no es bloqueante: la medida es informativa (warn/amarillo), no fail/rojo.
        var result = VerifikRnmcResultMapper.Map(Response(measures: [Measure()]));

        Check(result, "medidas_correctivas").Status.Should().Be("warn");
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public void VariasMedidas_MessageContieneConteo()
    {
        var result = VerifikRnmcResultMapper.Map(Response(measures: [Measure(), Measure("Multa Tipo 1")]));

        Check(result, "medidas_correctivas").Message.Should().Contain("2 medida(s)");
    }

    [Fact]
    public void NullData_DoesNotThrow_ProduceUnknown()
    {
        var result = VerifikRnmcResultMapper.Map(new VerifikRnmcResponse());

        result.Provider.Should().Be("verifik_rnmc");
        result.Checks.Should().HaveCount(1);
        Check(result, "medidas_correctivas").Status.Should().Be("unknown");
    }

    [Fact]
    public void FullName_SeHidrata()
    {
        var result = VerifikRnmcResultMapper.Map(Response(fullName: "MATEO VERIFIK"));

        result.HydratedFields.Should().Contain(f => f.FieldKey == "owner_full_name" && f.ValueText == "MATEO VERIFIK");
    }

    [Fact]
    public void SinFullName_HydratedFieldsVacio()
    {
        var result = VerifikRnmcResultMapper.Map(Response(fullName: null));

        result.HydratedFields.Should().BeEmpty();
    }

    [Fact]
    public void Provider_EsVerifikRnmc()
    {
        var result = VerifikRnmcResultMapper.Map(Response());

        result.Provider.Should().Be("verifik_rnmc");
    }
}
