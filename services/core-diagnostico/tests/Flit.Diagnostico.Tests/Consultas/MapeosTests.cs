using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Grpc.Core;
using Xunit;
using TipoErrorFuente = Flit.Diagnostico.Api.Modulos.Consultas.Fuentes.TipoErrorFuente;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// ADR-0075 (HU #13727, T9) — <c>Mapeos/*</c>: la parte de respuesta de cada fuente, sin llamada gRPC. Del <c>*Response</c> de
/// core-consultas sale el resultado normalizado de la celda (sin nombres ni datos del infractor) y del <c>grpcStatus</c> el mismo
/// <c>tipo_error</c> que hoy da un <see cref="RpcException"/>: <c>Unavailable</c> → <c>RED</c>, <c>DeadlineExceeded</c> →
/// <c>TIMEOUT</c>; la RUNT con <c>FAILED_PRECONDITION</c>/<c>UNIMPLEMENTED</c> y el impuesto con <c>UNIMPLEMENTED</c> quedan
/// <c>NO_CONECTADO</c>. Pruebas puras, sin base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~MapeosTests
/// </code>
/// </example>
public sealed class MapeosTests
{
    [Fact]
    public void Traductor_por_codigo_es_el_mismo_que_por_excepcion()
    {
        foreach (var codigo in Enum.GetValues<StatusCode>())
        {
            var porCodigo = TraductorErroresConsultas.Traducir(codigo);
            var porExcepcion = TraductorErroresConsultas.Traducir(new RpcException(new Status(codigo, "detalle 9999912345")));
            porCodigo.Should().Be(porExcepcion, $"{codigo} se traduce igual venga del bus o del gRPC");
        }

        TraductorErroresConsultas.Traducir(StatusCode.Unavailable).TipoError.Should().Be("RED");
        TraductorErroresConsultas.Traducir(StatusCode.DeadlineExceeded).TipoError.Should().Be("TIMEOUT");
    }

    [Fact]
    public void Runt_encontrado_se_mapea_con_el_servicio_Intempo_y_sin_nombres()
    {
        var resultado = MapeoRunt.Mapear(RuntEncontrado());

        resultado.Exitoso.Should().BeTrue();
        var vehiculo = resultado.Vehiculo!;
        (vehiculo.Placa, vehiculo.Vin, vehiculo.Servicio, vehiculo.Organismo).Should().Be(("TST901", "9ZZPRUEBA00000901", "PARTICULAR", "ORGANISMO DE PRUEBA"));
        (vehiculo.Modelo, vehiculo.Cilindraje).Should().Be((2020, 1600));
        vehiculo.ServicioQueResolvio.Should().Be(MapeoRunt.Servicio);
        vehiculo.Propietarios.Should().ContainSingle().Which.Should().BeEquivalentTo(new { TipoDocumento = "CC", NumeroDocumento = "9999900901" });
    }

    [Theory]
    [InlineData(StatusCode.FailedPrecondition, TipoErrorFuente.NoConectado)]
    [InlineData(StatusCode.Unimplemented, TipoErrorFuente.NoConectado)]
    [InlineData(StatusCode.Unavailable, TipoErrorFuente.Red)]
    [InlineData(StatusCode.DeadlineExceeded, TipoErrorFuente.Timeout)]
    [InlineData(StatusCode.Unauthenticated, TipoErrorFuente.Autenticacion)]
    [InlineData(StatusCode.InvalidArgument, TipoErrorFuente.EstadoNoOk)]
    [InlineData(StatusCode.Internal, TipoErrorFuente.EstadoNoOk)]
    public void Runt_con_error_gRPC_queda_con_su_tipo(StatusCode codigo, TipoErrorFuente esperado)
    {
        var resultado = MapeoRunt.Error(codigo);

        resultado.Exitoso.Should().BeFalse();
        resultado.Error.Should().Be(esperado);
        resultado.Mensaje.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Runt_persona_lee_inscripcion_y_licencias_sin_nombres()
    {
        var resultado = new ResultadoConsulta { Proveedor = "kyverum_runt" };
        resultado.Chequeos.Add(new Chequeo { Clave = "conductor_identidad", Estado = EstadoChequeo.Ok });
        resultado.Campos.Add(new Campo { Clave = "person_full_name", ValorTexto = "PERSONA PRUEBA UNO" });
        resultado.Campos.Add(new Campo { Clave = "person_license_status", ValorTexto = "ACTIVO" });
        resultado.Campos.Add(new Campo { Clave = "person_license_categories", ValorTexto = "b1, C1,b1" });

        var r = MapeoRuntPersona.Mapear(new ConsultarConductorResponse { Resultado = resultado });

        r.Exitosa.Should().BeTrue();
        r.Servicio.Should().Be(ServicioConsulta.Kyverum);
        r.Resultado!.Inscrito.Should().BeTrue();
        r.Resultado.EstadoConductor.Should().Be("ACTIVO");
        r.Resultado.Licencias.Select(l => l.Categoria).Should().Equal("B1", "C1");
        MapeoRuntPersona.Mapear(new ConsultarConductorResponse()).Error!.TipoError.Should().Be("RESPUESTA_INVALIDA");
        MapeoRuntPersona.Error(StatusCode.Unavailable).Error!.TipoError.Should().Be("RED");
    }

    [Fact]
    public void Rues_lee_estado_y_renovacion_y_el_proveedor_caido_es_transitorio()
    {
        var resultado = new ResultadoConsulta { Proveedor = "verifik_rues" };
        resultado.Campos.Add(new Campo { Clave = "rues_estado", ValorTexto = "activa" });
        resultado.Campos.Add(new Campo { Clave = "rues_fecha_renovacion", ValorTexto = "2026-03-31" });

        var r = MapeoRues.Mapear(new ConsultarRuesResponse { Resultado = resultado });

        r.Resultado.Should().Be(new ResultadoRues(true, "ACTIVA", "2026-03-31"));
        r.Servicio.Should().Be(ServicioConsulta.Verifik);
        var caido = new ResultadoConsulta { Proveedor = "verifik_rues" };
        caido.Chequeos.Add(new Chequeo { Clave = "rues", Estado = EstadoChequeo.Error });
        var error = MapeoRues.Mapear(new ConsultarRuesResponse { Resultado = caido }).Error!;
        (error.TipoError, error.EsTransitorio).Should().Be(("HTTP_5XX", true));
        MapeoRues.Error(StatusCode.DeadlineExceeded).Error!.TipoError.Should().Be("TIMEOUT");
    }

    [Fact]
    public void Simit_normaliza_comparendos_sin_datos_del_infractor()
    {
        var resultado = new ResultadoConsulta { Proveedor = "smartcloud_simit", Semaforo = Semaforo.Rojo };
        resultado.Campos.Add(new Campo { Clave = "person_full_name", ValorTexto = "PERSONA PRUEBA INFRACTORA" });
        var multas = new Chequeo { Clave = "multas", Estado = EstadoChequeo.Fail };
        multas.Comparendos.Add(new Comparendo { Numero = "C-1", Fecha = "2026-01-10", Valor = "604100.00", Estado = "PENDIENTE", Organismo = "TRANSITO FICTICIO" });
        resultado.Chequeos.Add(multas);

        var r = MapeoSimit.Mapear(new ConsultarMultasResponse { Resultado = resultado });

        r.Resultado!.Semaforo.Should().Be("ROJO");
        r.Resultado.CantidadComparendos.Should().Be(1);
        r.Servicio.Should().Be(ServicioConsulta.Smartcloud);
        System.Text.Json.JsonSerializer.Serialize(r.Resultado).Should().NotContain("INFRACTORA");
        MapeoSimit.Error(StatusCode.PermissionDenied).Error!.TipoError.Should().Be("AUTENTICACION");
    }

    [Fact]
    public void Avaluo_con_valor_y_todas_las_fuentes_en_error_es_transitorio()
    {
        var conValor = new ConsultarAvaluosResponse { ValorSugerido = 52_000_000, FuentePrincipal = "fasecolda" };
        conValor.Avaluos.Add(new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Ok, Valor = 52_000_000 });
        var caidas = new ConsultarAvaluosResponse();
        caidas.Avaluos.Add(new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Error, Mensaje = "detalle del proveedor" });

        var ok = MapeoAvaluo.Mapear(conValor);
        var error = MapeoAvaluo.Mapear(caidas);

        (ok.Resultado!.ValorSugerido, ok.Resultado.FuentePrincipal, ok.Servicio).Should().Be((52_000_000L, "fasecolda", ServicioConsulta.Fasecolda));
        (error.Error!.TipoError, error.Error.EsTransitorio, error.Servicio).Should().Be(("HTTP_5XX", true, ServicioConsulta.Fasecolda));
        MapeoAvaluo.Error(StatusCode.Unavailable).Error!.TipoError.Should().Be("RED");
    }

    [Fact]
    public void Impuesto_normaliza_vigencias_y_UNIMPLEMENTED_es_no_conectado()
    {
        var respuesta = new ConsultarImpuestoVehicularResponse { Resultado = new ResultadoConsulta { Proveedor = "impuestos_prueba" }, MontoTotal = 400_000 };
        respuesta.Vigencias.Add(new VigenciaImpuesto { Anio = 2026, Valor = 190_000 });
        respuesta.Vigencias.Add(new VigenciaImpuesto { Anio = 2025, Valor = 210_000 });

        var r = MapeoImpuesto.Mapear(respuesta, "05");

        r.Resultado!.MontoTotal.Should().Be(400_000);
        r.Resultado.Vigencias.Select(v => v.Anio).Should().Equal(2025, 2026);
        r.Resultado.DepartamentoCodigo.Should().Be("05");
        MapeoImpuesto.Error(StatusCode.Unimplemented).Error!.TipoError.Should().Be(ErroresFuentePersona.NoConectado);
        MapeoImpuesto.Error(StatusCode.FailedPrecondition).Error!.TipoError.Should().Be("ESTADO_NO_OK");
        MapeoImpuesto.Error(StatusCode.DeadlineExceeded).Error!.TipoError.Should().Be("TIMEOUT");
    }

    /// <summary>Respuesta ENCONTRADO inventada (rango ficticio TST9## / 99999009##).</summary>
    internal static ConsultarVehiculoIntempoResponse RuntEncontrado()
    {
        var vehiculo = new VehiculoIntempo
        {
            Plate = "TST901",
            Vin = "9ZZPRUEBA00000901",
            InformacionGeneral = new InformacionGeneralIntempo
            {
                TipoServicio = "PARTICULAR",
                ClaseVehiculo = "AUTOMOVIL",
                OrganismoTransito = "ORGANISMO DE PRUEBA",
                Modelo = "2020",
                Cilidraje = "1600",
            },
        };
        vehiculo.PropietariosActuales.Add(new PropietarioActualIntempo { IdTipoDocumento = "C", NoDocumento = "9999900901", DetallePropiedad = "PROPIO" });
        return new ConsultarVehiculoIntempoResponse { Proveedor = "intempo", Resultado = ResultadoRuntIntempo.Encontrado, Vehiculo = vehiculo };
    }
}
