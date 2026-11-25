using System.Text.RegularExpressions;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.ApiLiquidacion;
using FluentAssertions;
using Grpc.Core;
using Id = Flit.Identidad.Grpc.V1;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (M07; F1b-CF1, plan §1.2 decisión R1, M04) — el <see cref="ReportadorEstado"/> manda a la plataforma
/// (<c>ReportarEstadoAprovisionamiento</c>) el estado, el motivo, su literal como mensaje (≤ 300), la acción, la firma y la
/// versión de la fila, todo en mayúsculas como lo exige el rpc, y solo si la versión no se reportó. Una respuesta
/// <c>aplicado=false</c> con una versión igual o mayor cuenta como reportada (no es error); si Identidad no responde, la fila
/// queda pendiente de reporte y se reintenta en la siguiente ronda. Los literales son los de <c>impuestos.v1.yaml</c>.
/// </summary>
/// <example>
/// <code>
/// var reporte = ReportadorEstado.Construir(fila); // { Estado = "REQUIERE_CREDENCIALES", Accion = "CARGAR_CREDENCIALES", ... }
/// </code>
/// </example>
public sealed partial class ReportadorEstadoTests
{
    [Fact]
    public async Task Reporta_estado_motivo_mensaje_accion_firma_y_version_y_marca_la_version_reportada()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.RequiereCredenciales, ct, f =>
        {
            f.MotivoCodigo = "EMPRESA_YA_REGISTRADA";
            f.Version = 4;
        });

        (await ReportarAsync(api, empresa, ct)).Should().BeTrue();

        var (destino, reporte) = identidad.Reportes.Should().ContainSingle().Subject;
        destino.Should().Be(empresa, "la compañía viaja en x-flit-tenant-id");
        reporte.Estado.Should().Be("REQUIERE_CREDENCIALES");
        reporte.MotivoCodigo.Should().Be("EMPRESA_YA_REGISTRADA");
        reporte.Mensaje.Should().Be("La empresa ya existe en el proveedor: rota las credenciales en la consola del API y cárgalas en Impuestos.");
        reporte.Accion.Should().Be("CARGAR_CREDENCIALES");
        reporte.FirmaConfirmada.Should().BeFalse();
        reporte.Version.Should().Be(4);
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.ReportadoVersion.Should().Be(4);
    }

    [Fact]
    public async Task Una_version_ya_reportada_no_se_vuelve_a_mandar()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Listo, ct, f =>
        {
            f.Version = 3;
            f.ReportadoVersion = 3;
        });

        (await ReportarAsync(api, empresa, ct)).Should().BeFalse();

        identidad.Reportes.Should().BeEmpty();
    }

    [Fact]
    public async Task Aplicado_false_con_una_version_igual_o_mayor_cuenta_como_reportada()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba { Responder = r => new RespuestaReporte(false, r.Version) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Listo, ct, f => f.Version = 2);

        (await ReportarAsync(api, empresa, ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.ReportadoVersion.Should().Be(2);
    }

    [Fact]
    public async Task Sin_Identidad_la_fila_queda_pendiente_de_reporte_sin_lanzar()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba { ReporteCaido = true };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        (await ReportarAsync(api, empresa, ct)).Should().BeFalse();

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.ReportadoVersion.Should().Be(0);
    }

    [Fact]
    public async Task Cada_transicion_del_aprovisionador_se_reporta_al_terminar()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba();
        // M07b: el alta va primero; con el alta no disponible (404) queda la credencial propia de respaldo.
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = r => r.RequestUri!.AbsolutePath == EntornoAprovisionamiento.RutaAlta
                ? ApiLiquidacionDePrueba.Json(System.Net.HttpStatusCode.NotFound, EntornoAprovisionamiento.ErrorApi("not_found"))
                : ApiLiquidacionDePrueba.Json(System.Net.HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200),
        };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, ClienteApiLiquidacionTests.ApiKey, null, OrigenCredencial.CargaManual, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var reporte = identidad.Reportes.Should().ContainSingle().Subject.Reporte;
        reporte.Estado.Should().Be("LISTO_SIN_WEBHOOK");
        reporte.Accion.Should().Be("REINTENTAR", "1.2.0: el alta idempotente trae el secreto");
        reporte.MotivoCodigo.Should().Be("SIN_WEBHOOK_SECRET");
        reporte.Version.Should().Be(2);
    }

    [Theory]
    [InlineData(E.Pendiente, "")]
    [InlineData(E.Listo, "")]
    [InlineData(E.ListoSinWebhook, "REINTENTAR")]
    [InlineData(E.Error, "REINTENTAR")]
    [InlineData(E.Incierto, "REINTENTAR")]
    [InlineData(E.RequiereCredenciales, "CARGAR_CREDENCIALES")]
    [InlineData(E.RequiereRevisionNit, "CORREGIR_NIT")]
    [InlineData(E.Suspendido, "")]
    public void La_accion_sale_del_estado(E estado, string accion)
    {
        var reporte = ReportadorEstado.Construir(new AprovisionamientoEmpresa { TenantId = Guid.NewGuid(), Estado = estado, Version = 1 });

        reporte.Accion.Should().Be(accion);
        reporte.Estado.Should().Be(Codigo(estado), "en mayúsculas con guion bajo, como el CHECK y el rpc");
    }

    [Fact]
    public void Sin_motivo_el_reporte_va_con_motivo_y_mensaje_vacios()
    {
        var reporte = ReportadorEstado.Construir(new AprovisionamientoEmpresa { TenantId = Guid.NewGuid(), Estado = E.Listo, Version = 7, FirmaConfirmada = true });

        reporte.Should().Be(new ReporteEstadoAprovisionamiento("LISTO", string.Empty, string.Empty, string.Empty, true, 7));
    }

    [Fact]
    public void Los_literales_de_los_motivos_son_los_del_contrato_y_caben_en_el_rpc()
    {
        var contrato = LiteralesDelContrato();

        contrato.Should().HaveCount(15, "1.2.0 suma EMPRESA_INACTIVA_EN_PROVEEDOR y CLAVE_ROTADA y conserva los retirados");
        MotivosAprovisionamiento.Literales.Should().BeEquivalentTo(contrato);
        MotivosAprovisionamiento.Retirados.Should().BeEquivalentTo(["EMPRESA_YA_REGISTRADA", "ALTA_SIN_CONFIRMAR"]);
        MotivosAprovisionamiento.Retirados.Should().OnlyContain(r => MotivosAprovisionamiento.EsDelContrato(r), "el historial puede traerlos");
        MotivosAprovisionamiento.Literales.Keys.Should().OnlyContain(k => k.Length <= 64);
        MotivosAprovisionamiento.Literales.Values.Should().OnlyContain(v => v.Length <= 300);
    }

    [Fact]
    public async Task El_cliente_gRPC_manda_la_compania_en_x_flit_tenant_id_y_todos_los_campos_del_reporte()
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var invocador = new InvocadorDePrueba(_ => new Id.ReportarEstadoAprovisionamientoResponse { Aplicado = false, VersionGuardada = 9 });
        var cliente = Cliente(invocador);

        var r = await cliente.ReportarAsync(empresa, new ReporteEstadoAprovisionamiento("ERROR", "AMBIENTE_NO_CONFIGURADO", "texto", "REINTENTAR", false, 9), ct);

        r.Should().Be(new RespuestaReporte(false, 9));
        invocador.Metodo.Should().EndWith("AprovisionamientoProductoService/ReportarEstadoAprovisionamiento");
        invocador.Cabeceras!.GetValue("x-flit-tenant-id").Should().Be(empresa.ToString());
        invocador.Peticion.Should().BeEquivalentTo(new Id.ReportarEstadoAprovisionamientoRequest
        {
            Estado = "ERROR",
            MotivoCodigo = "AMBIENTE_NO_CONFIGURADO",
            Mensaje = "texto",
            Accion = "REINTENTAR",
            FirmaConfirmada = false,
            Version = 9,
        });
    }

    [Theory]
    [InlineData(Id.EstadoNit.Valido, EstadoNitAlta.Valido)]
    [InlineData(Id.EstadoNit.Ambiguo, EstadoNitAlta.Ambiguo)]
    [InlineData(Id.EstadoNit.Ausente, EstadoNitAlta.Ausente)]
    [InlineData(Id.EstadoNit.IgualACabeza, EstadoNitAlta.IgualACabeza)]
    [InlineData(Id.EstadoNit.Unspecified, EstadoNitAlta.Ambiguo)]
    public async Task El_cliente_gRPC_lee_los_datos_del_alta_con_la_cabecera_de_la_compania(Id.EstadoNit nit, EstadoNitAlta esperado)
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var invocador = new InvocadorDePrueba(_ => new Id.ObtenerDatosAltaProveedorResponse
        {
            LegalName = "Flota Andina SAS",
            TaxIdBase = "900123456",
            EstadoNit = nit,
            ContactEmail = string.Empty,
            EmpresaActiva = true,
        });

        var datos = await Cliente(invocador).ObtenerDatosAltaAsync(empresa, ct);

        datos.Should().Be(new DatosAltaProveedor("Flota Andina SAS", "900123456", esperado, string.Empty, true), "correo nulo en la plataforma llega vacío (M04)");
        invocador.Cabeceras!.GetValue("x-flit-tenant-id").Should().Be(empresa.ToString());
    }

    [Fact]
    public async Task El_cliente_gRPC_lee_la_llave_de_Diagnostico_y_un_error_del_rpc_es_Identidad_no_disponible()
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var con = new InvocadorDePrueba(_ => new Id.ObtenerCredencialImpuestoParaCopiaResponse { Existe = true, ApiKey = "llave", Version = 4 });
        var caido = new InvocadorDePrueba(_ => throw new RpcException(new Status(StatusCode.Internal, "empresa inexistente")));

        var llave = await Cliente(con).ObtenerLlaveDiagnosticoAsync(empresa, ct);
        var act = () => Cliente(caido).ObtenerLlaveDiagnosticoAsync(empresa, ct);

        llave.Should().Be(new LlaveDiagnostico(true, "llave", 4));
        con.Cabeceras!.GetValue("x-flit-tenant-id").Should().Be(empresa.ToString());
        (await act.Should().ThrowAsync<IdentidadNoDisponibleException>()).Which.Message.Should().Contain("Internal").And.NotContain("llave");
    }

    private static ClienteIdentidadAltaGrpc Cliente(CallInvoker invocador) => new(
        new Id.EmpresasService.EmpresasServiceClient(invocador),
        new Id.CredencialesProductoService.CredencialesProductoServiceClient(invocador),
        new Id.AprovisionamientoProductoService.AprovisionamientoProductoServiceClient(invocador));

    /// <summary>Invocador gRPC en memoria: guarda el método, las cabeceras y la petición y responde con <c>responder</c>.</summary>
    private sealed class InvocadorDePrueba(Func<object, object> responder) : CallInvoker
    {
        public string? Metodo { get; private set; }

        public Metadata? Cabeceras { get; private set; }

        public object? Peticion { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Metodo = method.FullName;
            Cabeceras = options.Headers;
            Peticion = request;
            var respuesta = Task.FromResult((TResponse)responder(request!));
            return new AsyncUnaryCall<TResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();
    }

    private static async Task<bool> ReportarAsync(Api.ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReportadorEstado>().ReportarAsync(empresa, ct);
    }

    private static string Codigo(E estado) => Regex.Replace(estado.ToString(), "(?<!^)([A-Z])", "_$1").ToUpperInvariant();

    /// <summary><c>x-flit-etiquetas</c> de <c>MotivoAprovisionamiento</c> en <c>contracts/openapi/impuestos.v1.yaml</c>.</summary>
    private static Dictionary<string, string> LiteralesDelContrato()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "impuestos.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repo");
        var lineas = File.ReadAllLines(Path.Combine(dir!.FullName, "contracts", "openapi", "impuestos.v1.yaml"));
        var inicio = Array.FindIndex(lineas, l => l.TrimEnd() == "    MotivoAprovisionamiento:");
        var etiquetas = Array.FindIndex(lineas, inicio, l => l.Trim() == "x-flit-etiquetas:");
        var resultado = new Dictionary<string, string>();
        for (var i = etiquetas + 1; i < lineas.Length && lineas[i].StartsWith("        ", StringComparison.Ordinal); i++)
        {
            var m = Etiqueta().Match(lineas[i]);
            if (m.Success)
                resultado[m.Groups[1].Value] = m.Groups[2].Value.Trim().Trim('"');
        }

        return resultado;
    }

    [GeneratedRegex("^\\s+([A-Z_]+):\\s*(.+)$")]
    private static partial Regex Etiqueta();
}
