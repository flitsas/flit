using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TableroPrueba = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §5.1 fila 1, §5.2, §5.4, §8; ADR-0084) — <c>POST /diagnosticos/{id}/vehiculos/{vid}/tramite</c>,
/// individual y síncrono: permiso <c>diagnostico.derivacion.crear</c> (403 sin él y desde el contexto de backoffice), revalida
/// terminado + VIABLE + sin trámite + sin ítem vivo (422 NO_ELEGIBLE), 409 YA_TIENE_TRAMITE, tipo del lote (Con análisis) o del
/// cuerpo (Simple, validado contra el catálogo), traducción de rechazos (422, ítem OMITIDO) y de status (503 TRAMITES_NO_DISPONIBLE,
/// ítem FALLIDO) e idempotencia por <c>ya_existia</c>. PostgreSQL real (<see cref="ApiDePrueba"/>) y doble del rpc.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DerivacionIndividualTests
/// </code>
/// </example>
public sealed class DerivacionIndividualTests
{
    [Fact]
    public async Task Crea_el_borrador_201_con_el_tipo_del_lote_el_autor_y_la_empresa_y_la_segunda_vez_es_409()
    {
        var falso = new DerivacionTramitesFalso();
        var tramite = Guid.NewGuid();
        falso.Responder = _ => DerivacionTramitesFalso.Creado(tramite, "TR-000777");
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await DerivacionDePrueba.OrganismoAsync(empresa, ct, "99001");
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST411", true, "VIABLE")]);
        var vehiculo = lote.Vehiculos[0];
        var token = DerivacionDePrueba.TokenDerivar(api, empresa);

        var respuesta = await api.Cliente(token).PostAsJsonAsync(DerivacionDePrueba.Ruta(lote.Id, vehiculo.Id), new { tipo_tramite_codigo = "TRASPASO_UNILATERAL" }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("tramite_id").GetGuid().Should().Be(tramite);
        cuerpo.GetProperty("numero").GetString().Should().Be("TR-000777");
        cuerpo.GetProperty("ya_existia").GetBoolean().Should().BeFalse();
        cuerpo.GetProperty("url").GetString().Should().Be($"{DerivacionDePrueba.UrlTramites}/tramites/{tramite}?t={empresa.Id}");

        falso.Llamadas.Should().ContainSingle();
        var (solicitud, metadata, deadline) = falso.Llamadas.Single();
        solicitud.TipoTramiteCodigo.Should().Be("TRASPASO_STANDARD", "en Con análisis manda el tipo del lote, no el del cuerpo");
        solicitud.ExternalRef.Should().Be(vehiculo.Id.ToString());
        solicitud.UsuarioId.Should().Be(TableroPrueba.Usuario(token).ToString());
        solicitud.Placa.Should().Be("TST411");
        solicitud.OrganismoNombreRunt.Should().Be("STRIA TTO PRUEBA");
        solicitud.OrganismoCodigo.Should().Be("99001");
        solicitud.Propietario.NumeroDocumento.Should().Be("99999123", "el documento se descifra solo para el rpc");
        solicitud.DiagnosticoId.Should().Be(lote.Id.ToString());
        metadata!.GetValue("x-flit-tenant-id").Should().Be(empresa.Id.ToString(), "la empresa del lote viaja en la metadata");
        deadline.Should().NotBeNull("el rpc lleva el plazo de Diagnostico:Derivacion:PlazoRpcSegundos");

        var guardado = await DerivacionDePrueba.VehiculoAsync(empresa, vehiculo.Id, ct);
        guardado.TramiteId.Should().Be(tramite);
        guardado.TramiteNumero.Should().Be("TR-000777", "el índice del ZIP (Punto 8) lee el número del vehículo");
        guardado.TramiteCreadoEn.Should().NotBeNull();
        var items = await DerivacionDePrueba.ItemsAsync(empresa, vehiculo.Id, ct);
        items.Should().ContainSingle().Which.Estado.Should().Be(EstadoItemDerivacion.Creado);
        items[0].Intentos.Should().Be(1);
        solicitud.IntentoId.Should().Be(items[0].Id.ToString());
        await using (var db = empresa.Db())
        {
            var op = await db.Set<Flit.Diagnostico.Api.Modulos.Derivacion.OperacionDerivacion>().AsNoTracking().SingleAsync(o => o.Id == items[0].OperacionId, ct);
            op.Tipo.Should().Be(TipoOperacionDerivacion.Individual);
            op.Estado.Should().Be(EstadoOperacionDerivacion.Terminada);
            op.Creadas.Should().Be(1);
            op.UsuarioId.Should().Be(TableroPrueba.Usuario(token));
            op.TipoTramiteCodigo.Should().Be("TRASPASO_STANDARD");
        }

        var otraVez = await api.Cliente(token).PostAsJsonAsync(DerivacionDePrueba.Ruta(lote.Id, vehiculo.Id), new { }, ct);

        otraVez.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflicto = await otraVez.Content.ReadFromJsonAsync<JsonElement>(ct);
        conflicto.GetProperty("code").GetString().Should().Be(MotivoDerivacion.YaTieneTramite);
        conflicto.GetProperty("tramite_id").GetGuid().Should().Be(tramite);
        conflicto.GetProperty("numero").GetString().Should().Be("TR-000777");
        falso.Llamadas.Should().ContainSingle("con trámite no se vuelve a llamar a Trámites");
    }

    [Fact]
    public async Task Ya_existía_en_Trámites_es_éxito_201_con_ya_existia_true()
    {
        var falso = new DerivacionTramitesFalso();
        var tramite = Guid.NewGuid();
        falso.Responder = _ => DerivacionTramitesFalso.Creado(tramite, "TR-000778", yaExistia: true);
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST412", true, "VIABLE")]);

        var respuesta = await api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa)).PostAsync(DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("ya_existia").GetBoolean().Should().BeTrue();
        (await DerivacionDePrueba.VehiculoAsync(empresa, lote.Vehiculos[0].Id, ct)).TramiteId.Should().Be(tramite);
    }

    [Fact]
    public async Task Sin_el_permiso_con_el_rol_de_backoffice_o_desde_el_contexto_de_backoffice_es_403_y_no_llama()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST413", true, "VIABLE")]);
        var ruta = DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id);
        var backoffice = Permisos.RolesPorDefecto.Single(r => r.Codigo == "backoffice_diagnostico").Permisos;

        var sinPermiso = await api.Cliente(api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear])).PostAsync(ruta, null, ct);
        var rolBackoffice = await api.Cliente(api.Token(empresa: empresa.Id, permisos: backoffice, roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT")).PostAsync(ruta, null, ct);
        var contextoBackoffice = await api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa)).PostAsync($"{ruta}?empresa={empresa.Id}", null, ct);

        sinPermiso.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        rolBackoffice.StatusCode.Should().Be(HttpStatusCode.Forbidden, "el rol de backoffice no deriva (R-6)");
        contextoBackoffice.StatusCode.Should().Be(HttpStatusCode.Forbidden, "desde el backoffice no se deriva aunque se tenga el permiso");
        (await contextoBackoffice.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("DERIVACION_DESDE_BACKOFFICE");
        falso.Llamadas.Should().BeEmpty();
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task No_elegible_es_422_NO_ELEGIBLE_con_el_motivo_y_no_llama_a_Trámites()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos:
            [("TST414", true, "BLOQUEADO"), ("TST415", false, null), ("TST416", true, null), ("TST417", true, "VIABLE")]);
        await SembrarItemVivoAsync(empresa, lote, lote.Vehiculos[3].Id, ct);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));

        var motivos = new List<string?>();
        foreach (var v in lote.Vehiculos)
        {
            var r = await cliente.PostAsync(DerivacionDePrueba.Ruta(lote.Id, v.Id), null, ct);
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await r.Content.ReadAsStringAsync(ct));
            var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
            cuerpo.GetProperty("code").GetString().Should().Be("NO_ELEGIBLE");
            cuerpo.GetProperty("motivo").GetString().Should().NotBeNullOrWhiteSpace();
            motivos.Add(cuerpo.GetProperty("motivo_codigo").GetString());
        }

        motivos.Should().Equal(MotivoDerivacion.NoViable, MotivoDerivacion.NoTerminado, MotivoDerivacion.NoViable, MotivoDerivacion.EnCurso);
        falso.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Rechazo_de_Trámites_es_422_con_su_código_el_ítem_queda_OMITIDO_y_la_placa_vuelve_a_ser_elegible()
    {
        var falso = new DerivacionTramitesFalso { Responder = _ => DerivacionTramitesFalso.Rechazo("OT_NO_HABILITADO", "El OT no está en la lista de la compañía.") };
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST418", true, "VIABLE")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var ruta = DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id);

        var respuesta = await cliente.PostAsync(ruta, null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("code").GetString().Should().Be("NO_ELEGIBLE");
        cuerpo.GetProperty("motivo_codigo").GetString().Should().Be("OT_NO_HABILITADO");
        cuerpo.GetProperty("motivo").GetString().Should().Be("El OT no está en la lista de la compañía.");
        var item = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Should().ContainSingle().Subject;
        item.Estado.Should().Be(EstadoItemDerivacion.Omitido);
        item.MotivoCodigo.Should().Be("OT_NO_HABILITADO");
        (await DerivacionDePrueba.VehiculoAsync(empresa, lote.Vehiculos[0].Id, ct)).TramiteId.Should().BeNull();

        falso.Responder = _ => DerivacionTramitesFalso.Creado(Guid.NewGuid(), "TR-000779");
        (await cliente.PostAsync(ruta, null, ct)).StatusCode.Should().Be(HttpStatusCode.Created, "un ítem OMITIDO no ocupa ux_derivaciones_vehiculo_vivo");
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Trámites_caído_es_503_con_Retry_After_y_el_ítem_queda_FALLIDO_y_mal_configurado_503_sin_Retry_After()
    {
        var falso = new DerivacionTramitesFalso { Falla = StatusCode.Unavailable };
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST419", true, "VIABLE")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var ruta = DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id);

        var caido = await cliente.PostAsync(ruta, null, ct);

        caido.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        caido.Headers.RetryAfter.Should().NotBeNull();
        (await caido.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(MotivoDerivacion.TramitesNoDisponible);
        var item = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Should().ContainSingle().Subject;
        item.Estado.Should().Be(EstadoItemDerivacion.Fallido);
        item.MotivoCodigo.Should().Be(MotivoDerivacion.TramitesNoDisponible);

        falso.Falla = StatusCode.PermissionDenied;
        var malConfigurado = await cliente.PostAsync(ruta, null, ct);

        malConfigurado.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "FALLIDO no ocupa el índice: se puede volver a intentar");
        malConfigurado.Headers.RetryAfter.Should().BeNull("un error de configuración no se arregla esperando");
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Should().HaveCount(2).And.OnlyContain(i => i.Estado == EstadoItemDerivacion.Fallido);
    }

    [Fact]
    public async Task Lote_Simple_exige_el_tipo_en_el_cuerpo_y_lo_valida_contra_el_catálogo()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, modo: ModoDiagnostico.Simple, vehiculos: [("TST420", true, null)]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var ruta = DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id);

        var sinTipo = await cliente.PostAsync(ruta, null, ct);
        var desconocido = await cliente.PostAsJsonAsync(ruta, new { tipo_tramite_codigo = "MATRICULA_INICIAL" }, ct);
        var conTipo = await cliente.PostAsJsonAsync(ruta, new { tipo_tramite_codigo = "TRASPASO_UNILATERAL" }, ct);

        sinTipo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await sinTipo.Content.ReadFromJsonAsync<JsonElement>(ct);
        error.GetProperty("code").GetString().Should().Be("TIPO_TRAMITE_REQUERIDO");
        error.GetProperty("campo").GetString().Should().Be("tipo_tramite_codigo");
        desconocido.StatusCode.Should().Be(HttpStatusCode.BadRequest, "MATRICULA_INICIAL no está en la lista blanca de la etapa");
        (await desconocido.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("TIPO_TRAMITE_INVALIDO");
        conTipo.StatusCode.Should().Be(HttpStatusCode.Created, await conTipo.Content.ReadAsStringAsync(ct));
        falso.Llamadas.Should().ContainSingle().Which.Solicitud.TipoTramiteCodigo.Should().Be("TRASPASO_UNILATERAL");
    }

    [Fact]
    public async Task Lote_de_otra_compañía_o_vehículo_de_otro_lote_es_404()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var deA = await DerivacionDePrueba.SembrarAsync(empresas.A, ct, vehiculos: [("TST421", true, "VIABLE")]);
        var deB = await DerivacionDePrueba.SembrarAsync(empresas.B, ct, vehiculos: [("TST422", true, "VIABLE")]);
        var otroDeA = await DerivacionDePrueba.SembrarAsync(empresas.A, ct, vehiculos: [("TST423", true, "VIABLE")]);
        using var clienteB = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresas.B));
        using var clienteA = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresas.A));

        (await clienteB.PostAsync(DerivacionDePrueba.Ruta(deA.Id, deA.Vehiculos[0].Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clienteA.PostAsync(DerivacionDePrueba.Ruta(deA.Id, otroDeA.Vehiculos[0].Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clienteA.PostAsync(DerivacionDePrueba.Ruta(deA.Id, deB.Vehiculos[0].Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        falso.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Apagada_el_endpoint_no_existe()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, habilitada: false);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST424", true, "VIABLE")]);

        var respuesta = await api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa)).PostAsync(DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, "Diagnostico:Derivacion:Habilitada=false (por defecto) no mapea la derivación");
        falso.Llamadas.Should().BeEmpty();
    }

    /// <summary>Un ítem ENVIADO de una operación individual en curso (doble clic o masivo en vuelo).</summary>
    internal static async Task SembrarItemVivoAsync(EmpresaDePrueba empresa, Tablero.LoteSembrado lote, Guid vehiculo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        ArgumentNullException.ThrowIfNull(lote);
        var ahora = DateTimeOffset.UtcNow;
        var op = Flit.Diagnostico.Api.Modulos.Derivacion.OperacionDerivacion.Individual(empresa.Id, lote.Id, "TRASPASO_STANDARD", Guid.NewGuid(), ahora);
        await using var db = empresa.Db();
        db.Add(op);
        db.Add(Flit.Diagnostico.Api.Modulos.Derivacion.ItemDerivacion.Enviado(op, vehiculo, orden: 1, ahora));
        await db.SaveChangesAsync(ct);
    }
}
