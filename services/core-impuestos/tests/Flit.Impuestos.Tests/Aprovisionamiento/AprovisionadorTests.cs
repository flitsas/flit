using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Credenciales;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (M07; EP-CF2, F1a-CF4, F1a-CF7, F1a-CF8, F1a-CF9; D-CRED-DIAG, D-ALTA-REINTENTOS, D-409, D-TAXID, DA-8) — el
/// <see cref="Aprovisionador"/> evalúa una compañía en <c>PENDIENTE</c> en el orden de D-CRED-DIAG: (1) credencial propia →
/// <c>GET /fuentes</c>; (2) si no, la llave de Diagnóstico copiada y validada, sin alta ni rotación; (3) si no, el alta con los
/// datos de Identidad. Cada respuesta del alta lleva al estado de D-ALTA-REINTENTOS y solo «no salió» se reintenta solo. Incluye
/// las rutas del backoffice del aprovisionamiento (estado con historial y Reintentar).
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~AprovisionadorTests
/// </code>
/// </example>
public sealed class AprovisionadorTests
{
    private const string ApiKey = ClienteApiLiquidacionTests.ApiKey;
    private const string Secreto = ClienteApiLiquidacionTests.WebhookSecret;

    [Fact]
    public async Task Con_credencial_propia_y_secreto_valida_con_fuentes_y_queda_LISTO_sin_copia_ni_alta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, ApiKey, Secreto, OrigenCredencial.CargaManual, ct);

        (await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct)).Should().BeTrue();

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Listo);
        fila.FirmaConfirmada.Should().BeFalse();
        fila.Version.Should().Be(2);
        proveedor.Pedidos.Should().ContainSingle().Which.Cabeceras["X-API-Key"].Should().Be(ApiKey);
        identidad.LlamadasLlave.Should().Be(0, "con credencial propia no se pide la de Diagnóstico");
        identidad.LlamadasDatos.Should().Be(0, "ni se hace el alta");
        var paso = (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().ContainSingle().Subject;
        paso.Desde.Should().Be(E.Pendiente);
        paso.Hacia.Should().Be(E.Listo);
        paso.ActorTipo.Should().Be("SISTEMA");
    }

    [Fact]
    public async Task Con_credencial_propia_sin_secreto_queda_LISTO_SIN_WEBHOOK_con_motivo()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, ApiKey, null, OrigenCredencial.CargaManual, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.ListoSinWebhook);
        fila.MotivoCodigo.Should().Be("SIN_WEBHOOK_SECRET");
    }

    [Fact]
    public async Task Credencial_propia_rechazada_con_401_pide_credenciales_sin_alta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Unauthorized, EntornoAprovisionamiento.ErrorApi("unauthorized")),
        };
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, ApiKey, Secreto, OrigenCredencial.CargaManual, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.RequiereCredenciales);
        fila.MotivoCodigo.Should().Be("CREDENCIAL_RECHAZADA");
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(0);
        identidad.LlamadasLlave.Should().Be(0);
    }

    [Fact]
    public async Task Sin_credencial_propia_copia_la_llave_de_Diagnostico_valida_sin_alta_y_queda_LISTO_SIN_WEBHOOK()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        var identidad = new IdentidadDePrueba { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 3) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.ListoSinWebhook);
        fila.MotivoCodigo.Should().Be("SIN_WEBHOOK_SECRET");
        var pedido = proveedor.Pedidos.Should().ContainSingle("solo se valida la llave copiada; Diagnóstico queda intacto (sin alta ni rotación)").Subject;
        pedido.Uri.Should().EndWith(EntornoAprovisionamiento.RutaFuentes);
        pedido.Cabeceras["X-API-Key"].Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
        identidad.LlamadasDatos.Should().Be(0, "con copia válida no hay alta");
        var copia = await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct);
        copia!.ApiKey.Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
        copia.WebhookSecret.Should().BeNull("Diagnóstico no tiene secreto de webhook; lo carga el Super Admin");
        await using var db = api.Contexto(empresa);
        db.Set<CredencialEmpresa>().Single().Origen.Should().Be(OrigenCredencial.CopiaDiagnostico);
    }

    [Fact]
    public async Task Llave_de_Diagnostico_rechazada_pide_credenciales_y_no_la_guarda()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Unauthorized, EntornoAprovisionamiento.ErrorApi("unauthorized")),
        };
        var identidad = new IdentidadDePrueba { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 3) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.RequiereCredenciales);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct)).Should().BeNull();
        identidad.LlamadasDatos.Should().Be(0);
    }

    [Fact]
    public async Task Sin_ninguna_credencial_da_de_alta_con_el_cuerpo_exacto_y_queda_LISTO_con_firma_sin_confirmar()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        identidad.LlamadasLlave.Should().Be(1, "primero se busca la llave de Diagnóstico");
        var alta = proveedor.Pedidos.Should().ContainSingle(p => p.Uri.EndsWith(EntornoAprovisionamiento.RutaAlta)).Subject;
        alta.Cabeceras["Authorization"].Should().Be("Bearer " + ClienteApiLiquidacionTests.Token);
        using var cuerpo = JsonDocument.Parse(alta.Cuerpo);
        cuerpo.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["legal_name", "tax_id", "contact_email"]);
        cuerpo.RootElement.GetProperty("tax_id").GetString().Should().Be("900123456", "D-TAXID: NIT base sin DV");
        cuerpo.RootElement.GetProperty("contact_email").GetString().Should().Be("ops@flota.example", "D-EMAIL");
        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Listo);
        fila.FirmaConfirmada.Should().BeFalse();
        fila.MotivoDetalle.Should().BeNull("la marca del envío se limpia al guardar el resultado");
    }

    [Fact]
    public async Task Alta_409_pide_credenciales_sin_reintento()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Conflict, EntornoAprovisionamiento.ErrorApi("tax_id_taken"));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        (await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct)).Should().BeFalse("REQUIERE_CREDENCIALES no se vuelve a evaluar");

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.RequiereCredenciales);
        fila.MotivoCodigo.Should().Be("EMPRESA_YA_REGISTRADA");
        fila.ProximoIntentoEn.Should().BeNull();
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
    }

    [Theory]
    [InlineData(400, E.Error, TipoErrorAprovisionamiento.Datos, "ALTA_DATOS_RECHAZADOS")]
    [InlineData(415, E.Error, TipoErrorAprovisionamiento.Datos, "ALTA_DATOS_RECHAZADOS")]
    [InlineData(401, E.Error, TipoErrorAprovisionamiento.Ambiente, "AMBIENTE_NO_CONFIGURADO")]
    [InlineData(404, E.Error, TipoErrorAprovisionamiento.Ambiente, "AMBIENTE_NO_CONFIGURADO")]
    [InlineData(500, E.Incierto, null, "ALTA_SIN_CONFIRMAR")]
    [InlineData(503, E.Incierto, null, "ALTA_SIN_CONFIRMAR")]
    public async Task Cada_respuesta_del_alta_lleva_a_su_estado_sin_reintento_automatico(int status, E hacia, TipoErrorAprovisionamiento? tipo, string motivo)
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta((HttpStatusCode)status, EntornoAprovisionamiento.ErrorApi("x"));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(hacia);
        fila.ErrorTipo.Should().Be(tipo);
        fila.MotivoCodigo.Should().Be(motivo);
        fila.ProximoIntentoEn.Should().BeNull("sin reintento automático");
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1, "un alta que pudo llegar o que se rechazó no se repite sola");
    }

    [Fact]
    public async Task Si_no_salio_reintenta_con_espera_y_a_la_sexta_es_error_de_ambiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = _ => throw new HttpRequestException("rechazada", new SocketException((int)SocketError.ConnectionRefused)),
        };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        var antes = DateTimeOffset.UtcNow;

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Pendiente);
        fila.IntentosAutomaticos.Should().Be(1);
        fila.MotivoCodigo.Should().Be("REINTENTO_PROGRAMADO");
        fila.ProximoIntentoEn.Should().BeCloseTo(antes + TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30));
        (await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct)).Should().BeFalse("la espera no ha vencido");

        for (var i = 0; i < TransicionesAprovisionamiento.MaximoIntentosAutomaticos; i++)
        {
            await using (var db = api.Contexto(empresa))
            {
                var f = db.Set<AprovisionamientoEmpresa>().Single();
                f.ProximoIntentoEn = DateTimeOffset.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync(ct);
            }

            await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        }

        fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Error);
        fila.ErrorTipo.Should().Be(TipoErrorAprovisionamiento.Ambiente);
        fila.MotivoCodigo.Should().Be("PROVEEDOR_INALCANZABLE");
        Aprovisionador.EsperaReintento(1).Should().Be(TimeSpan.FromMinutes(1));
        Aprovisionador.EsperaReintento(2).Should().Be(TimeSpan.FromMinutes(5));
        Aprovisionador.EsperaReintento(3).Should().Be(TimeSpan.FromMinutes(15));
        Aprovisionador.EsperaReintento(4).Should().Be(TimeSpan.FromMinutes(60));
    }

    [Theory]
    [InlineData(EstadoNitAlta.Ambiguo, "NIT_AMBIGUO")]
    [InlineData(EstadoNitAlta.Ausente, "NIT_AUSENTE")]
    [InlineData(EstadoNitAlta.IgualACabeza, "NIT_IGUAL_A_CABEZA")]
    public async Task NIT_no_valido_pide_revision_sin_llamar_al_alta(EstadoNitAlta estadoNit, string motivo)
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        var identidad = new IdentidadDePrueba { Datos = new DatosAltaProveedor("Hija SAS", string.Empty, estadoNit, "ops@hija.example", true) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.RequiereRevisionNit);
        fila.MotivoCodigo.Should().Be(motivo);
        proveedor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Razon_social_de_mas_de_200_es_error_de_datos_sin_llamar_al_alta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        var identidad = new IdentidadDePrueba { Datos = new DatosAltaProveedor(new string('A', 201), "900123456", EstadoNitAlta.Valido, "ops@flota.example", true) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Error);
        fila.ErrorTipo.Should().Be(TipoErrorAprovisionamiento.Datos);
        fila.MotivoCodigo.Should().Be("RAZON_SOCIAL_LARGA");
        proveedor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_marca_de_alta_enviada_de_un_intento_anterior_es_INCIERTO_sin_volver_a_enviar()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct, f => f.MotivoDetalle = Aprovisionador.MarcaAltaEnviada);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Incierto, "el proceso cayó con el alta en el aire: pudo llegar");
        fila.MotivoCodigo.Should().Be("ALTA_SIN_CONFIRMAR");
        proveedor.Pedidos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(E.Listo)]
    [InlineData(E.RequiereCredenciales)]
    [InlineData(E.Suspendido)]
    public async Task Fuera_de_PENDIENTE_no_evalua_ni_llama(E estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, estado, ct);

        (await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct)).Should().BeFalse();

        proveedor.Pedidos.Should().BeEmpty();
        identidad.LlamadasLlave.Should().Be(0);
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Identidad_caida_al_buscar_la_llave_reintenta_y_no_da_de_alta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        var identidad = new IdentidadDePrueba { LlaveCaida = true };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Pendiente);
        fila.MotivoCodigo.Should().Be("REINTENTO_PROGRAMADO");
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(0, "sin saber si Diagnóstico tiene llave no se da de alta (evita un 409)");
    }

    [Fact]
    public async Task Backoffice_ver_aprovisionamiento_devuelve_estado_accion_e_historial_mas_reciente_primero()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Conflict, EntornoAprovisionamiento.ErrorApi("tax_id_taken"));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, true), ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        using var r = await api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api)).GetAsync(RutaBackoffice(empresa), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var raiz = cuerpo.RootElement;
        raiz.GetProperty("estado").GetString().Should().Be("REQUIERE_CREDENCIALES");
        raiz.GetProperty("motivo").GetString().Should().Be("EMPRESA_YA_REGISTRADA");
        raiz.GetProperty("accion").GetString().Should().Be("CARGAR_CREDENCIALES");
        raiz.GetProperty("firmaConfirmada").GetBoolean().Should().BeFalse();
        raiz.GetProperty("modoRespaldoFirma").GetBoolean().Should().BeFalse();
        var historial = raiz.GetProperty("historial").EnumerateArray().ToList();
        historial.Should().HaveCount(2);
        historial[0].GetProperty("hacia").GetString().Should().Be("REQUIERE_CREDENCIALES");
        historial[0].GetProperty("actorTipo").GetString().Should().Be("SISTEMA");
        historial[1].GetProperty("desde").ValueKind.Should().Be(JsonValueKind.Null);
        historial[1].GetProperty("hacia").GetString().Should().Be("PENDIENTE");
        historial[1].GetProperty("actorTipo").GetString().Should().Be("PLATAFORMA");
    }

    [Fact]
    public async Task Backoffice_ver_aprovisionamiento_sin_fila_es_404_y_quien_no_es_SA_recibe_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();

        using var sinFila = await api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api)).GetAsync(RutaBackoffice(empresa), ct);
        using var admin = await api.Cliente(api.Token(empresa: empresa, roles: ["admin_impuestos"], permisos: ["impuestos.backoffice.ver"])).GetAsync(RutaBackoffice(empresa), ct);

        sinFila.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await sinFila.Content.ReadAsStringAsync(ct)).Should().Contain("EMPRESA_NO_ENCONTRADA");
        admin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Backoffice_reintentar_desde_ERROR_responde_202_pasa_a_PENDIENTE_y_desde_LISTO_es_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var conError = Guid.NewGuid();
        var lista = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, conError, E.Error, ct, f =>
        {
            f.ErrorTipo = TipoErrorAprovisionamiento.Datos;
            f.MotivoCodigo = "ALTA_DATOS_RECHAZADOS";
        });
        await EntornoAprovisionamiento.SembrarAsync(api, lista, E.Listo, ct);
        var sa = api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api));

        using var aceptado = await sa.PostAsync(RutaBackoffice(conError) + "/reintentar", null, ct);
        using var rechazado = await sa.PostAsync(RutaBackoffice(lista) + "/reintentar", null, ct);

        aceptado.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var fila = await EntornoAprovisionamiento.FilaAsync(api, conError, ct);
        fila!.Estado.Should().Be(E.Pendiente);
        fila.ErrorTipo.Should().BeNull();
        fila.MotivoCodigo.Should().BeNull();
        fila.Version.Should().Be(2);
        var paso = (await EntornoAprovisionamiento.HistorialAsync(api, conError, ct)).Should().ContainSingle().Subject;
        paso.ActorTipo.Should().Be("SUPER_ADMIN");
        paso.ActorId.Should().Be(CargaManualCredencialesTests.ActorSuperAdmin);
        rechazado.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var texto = await rechazado.Content.ReadAsStringAsync(ct);
        texto.Should().Contain("ESTADO_NO_REINTENTABLE").And.Contain("En el estado actual de la compañía no se puede reintentar.");
        (await EntornoAprovisionamiento.FilaAsync(api, lista, ct))!.Version.Should().Be(1);
    }

    private static string RutaBackoffice(Guid empresa) => $"{Program.Prefijo}/backoffice/empresas/{empresa}/aprovisionamiento";
}
