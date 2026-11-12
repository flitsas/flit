using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §5.3 punto 4, ADR-0084 «un único punto de cierre») — <see cref="CierreItemDerivacion"/> es el
/// único que cierra un ítem: CREADO fija el trámite en el ítem y en el vehículo (una sola vez, R-16); OMITIDO guarda el código
/// del rechazo; FALLIDO, TRAMITES_NO_DISPONIBLE. Los contadores de la operación suben y la operación pasa a TERMINADA al
/// completar su total. Sin base: solo las entidades.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CierreItemDerivacionTests
/// </code>
/// </example>
public sealed class CierreItemDerivacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 16, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tramite = Guid.Parse("0199f3a1-7777-7000-8000-000000000077");

    [Fact]
    public void Creado_fija_el_trámite_en_el_ítem_y_en_el_vehículo_y_termina_la_operación()
    {
        var (op, item, vehiculo) = Escenario();

        CierreItemDerivacion.Cerrar(op, item, vehiculo, new ResultadoBorrador.Creado(Tramite, "TR-000123", YaExistia: false), Ahora);

        item.Estado.Should().Be(EstadoItemDerivacion.Creado);
        item.TramiteId.Should().Be(Tramite);
        item.TramiteNumero.Should().Be("TR-000123");
        item.MotivoCodigo.Should().BeNull();
        item.ActualizadoEn.Should().Be(Ahora);
        vehiculo.TramiteId.Should().Be(Tramite);
        vehiculo.TramiteNumero.Should().Be("TR-000123");
        vehiculo.TramiteCreadoEn.Should().Be(Ahora);
        op.Creadas.Should().Be(1);
        op.Estado.Should().Be(EstadoOperacionDerivacion.Terminada);
        op.TerminadaEn.Should().Be(Ahora);
    }

    [Fact]
    public void El_trámite_del_vehículo_se_fija_una_sola_vez()
    {
        var (op, item, vehiculo) = Escenario();
        var previo = Guid.NewGuid();
        vehiculo.TramiteId = previo;
        vehiculo.TramiteNumero = "TR-1";

        CierreItemDerivacion.Cerrar(op, item, vehiculo, new ResultadoBorrador.Creado(Tramite, "TR-2", YaExistia: true), Ahora);

        vehiculo.TramiteId.Should().Be(previo);
        vehiculo.TramiteNumero.Should().Be("TR-1");
        item.TramiteId.Should().Be(Tramite, "el ítem guarda lo que respondió Trámites");
    }

    [Fact]
    public void Rechazo_deja_el_ítem_OMITIDO_con_el_código_y_el_vehículo_sin_trámite()
    {
        var (op, item, vehiculo) = Escenario();

        CierreItemDerivacion.Cerrar(op, item, vehiculo, new ResultadoBorrador.Rechazado(MotivoDerivacion.OtNoHabilitado, "x"), Ahora);

        item.Estado.Should().Be(EstadoItemDerivacion.Omitido);
        item.MotivoCodigo.Should().Be(MotivoDerivacion.OtNoHabilitado);
        item.TramiteId.Should().BeNull();
        vehiculo.TramiteId.Should().BeNull();
        op.Omitidas.Should().Be(1);
        op.Estado.Should().Be(EstadoOperacionDerivacion.Terminada);
    }

    [Fact]
    public void No_disponible_deja_el_ítem_FALLIDO_con_TRAMITES_NO_DISPONIBLE()
    {
        var (op, item, vehiculo) = Escenario();

        CierreItemDerivacion.Cerrar(op, item, vehiculo, new ResultadoBorrador.NoDisponible(Transitorio: true), Ahora);

        item.Estado.Should().Be(EstadoItemDerivacion.Fallido);
        item.MotivoCodigo.Should().Be(MotivoDerivacion.TramitesNoDisponible);
        op.Fallidas.Should().Be(1);
        op.Estado.Should().Be(EstadoOperacionDerivacion.Terminada);
    }

    [Fact]
    public void Con_más_ítems_la_operación_sigue_EN_CURSO_hasta_completar_el_total()
    {
        var (op, item, vehiculo) = Escenario();
        op.Total = 2;

        CierreItemDerivacion.Cerrar(op, item, vehiculo, new ResultadoBorrador.Rechazado(MotivoDerivacion.OtNoHabilitado, "x"), Ahora);

        op.Estado.Should().Be(EstadoOperacionDerivacion.EnCurso);
        op.TerminadaEn.Should().BeNull();
    }

    [Fact]
    public void Un_ítem_ya_cerrado_no_se_vuelve_a_cerrar()
    {
        var (op, item, vehiculo) = Escenario();
        CierreItemDerivacion.Cerrar(op, item, vehiculo, new ResultadoBorrador.Rechazado(MotivoDerivacion.OtNoHabilitado, "x"), Ahora);

        var cerrar = () => CierreItemDerivacion.Cerrar(op, item, vehiculo, new ResultadoBorrador.Creado(Tramite, "TR-3", false), Ahora);

        cerrar.Should().Throw<InvalidOperationException>();
        op.Omitidas.Should().Be(1);
        op.Creadas.Should().Be(0);
    }

    private static (OperacionDerivacion Op, ItemDerivacion Item, VehiculoDiagnostico Vehiculo) Escenario()
    {
        var tenant = Guid.NewGuid();
        var vehiculo = new VehiculoDiagnostico
        {
            TenantId = tenant,
            DiagnosticoId = Guid.NewGuid(),
            IdentificadorOriginal = "TST402",
            IdentificadorNorm = "TST402",
            TipoIdentificador = TipoIdentificador.PlacaCarro,
            Placa = "TST402",
        };
        var op = OperacionDerivacion.Individual(tenant, vehiculo.DiagnosticoId, "TRASPASO_STANDARD", Guid.NewGuid(), Ahora);
        var item = ItemDerivacion.Enviado(op, vehiculo.Id, orden: 1, Ahora);
        return (op, item, vehiculo);
    }
}
