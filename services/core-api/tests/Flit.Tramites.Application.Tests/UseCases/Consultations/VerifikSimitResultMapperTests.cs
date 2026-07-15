using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class VerifikSimitResultMapperTests
{
    // Construye una respuesta con la estructura doblemente anidada del doc §3.5.
    private static VerifikSimitResponse Response(
        List<VerifikSimitMulta>? multas = null,
        List<VerifikSimitAcuerdoPago>? acuerdos = null) =>
        new()
        {
            Value = new VerifikSimitValueOuter
            {
                Value = new VerifikSimitValueInner
                {
                    Data = new VerifikSimitData
                    {
                        Multas = multas ?? [],
                        Cursos = [],
                        AcuerdosPago = acuerdos ?? [],
                        TotalMultasPagar = 0,
                        CantMultasPagar = 0,
                    },
                },
            },
        };

    private static VerifikSimitMulta MultaPendiente(decimal valorPagar = 344_730m) =>
        new()
        {
            Placa = "RDL805",
            Valor = valorPagar,
            ValorPagar = valorPagar,
            EstadoComparendo = "Pendiente",
            NumeroComparendo = "25612001000012662173",
            Infracciones = [new VerifikSimitInfraccion { CodigoInfraccion = "C35", ValorInfraccion = valorPagar }],
        };

    private static VerifikSimitMulta MultaPagada() =>
        new() { Placa = "ABC123", EstadoComparendo = "Pagado", ValorPagar = 0 };

    // Estado real del SIMIT (observado en la fuente interna): multa en curso pedagógico, sin deuda.
    private static VerifikSimitMulta MultaPendienteCurso() =>
        new() { Placa = "ABC123", EstadoComparendo = "Pendiente Curso", ValorPagar = 0 };

    private static VerifikSimitAcuerdoPago AcuerdoActivo(decimal pendiente = 837_716m) =>
        new()
        {
            Resolucion = "324",
            Estado = "Acuerdo de pago",
            ValorAcuerdo = pendiente,
            Pendiente = pendiente,
            TotalPagar = pendiente,
        };

    private static ConsultationCheck Check(ConsultationResult r, string key) =>
        r.Checks.Single(c => c.Key == key);

    [Fact]
    public void SinMultas_ProduceOkGreen()
    {
        var result = VerifikSimitResultMapper.Map(Response());

        Check(result, "multas").Status.Should().Be("ok");
        result.Overall.Should().Be("green");
    }

    [Fact]
    public void MultaPendiente_ProduceWarnYellow()
    {
        // FEATURE 05 (AC5) — tener comparendos ADVIERTE, ya no bloquea la creación del trámite:
        // warn/yellow en vez de fail/red. El gate de RADICACIÓN al OT no cambia: se deriva de la
        // clave del check (ver WizardStateQuery.SimitOf), no de esta severidad.
        var result = VerifikSimitResultMapper.Map(Response(multas: [MultaPendiente()]));

        Check(result, "multas").Status.Should().Be("warn");
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public void MultaPagada_NoContaComoPendiente_OkGreen()
    {
        // Solo la pendiente cuenta como comparendo.
        var result = VerifikSimitResultMapper.Map(Response(multas: [MultaPagada()]));

        Check(result, "multas").Status.Should().Be("ok");
        result.Overall.Should().Be("green");
    }

    [Fact]
    public void MultaPendienteCurso_NoCuentaComoPendiente_OkGreen()
    {
        // "Pendiente Curso" (estado real del SIMIT, sin deuda: valorPagar = 0) NO es un comparendo
        // pendiente. La coincidencia del estado es exacta y deliberada: ampliarla a "Pendiente*"
        // haría que más trámites se bloqueen en la RADICACIÓN, que cuelga de este check.
        var result = VerifikSimitResultMapper.Map(Response(multas: [MultaPendienteCurso()]));

        Check(result, "multas").Status.Should().Be("ok");
        result.Overall.Should().Be("green");
    }

    [Fact]
    public void AcuerdoPago_ProduceWarnYellow()
    {
        var result = VerifikSimitResultMapper.Map(Response(acuerdos: [AcuerdoActivo()]));

        Check(result, "acuerdos_pago").Status.Should().Be("warn");
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public void MultaPendienteYAcuerdo_OverallYellow()
    {
        // FEATURE 05 — ninguno de los dos hallazgos pinta rojo: el SIMIT ya no puede, por sí solo,
        // dejar el pre-vuelo en rojo. Solo un error de transporte lo consigue.
        var result = VerifikSimitResultMapper.Map(
            Response(multas: [MultaPendiente()], acuerdos: [AcuerdoActivo()]));

        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public void MultasYAcuerdos_UsanLasClavesDelContratoCompartido()
    {
        // Amarre del contrato con el gate de radicación: la clave de multas debe terminar en
        // "_multas" al prefijarse por actor, y la de acuerdos NO debe terminar así (si lo hiciera,
        // un acuerdo de pago dispararía un simit_multas falso en WizardStateQuery.SimitOf).
        var result = VerifikSimitResultMapper.Map(
            Response(multas: [MultaPendiente()], acuerdos: [AcuerdoActivo()]));

        result.Checks.Select(c => c.Key).Should().BeEquivalentTo([
            FinesCheckFactory.KeyMultas,
            FinesCheckFactory.KeyAcuerdos,
        ]);
        $"simit_comprador_{FinesCheckFactory.KeyAcuerdos}"
            .Should().NotEndWith($"_{FinesCheckFactory.KeyMultas}");
    }

    [Fact]
    public void NullData_DoesNotThrow_ProducesUnknown()
    {
        var result = VerifikSimitResultMapper.Map(new VerifikSimitResponse());

        result.Provider.Should().Be("verifik_simit");
        result.Checks.Should().HaveCount(2);
        result.Checks.Should().AllSatisfy(c => c.Status.Should().Be("unknown"));
    }

    [Fact]
    public void MultaMessage_ContieneCantidadYValor()
    {
        var result = VerifikSimitResultMapper.Map(Response(multas: [MultaPendiente(200_000m)]));

        Check(result, "multas").Message.Should().Contain("1 multa(s)");
        Check(result, "multas").Message.Should().Contain("200");
    }

    [Fact]
    public void DoubleNesting_ValueValueData_EsRequeridoParaAccederDatos()
    {
        // Verifica que el mapper consume exactamente value.value.data (doble envoltorio).
        var sinDobleEnvoltorio = new VerifikSimitResponse
        {
            Value = new VerifikSimitValueOuter
            {
                Value = null, // falta el segundo .value
            },
        };
        var result = VerifikSimitResultMapper.Map(sinDobleEnvoltorio);

        result.Checks.Should().AllSatisfy(c => c.Status.Should().Be("unknown"));
    }
}
