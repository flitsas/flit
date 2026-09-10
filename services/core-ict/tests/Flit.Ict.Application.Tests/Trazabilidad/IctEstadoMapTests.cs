using Flit.Ict.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Trazabilidad;

/// <summary>
/// Fija el comportamiento de <see cref="IctEstado.Map"/> caso por caso.
/// </summary>
/// <remarks>
/// No prueba código nuevo a propósito: existe para que la proyección de estado en SQL
/// (<c>DbTrazabilidadBandejaRepository.EstadoSql</c>) no se quede atrás en silencio. Las dos están
/// duplicadas porque el filtro y los contadores de la bandeja tienen que resolverse en el motor,
/// y el compilador no puede ver que discrepan. Si alguien cambia el mapeo de dominio, estas pruebas
/// se ponen rojas y obligan a mirar la constante SQL; si cambia solo la SQL, la única defensa es
/// ejecutarla contra Postgres, así que ese contraste queda documentado en el propio repositorio.
/// </remarks>
public sealed class IctEstadoMapTests
{
    [Fact]
    public void Un_pretramite_ya_materializado_es_borrador_creado_pase_lo_que_pase()
    {
        // La existencia del trámite gana sobre cualquier estado interno del pipeline: es la única
        // rama que ignora process_status_id, y por eso va primera también en el CASE de SQL.
        foreach (var processStatusId in new short[] { 1, 2, 3, 4, 9 })
        {
            IctEstado.Map(processStatusId, hasProcedureInstance: true,
                    businessValidated: false, externalStarted: false)
                .Should().Be(IctEstado.BorradorCreado);
        }
    }

    [Theory]
    [InlineData((short)1, false, IctEstado.Recibido)]
    [InlineData((short)2, false, IctEstado.EnValidacionNegocio)]
    [InlineData((short)2, true, IctEstado.EnValidacionExterna)]
    [InlineData((short)3, false, IctEstado.Procesado)]
    [InlineData((short)4, false, IctEstado.ConNovedades)]
    public void El_estado_interno_se_proyecta_al_vocabulario_v2(
        short processStatusId, bool businessValidated, string esperado)
    {
        IctEstado.Map(processStatusId, hasProcedureInstance: false,
                businessValidated: businessValidated, externalStarted: false)
            .Should().Be(esperado);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)5)]
    [InlineData((short)99)]
    public void Cualquier_estado_interno_no_contemplado_cae_en_anulado(short processStatusId)
    {
        // El ELSE del CASE de SQL tiene que espejar esta rama: un valor inesperado no puede quedar
        // fuera de los siete contadores, o la tira dejaría de sumar el total de la bandeja.
        IctEstado.Map(processStatusId, hasProcedureInstance: false,
                businessValidated: false, externalStarted: false)
            .Should().Be(IctEstado.Anulado);
    }

    [Fact]
    public void La_validacion_de_negocio_solo_se_da_por_superada_con_el_valor_2()
    {
        // El SQL lo escribe como «business_validation <> 2»: cualquier otro valor sigue siendo
        // validación de negocio en curso, incluido el 1 (en proceso) y el 0 (sin empezar).
        IctEstado.Map(2, false, businessValidated: false, externalStarted: false)
            .Should().Be(IctEstado.EnValidacionNegocio);
        IctEstado.Map(2, false, businessValidated: true, externalStarted: false)
            .Should().Be(IctEstado.EnValidacionExterna);
    }
}
