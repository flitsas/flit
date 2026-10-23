using System.Reflection;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13860 (F09-H1) — contrato del impuesto vehicular en <c>flit.consultas.v1.ConsultasService</c>: los rpcs
/// <c>ConsultarImpuestoVehicular</c> y <c>ListarDepartamentosImpuesto</c> con sus mensajes (números de campo y tipos del
/// contrato, montos en pesos enteros) y, mientras C3 no exista, UNIMPLEMENTED en un servidor que hereda
/// <see cref="ConsultasService.ConsultasServiceBase"/> sin sobrescribirlos (como <c>ConsultasGrpcService</c> de core-consultas),
/// sin afectar a los demás rpcs. Las pruebas leen el descriptor generado: un campo renumerado o con otro tipo las rompe.
/// </summary>
/// <example>
/// <code>
/// dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~ContratoImpuestoProtoTests"
/// </code>
/// </example>
public sealed class ContratoImpuestoProtoTests
{
    private const string Servicio = "flit.consultas.v1.ConsultasService";

    private static MessageDescriptor? Mensaje(string nombre) =>
        ConsultasReflection.Descriptor.MessageTypes.FirstOrDefault(m => m.Name == nombre);

    private static MessageDescriptor MensajeObligatorio(string nombre)
    {
        var m = Mensaje(nombre);
        m.Should().NotBeNull($"el contrato debe declarar el mensaje {nombre}");
        return m!;
    }

    private static void Campo(MessageDescriptor m, string nombre, int numero, FieldType tipo, bool repetido = false, string? tipoMensaje = null)
    {
        var f = m.FindFieldByName(nombre);
        f.Should().NotBeNull($"{m.Name} debe tener el campo {nombre}");
        f!.FieldNumber.Should().Be(numero, $"{m.Name}.{nombre} es el campo {numero}");
        f.FieldType.Should().Be(tipo, $"{m.Name}.{nombre} es {tipo}");
        f.IsRepeated.Should().Be(repetido, $"{m.Name}.{nombre} repeated={repetido}");
        if (tipoMensaje is not null)
            f.MessageType.FullName.Should().Be(tipoMensaje);
    }

    [Fact]
    public void El_cliente_generado_expone_los_dos_rpcs_de_impuesto()
    {
        var cliente = typeof(ConsultasService.ConsultasServiceClient);
        foreach (var metodo in new[] { "ConsultarImpuestoVehicular", "ListarDepartamentosImpuesto" })
        {
            cliente.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name)
                .Should().Contain($"{metodo}Async", $"el cliente generado debe exponer {metodo}Async");
            typeof(ConsultasService.ConsultasServiceBase).GetMethod(metodo).Should()
                .NotBeNull($"el servidor base debe declarar {metodo}");
        }

        var servicio = ConsultasReflection.Descriptor.Services.Single(s => s.Name == "ConsultasService");
        var consultar = servicio.FindMethodByName("ConsultarImpuestoVehicular");
        consultar.Should().NotBeNull();
        consultar!.InputType.Name.Should().Be("ConsultarImpuestoVehicularRequest");
        consultar.OutputType.Name.Should().Be("ConsultarImpuestoVehicularResponse");
        var listar = servicio.FindMethodByName("ListarDepartamentosImpuesto");
        listar.Should().NotBeNull();
        listar!.InputType.Name.Should().Be("ListarDepartamentosImpuestoRequest");
        listar.OutputType.Name.Should().Be("ListarDepartamentosImpuestoResponse");

        var req = MensajeObligatorio("ConsultarImpuestoVehicularRequest");
        Campo(req, "opciones", 1, FieldType.Message, tipoMensaje: "flit.consultas.v1.OpcionesConsulta");
        Campo(req, "placa", 2, FieldType.Message, tipoMensaje: "flit.platform.v1.Placa");
        Campo(req, "departamento_codigo", 3, FieldType.String);
        req.Fields.InFieldNumberOrder().Should().HaveCount(3);

        var resp = MensajeObligatorio("ConsultarImpuestoVehicularResponse");
        Campo(resp, "resultado", 1, FieldType.Message, tipoMensaje: "flit.consultas.v1.ResultadoConsulta");
        Campo(resp, "monto_total", 2, FieldType.Int64);
        Campo(resp, "vigencias", 3, FieldType.Message, repetido: true, tipoMensaje: "flit.consultas.v1.VigenciaImpuesto");
        resp.Fields.InFieldNumberOrder().Should().HaveCount(3);

        var vigencia = MensajeObligatorio("VigenciaImpuesto");
        Campo(vigencia, "anio", 1, FieldType.Int32);
        Campo(vigencia, "valor", 2, FieldType.Int64);
        Campo(vigencia, "pagada", 3, FieldType.Bool);
        vigencia.Fields.InFieldNumberOrder().Should().HaveCount(3);

        MensajeObligatorio("ListarDepartamentosImpuestoRequest").Fields.InFieldNumberOrder().Should().BeEmpty();
        var deptos = MensajeObligatorio("ListarDepartamentosImpuestoResponse");
        Campo(deptos, "departamento_codigos", 1, FieldType.String, repetido: true);
        deptos.Fields.InFieldNumberOrder().Should().HaveCount(1);
    }

    [Fact]
    public void La_respuesta_del_impuesto_conserva_monto_y_vigencias_en_pesos_enteros()
    {
        var respDesc = MensajeObligatorio("ConsultarImpuestoVehicularResponse");
        var vigDesc = MensajeObligatorio("VigenciaImpuesto");

        var respuesta = (IMessage)Activator.CreateInstance(respDesc.ClrType)!;
        respDesc.FindFieldByName("monto_total").Accessor.SetValue(respuesta, 350_000L);
        var vigencias = (System.Collections.IList)respDesc.FindFieldByName("vigencias").Accessor.GetValue(respuesta);
        vigencias.Add(Vigencia(vigDesc, 2025, 280_000L, pagada: true));
        vigencias.Add(Vigencia(vigDesc, 2026, 350_000L, pagada: false));

        var leida = respDesc.Parser.ParseFrom(respuesta.ToByteArray());

        var monto = respDesc.FindFieldByName("monto_total").Accessor.GetValue(leida);
        monto.Should().BeOfType<long>().And.Be(350_000L);
        var leidas = ((System.Collections.IList)respDesc.FindFieldByName("vigencias").Accessor.GetValue(leida)).Cast<IMessage>().ToList();
        leidas.Should().HaveCount(2);
        Leer(vigDesc, leidas[0]).Should().Be((2025, 280_000L, true));
        Leer(vigDesc, leidas[1]).Should().Be((2026, 350_000L, false));

        // monto_total = suma de lo no pagado (decisión Q1 = a): el contrato lo documenta y el ejemplo lo respeta.
        leidas.Select(v => Leer(vigDesc, v)).Where(v => !v.Pagada).Sum(v => v.Valor).Should().Be(350_000L);
    }

    [Fact]
    public async Task Los_rpcs_de_impuesto_no_estan_sobrescritos_y_responden_UNIMPLEMENTED()
    {
        // Lo declara la base generada: un servidor que no los sobrescribe (core-consultas sin C3) compila igual.
        typeof(ConsultasService.ConsultasServiceBase).GetMethod("ConsultarImpuestoVehicular").Should()
            .NotBeNull("ConsultasServiceBase debe declarar ConsultarImpuestoVehicular");
        typeof(ConsultasService.ConsultasServiceBase).GetMethod("ListarDepartamentosImpuesto").Should()
            .NotBeNull("ConsultasServiceBase debe declarar ListarDepartamentosImpuesto");

        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Services.AddGrpc();
        await using var app = b.Build();
        app.MapGrpcService<ConsultasSinC3>();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() });
        var invocador = canal.CreateCallInvoker();

        foreach (var metodo in new[] { "ConsultarImpuestoVehicular", "ListarDepartamentosImpuesto" })
        {
            var llamada = () => invocador.AsyncUnaryCall(Crudo(metodo), null, new CallOptions(), []).ResponseAsync;
            (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unimplemented, metodo);
        }

        // Los demás rpcs no se afectan: el mismo servidor sigue atendiendo los que sí implementa.
        var rues = await new ConsultasService.ConsultasServiceClient(canal)
            .ConsultarRuesAsync(new ConsultarRuesRequest { Nit = "900000001" }, cancellationToken: TestContext.Current.CancellationToken);
        rues.Resultado.Proveedor.Should().Be("falso");
    }

    private static IMessage Vigencia(MessageDescriptor d, int anio, long valor, bool pagada)
    {
        var v = (IMessage)Activator.CreateInstance(d.ClrType)!;
        d.FindFieldByName("anio").Accessor.SetValue(v, anio);
        d.FindFieldByName("valor").Accessor.SetValue(v, valor);
        d.FindFieldByName("pagada").Accessor.SetValue(v, pagada);
        return v;
    }

    private static (int Anio, long Valor, bool Pagada) Leer(MessageDescriptor d, IMessage v) =>
        ((int)d.FindFieldByName("anio").Accessor.GetValue(v),
         (long)d.FindFieldByName("valor").Accessor.GetValue(v),
         (bool)d.FindFieldByName("pagada").Accessor.GetValue(v));

    // Llamada en bytes (cuerpo vacío = mensaje por defecto): no depende de los tipos generados del impuesto.
    private static Method<byte[], byte[]> Crudo(string metodo)
    {
        var bytes = Marshallers.Create(x => x, x => x);
        return new Method<byte[], byte[]>(MethodType.Unary, Servicio, metodo, bytes, bytes);
    }

    /// <summary>Como <c>ConsultasGrpcService</c> sin C3: implementa un rpc cualquiera y deja el impuesto en la base.</summary>
    private sealed class ConsultasSinC3 : ConsultasService.ConsultasServiceBase
    {
        public override Task<ConsultarRuesResponse> ConsultarRues(ConsultarRuesRequest request, ServerCallContext context) =>
            Task.FromResult(new ConsultarRuesResponse { Resultado = new ResultadoConsulta { Proveedor = "falso" } });
    }
}
