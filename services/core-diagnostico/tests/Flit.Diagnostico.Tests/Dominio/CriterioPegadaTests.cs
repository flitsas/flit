using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// Ajuste 2026-11-11 Punto 3 (decisión 3.2) — criterio «pegada»: una celda en ERROR, o en PENDIENTE/EN_CURSO con su último
/// cambio (<c>actualizado_en</c>) hace MÁS del umbral (por defecto 10 min). COMPLETADO y NA nunca. <see cref="CriterioPegada.Reencolables"/>
/// aplica el orden de la RUNT: si la RUNT del vehículo no está COMPLETADO, solo ella se reencola (las demás esperan su cierre).
/// </summary>
/// <example>
/// <code>
/// CriterioPegada.EstaPegada(EstadoCelda.EnCurso, actualizadoEn: ahora.AddMinutes(-11), ahora, CriterioPegada.UmbralPorDefecto); // true
/// </code>
/// </example>
public sealed class CriterioPegadaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Umbral = CriterioPegada.UmbralPorDefecto;

    [Fact]
    public void El_umbral_por_defecto_es_10_minutos() => CriterioPegada.UmbralPorDefecto.Should().Be(TimeSpan.FromMinutes(10));

    // Bordes 9 / 10 / 11 min para PENDIENTE y EN_CURSO: solo «más de» 10 min cuenta.
    [Theory]
    [InlineData(EstadoCelda.Pendiente, 9, false)]
    [InlineData(EstadoCelda.Pendiente, 10, false)]
    [InlineData(EstadoCelda.Pendiente, 11, true)]
    [InlineData(EstadoCelda.EnCurso, 9, false)]
    [InlineData(EstadoCelda.EnCurso, 10, false)]
    [InlineData(EstadoCelda.EnCurso, 11, true)]
    public void Pendiente_o_en_curso_cuenta_solo_con_mas_de_10_minutos(EstadoCelda estado, int minutos, bool pegada) =>
        CriterioPegada.EstaPegada(estado, Ahora.AddMinutes(-minutos), Ahora, Umbral).Should().Be(pegada);

    // ERROR siempre cuenta (recién o hace horas); COMPLETADO y NA nunca, por viejas que sean.
    [Theory]
    [InlineData(EstadoCelda.Error, 0, true)]
    [InlineData(EstadoCelda.Error, 600, true)]
    [InlineData(EstadoCelda.Completado, 600, false)]
    [InlineData(EstadoCelda.Na, 600, false)]
    public void Error_siempre_y_completado_o_na_nunca(EstadoCelda estado, int minutos, bool pegada) =>
        CriterioPegada.EstaPegada(estado, Ahora.AddMinutes(-minutos), Ahora, Umbral).Should().Be(pegada);

    [Fact]
    public void El_umbral_es_configurable() =>
        CriterioPegada.EstaPegada(EstadoCelda.EnCurso, Ahora.AddMinutes(-6), Ahora, TimeSpan.FromMinutes(5)).Should().BeTrue();

    // Reencolables: RUNT completada ⇒ todas las pegadas del vehículo; las completas y las recientes, no.
    [Fact]
    public void Con_la_RUNT_completa_se_reencolan_todas_las_pegadas_del_vehiculo()
    {
        var v = Guid.NewGuid();
        var runt = Celda(v, Fuente.Runt, EstadoCelda.Completado, 60);
        var simit = Celda(v, Fuente.SimitVehiculo, EstadoCelda.Error, 1);
        var avaluo = Celda(v, Fuente.Avaluo, EstadoCelda.EnCurso, 11);
        var impuesto = Celda(v, Fuente.ImpuestoDepartamental, EstadoCelda.EnCurso, 9);

        CriterioPegada.Reencolables([runt, simit, avaluo, impuesto], Ahora, Umbral).Select(c => c.Id)
            .Should().BeEquivalentTo([simit.Id, avaluo.Id]);
    }

    // Reencolables: RUNT pegada ⇒ solo la RUNT (las demás esperan su cierre, que las libera o las bloquea).
    [Fact]
    public void Con_la_RUNT_pegada_solo_se_reencola_la_RUNT()
    {
        var v = Guid.NewGuid();
        var runt = Celda(v, Fuente.Runt, EstadoCelda.Error, 30);
        var bloqueada = Celda(v, Fuente.SimitVehiculo, EstadoCelda.Error, 30);
        var diferida = Celda(v, Fuente.Avaluo, EstadoCelda.Pendiente, 30);

        CriterioPegada.Reencolables([runt, bloqueada, diferida], Ahora, Umbral).Select(c => c.Id).Should().Equal(runt.Id);
    }

    // Reencolables: RUNT en curso reciente ⇒ nada, aunque las demás lleven más de 10 min diferidas (esperan a la RUNT).
    [Fact]
    public void Con_la_RUNT_en_curso_reciente_no_se_reencola_nada()
    {
        var v = Guid.NewGuid();
        CriterioPegada.Reencolables(
            [Celda(v, Fuente.Runt, EstadoCelda.EnCurso, 2), Celda(v, Fuente.SimitVehiculo, EstadoCelda.Pendiente, 30)], Ahora, Umbral)
            .Should().BeEmpty();
    }

    // Reencolables: cada vehículo se evalúa con SU RUNT.
    [Fact]
    public void Cada_vehiculo_se_evalua_con_su_propia_RUNT()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var simitA = Celda(a, Fuente.SimitVehiculo, EstadoCelda.Error, 1);
        var runtB = Celda(b, Fuente.Runt, EstadoCelda.EnCurso, 15);

        CriterioPegada.Reencolables(
            [Celda(a, Fuente.Runt, EstadoCelda.Completado, 50), simitA, runtB, Celda(b, Fuente.SimitVehiculo, EstadoCelda.Pendiente, 15)], Ahora, Umbral)
            .Select(c => c.Id).Should().BeEquivalentTo([simitA.Id, runtB.Id]);
    }

    private static CeldaParaReintento Celda(Guid vehiculo, Fuente fuente, EstadoCelda estado, int minutos) =>
        new(Guid.NewGuid(), vehiculo, fuente, estado, Ahora.AddMinutes(-minutos));
}
