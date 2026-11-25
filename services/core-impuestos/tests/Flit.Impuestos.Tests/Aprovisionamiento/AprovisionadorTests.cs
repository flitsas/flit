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
/// HU #14694 (M07 y M07b; EP-CF2, F1a-CF4, F1a-CF7, F1a-CF8, F1a-CF9; D-ALTA-IDEM, D-TAXID, DA-8) — el
/// <see cref="Aprovisionador"/> evalúa una compañía en <c>PENDIENTE</c> llamando SIEMPRE primero al alta idempotente del
/// proveedor con los datos de Identidad. Si el alta no deja credenciales (y no le quedan reintentos automáticos, o no se pudo
/// hacer), usa de respaldo las existentes: (1) credencial propia → <c>GET /fuentes</c>; (2) la llave de Diagnóstico copiada y
/// validada. Un 409 es un contrato inesperado: <c>ERROR(AMBIENTE)</c> sin respaldo. Incluye las rutas del backoffice del
/// aprovisionamiento (estado con historial y Reintentar).
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

    /// <summary>Proveedor con el alta no disponible en el ambiente (404) y <c>GET /fuentes</c> con <paramref name="fuentes"/>.</summary>
    private static ApiLiquidacionDePrueba SinAlta(HttpStatusCode fuentes = HttpStatusCode.OK) => new()
    {
        Responder = r => r.RequestUri!.AbsolutePath == EntornoAprovisionamiento.RutaAlta
            ? ApiLiquidacionDePrueba.Json(HttpStatusCode.NotFound, EntornoAprovisionamiento.ErrorApi("not_found"))
            : fuentes == HttpStatusCode.OK
                ? ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200)
                : ApiLiquidacionDePrueba.Json(fuentes, EntornoAprovisionamiento.ErrorApi("unauthorized")),
    };

    [Fact]
    public async Task Sin_alta_disponible_la_credencial_propia_con_secreto_se_valida_con_fuentes_y_queda_LISTO_sin_copia()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = SinAlta();
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
        proveedor.Pedidos.Should().ContainSingle(p => p.Uri.EndsWith(EntornoAprovisionamiento.RutaFuentes)).Which.Cabeceras["X-API-Key"].Should().Be(ApiKey);
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1, "D-ALTA-IDEM: el alta va primero");
        identidad.LlamadasLlave.Should().Be(0, "con credencial propia no se pide la de Diagnóstico");
        var paso = (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().ContainSingle().Subject;
        paso.Desde.Should().Be(E.Pendiente);
        paso.Hacia.Should().Be(E.Listo);
        paso.ActorTipo.Should().Be("SISTEMA");
    }

    [Fact]
    public async Task Sin_alta_disponible_la_credencial_propia_sin_secreto_queda_LISTO_SIN_WEBHOOK_con_motivo()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = SinAlta();
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
    public async Task Sin_alta_disponible_la_credencial_propia_rechazada_con_401_pide_credenciales()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = SinAlta(HttpStatusCode.Unauthorized);
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
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1, "el alta se intentó antes de pedir credenciales");
        identidad.LlamadasLlave.Should().Be(0);
    }

    [Fact]
    public async Task Sin_alta_disponible_ni_credencial_propia_copia_la_llave_de_Diagnostico_valida_y_queda_LISTO_SIN_WEBHOOK()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = SinAlta();
        var identidad = new IdentidadDePrueba { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 3) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.ListoSinWebhook);
        fila.MotivoCodigo.Should().Be("SIN_WEBHOOK_SECRET");
        var pedido = proveedor.Pedidos.Should().ContainSingle(p => p.Uri.EndsWith(EntornoAprovisionamiento.RutaFuentes), "solo se valida la llave copiada").Subject;
        pedido.Cabeceras["X-API-Key"].Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
        identidad.LlamadasDatos.Should().Be(1, "el alta se intentó primero");
        var copia = await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct);
        copia!.ApiKey.Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
        copia.WebhookSecret.Should().BeNull("Diagnóstico no tiene secreto de webhook; lo carga el Super Admin");
        await using var db = api.Contexto(empresa);
        db.Set<CredencialEmpresa>().Single().Origen.Should().Be(OrigenCredencial.CopiaDiagnostico);
    }

    [Fact]
    public async Task Sin_alta_disponible_la_llave_de_Diagnostico_rechazada_pide_credenciales_y_no_la_guarda()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = SinAlta(HttpStatusCode.Unauthorized);
        var identidad = new IdentidadDePrueba { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 3) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.RequiereCredenciales);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct)).Should().BeNull();
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

        identidad.LlamadasLlave.Should().Be(0, "D-ALTA-IDEM: el alta va primero y dio credenciales");
        var alta = proveedor.Pedidos.Should().ContainSingle(p => p.Uri.EndsWith(EntornoAprovisionamiento.RutaAlta)).Subject;
        alta.Cabeceras["Authorization"].Should().Be("Bearer " + ClienteApiLiquidacionTests.Token);
        using var cuerpo = JsonDocument.Parse(alta.Cuerpo);
        cuerpo.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["legal_name", "tax_id", "contact_email"]);
        cuerpo.RootElement.GetProperty("tax_id").GetString().Should().Be("900123456", "D-TAXID: NIT base sin DV");
        cuerpo.RootElement.GetProperty("contact_email").GetString().Should().Be("ops@flota.example", "D-EMAIL");
        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Listo);
        fila.FirmaConfirmada.Should().BeFalse();
        fila.MotivoDetalle.Should().BeNull("no hay marca del envío: el alta es idempotente");
    }

    [Fact]
    public async Task Alta_409_es_error_de_ambiente_sin_reintento()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Conflict, EntornoAprovisionamiento.ErrorApi("tax_id_taken"));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        (await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct)).Should().BeFalse("ERROR no se vuelve a evaluar solo");

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Error);
        fila.ErrorTipo.Should().Be(TipoErrorAprovisionamiento.Ambiente);
        fila.MotivoCodigo.Should().Be("AMBIENTE_NO_CONFIGURADO", "el 409 del contrato viejo ya no es EMPRESA_YA_REGISTRADA");
        fila.ProximoIntentoEn.Should().BeNull();
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
    }

    [Theory]
    [InlineData(400, E.Error, TipoErrorAprovisionamiento.Datos, "ALTA_DATOS_RECHAZADOS")]
    [InlineData(415, E.Error, TipoErrorAprovisionamiento.Datos, "ALTA_DATOS_RECHAZADOS")]
    [InlineData(401, E.Error, TipoErrorAprovisionamiento.Ambiente, "AMBIENTE_NO_CONFIGURADO")]
    [InlineData(404, E.Error, TipoErrorAprovisionamiento.Ambiente, "AMBIENTE_NO_CONFIGURADO")]
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
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1, "un alta rechazada no se repite sola");
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
    public async Task Una_marca_ALTA_ENVIADA_que_quedo_de_M07_no_impide_el_alta_idempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct, f => f.MotivoDetalle = "ALTA_ENVIADA");

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Listo, "repetir el alta devuelve las credenciales del primer intento");
        fila.MotivoDetalle.Should().BeNull();
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
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
    public async Task Identidad_caida_al_pedir_los_datos_del_alta_reintenta_sin_llamar_al_proveedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        var identidad = new IdentidadDePrueba { DatosCaido = true };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Pendiente);
        fila.MotivoCodigo.Should().Be("REINTENTO_PROGRAMADO");
        fila.IntentosAutomaticos.Should().Be(1);
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(0, "sin los datos de Identidad no hay alta");
        identidad.LlamadasLlave.Should().Be(0, "quedan reintentos: todavía no se usa el respaldo");
    }

    [Fact]
    public async Task Backoffice_ver_aprovisionamiento_devuelve_estado_accion_e_historial_mas_reciente_primero()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.BadRequest, EntornoAprovisionamiento.ErrorApi("bad_request"));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, true), ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        using var r = await api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api)).GetAsync(RutaBackoffice(empresa), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var raiz = cuerpo.RootElement;
        raiz.GetProperty("estado").GetString().Should().Be("ERROR");
        raiz.GetProperty("motivo").GetString().Should().Be("ALTA_DATOS_RECHAZADOS");
        raiz.GetProperty("accion").GetString().Should().Be("REINTENTAR");
        raiz.GetProperty("firmaConfirmada").GetBoolean().Should().BeFalse();
        raiz.GetProperty("modoRespaldoFirma").GetBoolean().Should().BeFalse();
        var historial = raiz.GetProperty("historial").EnumerateArray().ToList();
        historial.Should().HaveCount(2);
        historial[0].GetProperty("hacia").GetString().Should().Be("ERROR");
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

    [Fact]
    public async Task Backoffice_reintentar_desde_LISTO_SIN_WEBHOOK_responde_202_y_pasa_a_PENDIENTE()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.ListoSinWebhook, ct, f => f.MotivoCodigo = "SIN_WEBHOOK_SECRET");

        using var r = await api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(api)).PostAsync(RutaBackoffice(empresa) + "/reintentar", null, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Accepted, "M07b: la acción de LISTO_SIN_WEBHOOK es REINTENTAR (el alta trae el secreto)");
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Pendiente);
    }

    private static string RutaBackoffice(Guid empresa) => $"{Program.Prefijo}/backoffice/empresas/{empresa}/aprovisionamiento";
}
