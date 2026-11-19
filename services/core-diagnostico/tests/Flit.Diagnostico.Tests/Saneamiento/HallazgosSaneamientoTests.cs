using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Saneamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Generación de renglones por fuente (plan §3.3, S11, brief C3/C4; HU #14576 AC14, AC15 y AC16): pura, sobre
/// <see cref="HechosDictamen"/>, sin modo ni tipo de trámite y sin llamar al evaluador. La prueba cruzada R7 contrasta, sobre una
/// matriz de hechos, los renglones con las reglas que dispara <see cref="EvaluadorDictamen"/> con esos mismos hechos.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~HallazgosSaneamientoTests
/// </code>
/// </example>
public sealed class HallazgosSaneamientoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 14);

    private static readonly HechosSimit Limpio = new(0, true, false, false, []);

    private static HechosRuntVehiculo Runt(
        bool leasing = false, bool docLocatario = false, string soat = "VIGENTE", string rtm = "VIGENTE",
        ClaseVehiculoDictamen clase = ClaseVehiculoDictamen.Carro, DateOnly? matricula = null) =>
        new("ACTIVO", matricula ?? new DateOnly(2015, 1, 1), leasing, docLocatario, [soat], [rtm], 0, false, 0, false, 0, clase, ServicioVehiculoDictamen.Particular);

    private static HechosDictamen Hechos(
        HechosRuntVehiculo? runt = null, HechosSimit? vehiculo = null, HechosSimit? propietario = null, HechosSimit? locatario = null,
        long? impuestoMonto = null, bool impuestoDeuda = false, string tipo = "TRASPASO_STANDARD") =>
        new(tipo, Hoy, EstadoFuenteDictamen.Disponible, EstadoFuenteDictamen.Disponible, EstadoFuenteDictamen.Disponible, EstadoFuenteDictamen.Disponible,
            runt ?? Runt(), vehiculo ?? Limpio, propietario ?? Limpio, locatario, null, null, impuestoMonto, impuestoDeuda);

    [Fact]
    public void Sin_hallazgos_no_hay_renglones() => HallazgosSaneamiento.Generar(Hechos()).Should().BeEmpty();

    public static TheoryData<string, FuenteSaneamiento, HallazgoSaneamiento> Ejemplos => new()
    {
        { "vehiculo_comparendos", FuenteSaneamiento.SimitVehiculo, HallazgoSaneamiento.Deuda },
        { "vehiculo_acuerdo", FuenteSaneamiento.SimitVehiculo, HallazgoSaneamiento.Deuda },
        { "propietario_sin_paz_y_salvo", FuenteSaneamiento.SimitPropietario, HallazgoSaneamiento.Deuda },
        { "locatario_deuda", FuenteSaneamiento.SimitLocatario, HallazgoSaneamiento.Deuda },
        { "locatario_sin_documento", FuenteSaneamiento.SimitLocatario, HallazgoSaneamiento.SinVerificar },
        { "impuesto_tiene_deuda", FuenteSaneamiento.Impuesto, HallazgoSaneamiento.Deuda },
        { "impuesto_monto", FuenteSaneamiento.Impuesto, HallazgoSaneamiento.Deuda },
        { "soat", FuenteSaneamiento.Soat, HallazgoSaneamiento.NoVigente },
        { "soat_maquinaria", FuenteSaneamiento.Soat, HallazgoSaneamiento.NoVigente },
        { "rtm", FuenteSaneamiento.Rtm, HallazgoSaneamiento.NoVigente },
    };

    // AC14 — un renglón por fuente con hallazgo, con el código de la tabla de §3.3.
    [Theory]
    [MemberData(nameof(Ejemplos))]
    public void HU14576_AC14_un_renglon_por_fuente_con_hallazgo(string caso, FuenteSaneamiento fuente, HallazgoSaneamiento hallazgo) =>
        HallazgosSaneamiento.Generar(Caso(caso)).Should().Equal(new HallazgoFuente(fuente, hallazgo));

    private static HechosDictamen Caso(string caso) => caso switch
    {
        "vehiculo_comparendos" => Hechos(vehiculo: Limpio with { CantidadComparendos = 2 }),
        "vehiculo_acuerdo" => Hechos(vehiculo: Limpio with { Acuerdos = [new AcuerdoPago(150_000)] }),
        "propietario_sin_paz_y_salvo" => Hechos(propietario: Limpio with { PazYSalvo = false }),
        "locatario_deuda" => Hechos(runt: Runt(leasing: true, docLocatario: true), locatario: Limpio with { CantidadComparendos = 1 }),
        "locatario_sin_documento" => Hechos(runt: Runt(leasing: true, docLocatario: false)),
        "impuesto_tiene_deuda" => Hechos(impuestoDeuda: true),
        "impuesto_monto" => Hechos(impuestoMonto: 320_000),
        "soat" => Hechos(runt: Runt(soat: "NO VIGENTE")),
        "soat_maquinaria" => Hechos(runt: Runt(soat: "NO VIGENTE", clase: ClaseVehiculoDictamen.Maquinaria, rtm: "NO VIGENTE")),
        "rtm" => Hechos(runt: Runt(rtm: "NO VIGENTE")),
        _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "caso desconocido"),
    };

    // AC14 / C4 — la licencia suspendida o cancelada no es deuda de saneamiento; el remolque no exige SOAT; RTM aún no exigible.
    [Fact]
    public void HU14576_AC14_licencia_remolque_y_rtm_no_exigible_no_generan_renglon()
    {
        HallazgosSaneamiento.Generar(Hechos(vehiculo: Limpio with { Suspendida = true, Cancelada = true })).Should().BeEmpty();
        HallazgosSaneamiento.Generar(Hechos(runt: Runt(soat: "NO VIGENTE", clase: ClaseVehiculoDictamen.Remolque))).Should().BeEmpty();
        HallazgosSaneamiento.Generar(Hechos(runt: Runt(rtm: "NO VIGENTE", matricula: Hoy.AddYears(-1)))).Should().BeEmpty();
    }

    // AC14 — en leasing el SIMIT del propietario no se evalúa (igual que el evaluador).
    [Fact]
    public void HU14576_AC14_en_leasing_no_se_evalua_el_SIMIT_del_propietario() =>
        HallazgosSaneamiento.Generar(Hechos(runt: Runt(leasing: true, docLocatario: true), propietario: Limpio with { PazYSalvo = false }, locatario: Limpio))
            .Should().BeEmpty();

    // AC15 — modo Simple: sin dictamen, con un tipo fuera del catálogo (el evaluador lanzaría) y con fuentes en N/A.
    [Fact]
    public void HU14576_AC15_modo_Simple_sin_tipo_de_traspaso_solo_el_impuesto()
    {
        var simple = Hechos(impuestoDeuda: true, tipo: string.Empty) with
        {
            EstadoSimitPropietario = EstadoFuenteDictamen.NoAplica,
            SimitPropietario = null,
        };

        HallazgosSaneamiento.Generar(simple).Should().Equal(new HallazgoFuente(FuenteSaneamiento.Impuesto, HallazgoSaneamiento.Deuda));
        var evaluar = () => EvaluadorDictamen.Evaluar(simple);
        evaluar.Should().Throw<ArgumentOutOfRangeException>("el evaluador solo cubre TRASPASO; la generación no pasa por él");
        HallazgosSaneamiento.Generar(simple with { TipoTramiteCodigo = "TRASPASO_STANDARD" }).Should().Equal(HallazgosSaneamiento.Generar(simple),
            "no depende del tipo de trámite");
    }

    // AC15 — una fuente en N/A u error no genera renglón, aunque traiga lectura.
    [Fact]
    public void HU14576_AC15_fuente_no_disponible_no_genera_renglon()
    {
        var hechos = Hechos(vehiculo: Limpio with { CantidadComparendos = 3 }) with { EstadoSimitVehiculo = EstadoFuenteDictamen.NoAplica };
        HallazgosSaneamiento.Generar(hechos).Should().BeEmpty();
        HallazgosSaneamiento.Generar(Hechos(runt: Runt(soat: "NO VIGENTE")) with { EstadoRunt = EstadoFuenteDictamen.Error }).Should().BeEmpty();
    }

    // AC16 (R7) — prueba cruzada: sobre la matriz de hechos, los renglones son exactamente las fuentes de las reglas SIMIT_*,
    // SIMIT_ACUERDO_PAGO, SIMIT_SIN_VERIFICAR (locatario), IMPUESTO, SOAT_NO_VIGENTE/SOAT_MAQUINARIA y RTM_NO_VIGENTE.
    [Fact]
    public void HU14576_AC16_R7_mismos_predicados_que_el_evaluador()
    {
        var casos = 0;
        foreach (var comparendosVehiculo in new[] { 0, 2 })
            foreach (var acuerdoPropietario in new[] { 0L, 90_000L })
                foreach (var pazPropietario in new bool?[] { true, false, null })
                    foreach (var (leasing, doc) in new[] { (false, false), (true, true), (true, false) })
                        foreach (var locatarioMultas in new[] { 0, 1 })
                            foreach (var (impuestoDeuda, monto) in new[] { (false, (long?)null), (true, (long?)null), (false, (long?)50_000), (false, (long?)0) })
                                foreach (var soat in new[] { "VIGENTE", "NO VIGENTE" })
                                    foreach (var rtm in new[] { "VIGENTE", "VENCIDA" })
                                        foreach (var clase in new[] { ClaseVehiculoDictamen.Carro, ClaseVehiculoDictamen.Moto, ClaseVehiculoDictamen.Remolque, ClaseVehiculoDictamen.Maquinaria })
                                        {
                                            var hechos = Hechos(
                                                runt: Runt(leasing, doc, soat, rtm, clase),
                                                vehiculo: Limpio with { CantidadComparendos = comparendosVehiculo },
                                                propietario: Limpio with { PazYSalvo = pazPropietario, Acuerdos = acuerdoPropietario > 0 ? [new AcuerdoPago(acuerdoPropietario)] : [] },
                                                locatario: doc ? Limpio with { CantidadComparendos = locatarioMultas } : null,
                                                impuestoMonto: monto,
                                                impuestoDeuda: impuestoDeuda);
                                            HallazgosSaneamiento.Generar(hechos).Should().BeEquivalentTo(DelEvaluador(hechos), $"caso {casos}");
                                            casos++;
                                        }

        casos.Should().Be(2 * 2 * 3 * 3 * 2 * 4 * 2 * 2 * 4);
    }

    private static List<HallazgoFuente> DelEvaluador(HechosDictamen hechos)
    {
        var esperados = new List<HallazgoFuente>();
        foreach (var regla in EvaluadorDictamen.Evaluar(hechos).Reglas)
        {
            HallazgoFuente? hallazgo = regla.Codigo switch
            {
                "SIMIT_VEHICULO" or "SIMIT_PROPIETARIO" or "SIMIT_LOCATARIO" or "SIMIT_ACUERDO_PAGO" => new(Fuente(regla.Fuente), HallazgoSaneamiento.Deuda),
                "SIMIT_SIN_VERIFICAR" when regla.Fuente == "SIMIT_LOCATARIO" => new(FuenteSaneamiento.SimitLocatario, HallazgoSaneamiento.SinVerificar),
                "IMPUESTO" => new(FuenteSaneamiento.Impuesto, HallazgoSaneamiento.Deuda),
                "SOAT_NO_VIGENTE" or "SOAT_MAQUINARIA" => new(FuenteSaneamiento.Soat, HallazgoSaneamiento.NoVigente),
                "RTM_NO_VIGENTE" => new(FuenteSaneamiento.Rtm, HallazgoSaneamiento.NoVigente),
                _ => null,
            };
            if (hallazgo is not null && !esperados.Contains(hallazgo))
                esperados.Add(hallazgo);
        }

        return esperados;
    }

    private static FuenteSaneamiento Fuente(string fuente) => fuente switch
    {
        "SIMIT_VEHICULO" => FuenteSaneamiento.SimitVehiculo,
        "SIMIT_PROPIETARIO" => FuenteSaneamiento.SimitPropietario,
        "SIMIT_LOCATARIO" => FuenteSaneamiento.SimitLocatario,
        _ => throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "fuente SIMIT desconocida"),
    };
}
