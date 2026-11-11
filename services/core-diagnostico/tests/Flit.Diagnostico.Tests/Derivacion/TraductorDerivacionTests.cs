using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §3 tabla de códigos, ADR-0084) — traducción de la respuesta y de los status del rpc
/// <c>CrearBorradorDesdeDiagnostico</c>: <c>creado</c> es éxito también con <c>ya_existia=true</c>; los nueve rechazos de negocio
/// son no reintentables (OMITIDO con su código); RESOURCE_EXHAUSTED, UNAVAILABLE, INTERNAL y DEADLINE_EXCEEDED son transitorios;
/// PERMISSION_DENIED, UNAUTHENTICATED y UNIMPLEMENTED son de configuración (TRAMITES_NO_DISPONIBLE no transitorio). Sin base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TraductorDerivacionTests
/// </code>
/// </example>
public sealed class TraductorDerivacionTests
{
    private static readonly Guid Tramite = Guid.Parse("0199f3a1-7777-7000-8000-000000000077");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Creado_es_éxito_con_o_sin_ya_existia(bool yaExistia)
    {
        var r = TraductorDerivacion.Desde(new CrearBorradorDesdeDiagnosticoResponse
        {
            Creado = new BorradorCreado { ProcedureInstanceId = Tramite.ToString(), Numero = "TR-000123", Estado = "borrador", YaExistia = yaExistia },
        });

        r.Should().BeEquivalentTo(new ResultadoBorrador.Creado(Tramite, "TR-000123", yaExistia));
    }

    [Theory]
    [InlineData("TIPO_NO_DISPONIBLE")]
    [InlineData("FAMILIA_BLOQUEADA")]
    [InlineData("USUARIO_SIN_PERMISO")]
    [InlineData("OT_NO_HABILITADO")]
    [InlineData("REGLA_COMPANIA")]
    [InlineData("TRAMITE_ACTIVO_DUPLICADO")]
    [InlineData("EMPRESA_INACTIVA")]
    [InlineData("REFERENCIA_DE_OTRO_ORIGEN")]
    [InlineData("DATOS_INVALIDOS")]
    public void Rechazo_de_negocio_conserva_el_código_y_el_motivo(string codigo)
    {
        var r = TraductorDerivacion.Desde(new CrearBorradorDesdeDiagnosticoResponse { Rechazo = new RechazoBorrador { Codigo = codigo, Motivo = "Motivo de Trámites." } });

        r.Should().BeEquivalentTo(new ResultadoBorrador.Rechazado(codigo, "Motivo de Trámites."));
    }

    [Fact]
    public void Rechazo_con_código_desconocido_queda_como_RECHAZO_TRAMITES_y_sin_motivo_usa_el_mensaje_propio()
    {
        var r = TraductorDerivacion.Desde(new CrearBorradorDesdeDiagnosticoResponse { Rechazo = new RechazoBorrador { Codigo = "ALGO_NUEVO" } });

        r.Should().BeEquivalentTo(new ResultadoBorrador.Rechazado(MotivoDerivacion.RechazoTramites, MotivoDerivacion.Mensaje(MotivoDerivacion.RechazoTramites)));
    }

    [Fact]
    public void Creado_con_id_que_no_es_uuid_o_respuesta_vacía_no_es_éxito()
    {
        TraductorDerivacion.Desde(new CrearBorradorDesdeDiagnosticoResponse { Creado = new BorradorCreado { ProcedureInstanceId = "x", Numero = "1" } })
            .Should().BeEquivalentTo(new ResultadoBorrador.NoDisponible(Transitorio: false));
        TraductorDerivacion.Desde(new CrearBorradorDesdeDiagnosticoResponse())
            .Should().BeEquivalentTo(new ResultadoBorrador.NoDisponible(Transitorio: false));
    }

    [Theory]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public void Status_transitorio(StatusCode estado) =>
        TraductorDerivacion.Desde(new RpcException(new Status(estado, "x"))).Should().BeEquivalentTo(new ResultadoBorrador.NoDisponible(Transitorio: true));

    [Theory]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.Unimplemented)]
    [InlineData(StatusCode.InvalidArgument)]
    public void Status_de_configuración_no_es_transitorio(StatusCode estado) =>
        TraductorDerivacion.Desde(new RpcException(new Status(estado, "x"))).Should().BeEquivalentTo(new ResultadoBorrador.NoDisponible(Transitorio: false));
}
