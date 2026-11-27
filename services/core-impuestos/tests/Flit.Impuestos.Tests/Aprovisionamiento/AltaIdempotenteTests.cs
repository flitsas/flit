using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.ApiLiquidacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (M07b, plan §5.8, D-ALTA-IDEM; guía del proveedor §3 y §6) — el alta del proveedor es idempotente: 200 o 201 traen
/// las credenciales (la misma empresa con el mismo NIT), el reintento tras timeout/5xx/429 es seguro con el mismo cuerpo,
/// <c>api_key_rotated</c> reemplaza la llave en la misma transacción (historial <c>CLAVE_ROTADA</c> y propagación a Diagnóstico
/// por la outbox) y <c>company.active=false</c> guarda las credenciales y deja <c>ERROR(AMBIENTE)</c>
/// <c>EMPRESA_INACTIVA_EN_PROVEEDOR</c>. El alta va siempre primero; la copia de Diagnóstico queda de respaldo. Las filas que M07
/// dejó en <c>INCIERTO</c> por el alta vuelven a <c>PENDIENTE</c> al arrancar.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~AltaIdempotenteTests
/// </code>
/// </example>
public sealed class AltaIdempotenteTests
{
    private const string ApiKey = ClienteApiLiquidacionTests.ApiKey;
    private const string Secreto = ClienteApiLiquidacionTests.WebhookSecret;
    private const string LlaveRotada = "RoTaDa-llave-nueva-de-prueba-0123456789-xyz";

    [Fact]
    public async Task Alta_200_de_una_empresa_que_ya_existia_trae_las_mismas_credenciales_y_queda_LISTO()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Listo);
        fila.MotivoCodigo.Should().BeNull();
        var credencial = await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct);
        credencial!.ApiKey.Should().Be(ApiKey);
        credencial.WebhookSecret.Should().Be(Secreto);
        credencial.EmpresaApiId.Should().Be(EntornoAprovisionamiento.EmpresaApiId);
    }

    [Fact]
    public async Task El_alta_va_primero_aunque_la_compania_tenga_llave_de_Diagnostico()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false));
        var identidad = new IdentidadDePrueba { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 3) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        identidad.LlamadasLlave.Should().Be(0, "D-ALTA-IDEM: la copia de Diagnóstico solo es respaldo");
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo);
        await using var db = api.Contexto(empresa);
        db.Set<CredencialEmpresa>().Single().Origen.Should().Be(OrigenCredencial.Alta);
    }

    [Fact]
    public async Task Reintentar_desde_LISTO_SIN_WEBHOOK_vuelve_a_dar_de_alta_y_obtiene_el_secreto()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.ListoSinWebhook, ct, f => f.MotivoCodigo = "SIN_WEBHOOK_SECRET");
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, ApiKey, null, OrigenCredencial.CopiaDiagnostico, ct);

        (await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Reintentar(empresa, Guid.NewGuid()), ct)).Should().BeTrue();
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Pendiente);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.WebhookSecret.Should().Be(Secreto);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(0)]
    public async Task Timeout_5xx_429_o_sin_respuesta_se_reintentan_solos_con_el_mismo_cuerpo_y_el_reintento_da_las_credenciales(int primera)
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAltas(n => n == 1
            ? primera == 0
                ? throw new TaskCanceledException("timeout de prueba")
                : ApiLiquidacionDePrueba.Json((HttpStatusCode)primera, EntornoAprovisionamiento.ErrorApi("x"))
            : ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false)));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        var antes = DateTimeOffset.UtcNow;

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Pendiente, "ya no hay INCIERTO desde el alta: el reintento es seguro");
        fila.MotivoCodigo.Should().Be("REINTENTO_PROGRAMADO");
        fila.IntentosAutomaticos.Should().Be(1);
        fila.ProximoIntentoEn.Should().BeCloseTo(antes + TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30));
        fila.MotivoDetalle.Should().NotBe("ALTA_ENVIADA", "la marca del envío se retiró");

        await VencerEsperaAsync(api, empresa, ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo);
        var altas = proveedor.Pedidos.Where(p => p.Uri.EndsWith(EntornoAprovisionamiento.RutaAlta, StringComparison.Ordinal)).ToList();
        altas.Should().HaveCount(2);
        altas[1].Cuerpo.Should().Be(altas[0].Cuerpo, "guía §3: el reintento va con el mismo cuerpo");
    }

    [Fact]
    public async Task Alta_409_es_un_contrato_inesperado_ERROR_de_ambiente_con_alerta_y_sin_reintento()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Conflict, EntornoAprovisionamiento.ErrorApi("tax_id_taken"));
        var identidad = new IdentidadDePrueba { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 3) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        (await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct)).Should().BeFalse();

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Error);
        fila.ErrorTipo.Should().Be(TipoErrorAprovisionamiento.Ambiente);
        fila.MotivoCodigo.Should().Be("AMBIENTE_NO_CONFIGURADO");
        fila.MotivoDetalle.Should().Be(AltaEmpresaApi.DetalleContratoInesperado);
        fila.ProximoIntentoEn.Should().BeNull();
        identidad.LlamadasLlave.Should().Be(0, "un 409 no se tapa con la copia de Diagnóstico: hay que verlo");
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
    }

    [Fact]
    public async Task Empresa_inactiva_en_el_proveedor_guarda_las_credenciales_y_queda_ERROR_de_ambiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false, activa: false));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Error);
        fila.ErrorTipo.Should().Be(TipoErrorAprovisionamiento.Ambiente);
        fila.MotivoCodigo.Should().Be("EMPRESA_INACTIVA_EN_PROVEEDOR");
        var credencial = await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct);
        credencial!.ApiKey.Should().Be(ApiKey, "las credenciales vienen igual y se guardan para cuando la reactiven");
        credencial.WebhookSecret.Should().Be(Secreto);
        (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Last().MotivoCodigo.Should().Be("EMPRESA_INACTIVA_EN_PROVEEDOR");
    }

    [Fact]
    public async Task Llave_rotada_reemplaza_la_guardada_deja_CLAVE_ROTADA_y_encola_la_propagacion_sin_la_llave()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false, apiKey: LlaveRotada, rotada: true));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, EntornoAprovisionamiento.LlaveDiagnostico, null, OrigenCredencial.CopiaDiagnostico, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(LlaveRotada);
        var historial = await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct);
        historial.Should().Contain(h => h.MotivoCodigo == "CLAVE_ROTADA" && h.Hacia == E.Listo);
        var mensajes = await EntornoAprovisionamiento.OutboxAsync(api, empresa, ConsumidorCredenciales.TipoPropagar, ct);
        var mensaje = mensajes.Should().ContainSingle().Subject;
        mensaje.Exchange.Should().Be("flit.impuestos");
        mensaje.Payload.Should().NotContain(LlaveRotada).And.NotContain(EntornoAprovisionamiento.LlaveDiagnostico);
        EntornoAprovisionamiento.Sobre(mensaje).TenantId.Should().Be(empresa);
    }

    [Fact]
    public async Task Sin_rotacion_no_se_encola_ninguna_propagacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false));
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.OutboxAsync(api, empresa, ConsumidorCredenciales.TipoPropagar, ct)).Should().BeEmpty();
        (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().NotContain(h => h.MotivoCodigo == "CLAVE_ROTADA");
    }

    [Fact]
    public async Task La_propagacion_manda_la_llave_vigente_a_Diagnostico_por_Identidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false, apiKey: LlaveRotada, rotada: true));
        var identidad = new IdentidadDePrueba();
        var logs = new RegistroDeLogs();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad, logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        var sobre = EntornoAprovisionamiento.Sobre((await EntornoAprovisionamiento.OutboxAsync(api, empresa, ConsumidorCredenciales.TipoPropagar, ct)).Single());

        (await EntornoAprovisionamiento.EntregarInternoAsync(api, sobre, ct)).Should().BeTrue();
        (await EntornoAprovisionamiento.EntregarInternoAsync(api, sobre, ct)).Should().BeFalse("la bandeja ya tiene ese eventId");

        identidad.Actualizaciones.Should().Equal((empresa, LlaveRotada));
        logs.Todo.Should().NotContain(LlaveRotada);
    }

    [Fact]
    public async Task La_propagacion_sin_llave_de_Diagnostico_termina_sin_error_y_con_Identidad_caida_se_reintenta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false, apiKey: LlaveRotada, rotada: true));
        var identidad = new IdentidadDePrueba { DiagnosticoTieneLlave = false, ActualizacionCaida = true };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        var sobre = EntornoAprovisionamiento.Sobre((await EntornoAprovisionamiento.OutboxAsync(api, empresa, ConsumidorCredenciales.TipoPropagar, ct)).Single());

        var caida = () => EntornoAprovisionamiento.EntregarInternoAsync(api, sobre, ct);
        await caida.Should().ThrowAsync<IdentidadNoDisponibleException>("el SDK reintenta el trabajo con su espera");

        identidad.ActualizacionCaida = false;
        (await EntornoAprovisionamiento.EntregarInternoAsync(api, sobre, ct)).Should().BeTrue();
        identidad.Actualizaciones.Should().ContainSingle();
    }

    [Fact]
    public async Task Con_el_alta_inalcanzable_tras_los_reintentos_usa_la_copia_de_Diagnostico_de_respaldo()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = r => r.RequestUri!.AbsolutePath == EntornoAprovisionamiento.RutaAlta
                ? throw new HttpRequestException("rechazada", new SocketException((int)SocketError.ConnectionRefused))
                : ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200),
        };
        var identidad = new IdentidadDePrueba { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 3) };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        for (var i = 0; i < TransicionesAprovisionamiento.MaximoIntentosAutomaticos; i++)
        {
            await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
            (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Pendiente);
            identidad.LlamadasLlave.Should().Be(0, $"intento {i + 1}: todavía quedan reintentos del alta");
            await VencerEsperaAsync(api, empresa, ct);
        }

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.ListoSinWebhook);
        identidad.LlamadasLlave.Should().Be(1);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
    }

    [Fact]
    public async Task Una_llave_propia_rechazada_se_recupera_con_el_alta_antes_de_pedir_credenciales()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = r => r.RequestUri!.AbsolutePath == EntornoAprovisionamiento.RutaAlta
                ? ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false))
                : ApiLiquidacionDePrueba.Json(HttpStatusCode.Unauthorized, EntornoAprovisionamiento.ErrorApi("unauthorized")),
        };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, "LlAvE-vieja-rechazada-0123456789-abcdefgh", null, OrigenCredencial.CargaManual, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(ApiKey);
        EntornoAprovisionamiento.Fuentes(proveedor).Should().Be(0, "con el alta disponible no se valida la llave vieja");
    }

    [Fact]
    public async Task Al_arrancar_las_filas_INCIERTO_del_alta_vuelven_a_PENDIENTE_y_se_limpia_la_marca_retirada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var incierta = Guid.NewGuid();
        var conMarca = Guid.NewGuid();
        var otra = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, incierta, E.Incierto, ct, f =>
        {
            f.MotivoCodigo = "ALTA_SIN_CONFIRMAR";
            f.Version = 4;
        });
        await EntornoAprovisionamiento.SembrarAsync(api, conMarca, E.Pendiente, ct, f => f.MotivoDetalle = "ALTA_ENVIADA");
        await EntornoAprovisionamiento.SembrarAsync(api, otra, E.Error, ct, f =>
        {
            f.ErrorTipo = TipoErrorAprovisionamiento.Datos;
            f.MotivoCodigo = "ALTA_DATOS_RECHAZADOS";
        });
        var trabajador = ActivatorUtilities.CreateInstance<TrabajadorReintentoAlta>(api.App.Services);

        (await trabajador.NormalizarAltasRetiradasAsync(ct)).Should().Be(1);
        (await trabajador.NormalizarAltasRetiradasAsync(ct)).Should().Be(0, "es de una sola vez: ya no quedan");

        var fila = await EntornoAprovisionamiento.FilaAsync(api, incierta, ct);
        fila!.Estado.Should().Be(E.Pendiente);
        fila.MotivoCodigo.Should().BeNull();
        fila.ProximoIntentoEn.Should().BeNull("la ronda siguiente la evalúa");
        fila.Version.Should().Be(5);
        var paso = (await EntornoAprovisionamiento.HistorialAsync(api, incierta, ct)).Should().ContainSingle().Subject;
        paso.Desde.Should().Be(E.Incierto);
        paso.Hacia.Should().Be(E.Pendiente);
        paso.ActorTipo.Should().Be("SISTEMA");
        paso.MotivoCodigo.Should().Be(TrabajadorReintentoAlta.MotivoAltaIdempotente);
        (await EntornoAprovisionamiento.FilaAsync(api, conMarca, ct))!.MotivoDetalle.Should().BeNull();
        (await EntornoAprovisionamiento.FilaAsync(api, otra, ct))!.Estado.Should().Be(E.Error);
    }

    private static async Task VencerEsperaAsync(Api.ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        var f = await db.Set<AprovisionamientoEmpresa>().SingleAsync(ct);
        f.ProximoIntentoEn = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync(ct);
    }
}
