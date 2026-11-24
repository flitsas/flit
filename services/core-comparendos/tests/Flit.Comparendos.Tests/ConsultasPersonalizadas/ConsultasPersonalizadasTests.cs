using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Tests.Api;
using Flit.Queries.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using F = Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas.ComparendoQueryFieldCatalog;

namespace Flit.Comparendos.Tests.ConsultasPersonalizadas;

/// <summary>Reportes 2.0 — Consultas personalizadas de Comparendos (CONTRATO §3–§5), contra Postgres real.</summary>
public sealed class ConsultasPersonalizadasTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string Ruta = "/api/v1/comparendos/reportes/consultas";
    private static readonly DateOnly Hoy = new(2026, 10, 9);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private static UsuarioDePrueba Gestora(Guid empresa) => new(empresa, [Permisos.ReportesConsultas]);

    // ── Datos ─────────────────────────────────────────────────────────────────────────────────

    private static Comparendo Nuevo(Guid tenant, string numero, Action<Comparendo>? ajustar = null)
    {
        var c = new Comparendo
        {
            Id = Guid.NewGuid(), TenantId = tenant, Numero = numero, NumeroNormalizado = numero, TipoIngreso = TipoIngreso.SimitNit,
            Placa = "ABC124", FechaImposicion = new DateOnly(2026, 9, 28), FechaNotificacion = new DateOnly(2026, 10, 1), EsFotomulta = true,
            CodigoInfraccion = "C29", DescripcionInfraccion = "Exceso de velocidad", TipoVehiculo = "AUTOMOVIL", ServicioVehiculo = "PARTICULAR",
            ValorCapital = 600000m, ValorAdicional = 4100m, ValorTotal = 604100m,
            FechaLimiteGestion = new DateOnly(2026, 10, 16), FechaFinDescuento50 = new DateOnly(2026, 10, 16), FechaFinDescuento25 = new DateOnly(2026, 11, 6),
            FechaCaducidad = new DateOnly(2027, 10, 1), FechaPrescripcion = new DateOnly(2029, 10, 1),
            CreadoEn = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero), UltimoEventoEn = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero),
        };
        ajustar?.Invoke(c);
        return c;
    }

    private static DerechoPeticion Dp(Comparendo c, Guid tipologia, string radicado, Action<DerechoPeticion>? ajustar = null)
    {
        var d = new DerechoPeticion
        {
            Id = Guid.NewGuid(), TenantId = c.TenantId, ComparendoId = c.Id, TipologiaId = tipologia, PlantillaVersionId = Guid.NewGuid(), Radicado = radicado,
            OrganismoTransitoId = c.OrganismoTransitoId ?? Guid.NewGuid(), DocumentoArchivoId = Guid.NewGuid(), Estado = EstadoDp.Enviado,
            FechaRadicacion = new DateOnly(2026, 10, 5), EnviadoPor = Guid.NewGuid(), CreadoEn = c.CreadoEn,
        };
        ajustar?.Invoke(d);
        return d;
    }

    private static Pago Pago(Comparendo c, TipoPago tipo) => new()
    {
        Id = Guid.NewGuid(), TenantId = c.TenantId, ComparendoId = c.Id, TipoPago = tipo, ValorPagado = 302050m, FechaPago = new DateOnly(2026, 10, 6),
        Origen = OrigenPago.Manual, RegistradoPor = Guid.NewGuid(), RegistradoEn = c.CreadoEn,
    };

    private Task GuardarAsync(params object[] entidades) => api.EnDbAsync(async db =>
    {
        db.AddRange(entidades);
        await db.SaveChangesAsync(Ct);
    });

    private async Task<Guid> EmpresaAsync(string nombre)
    {
        var empresa = await ComparendosDePrueba.EmpresaAsync(api, reportes: true);
        await api.EnDbAsync(async db =>
        {
            var config = await db.ConfiguracionesEmpresa.SingleAsync(e => e.TenantId == empresa, Ct);
            config.EmpresaNombre = nombre;
            await db.SaveChangesAsync(Ct);
        });
        return empresa;
    }

    private static object Definicion(IEnumerable<object> condiciones, string campo = F.FechaImposicion, string preset = QueryRangePreset.Personalizado,
        DateOnly? desde = null, DateOnly? hasta = null) => new
    {
        fechas = new { campo, preset, from = desde ?? new DateOnly(2026, 9, 1), to = hasta ?? Hoy },
        condiciones,
        columnas = Array.Empty<string>(),
    };

    private static object Es(string campo, params string[] valores) => new { fieldId = campo, @operator = QueryOperator.EsAlguno, values = valores };

    private static async Task<JsonElement> CorrerAsync(HttpClient cliente, object definicion, string query = "")
    {
        var r = await cliente.PostAsJsonAsync($"{Ruta}/run{query}", definicion, Ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(Ct));
        return await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<string> Numeros(JsonElement resultado) =>
        resultado.GetProperty("filas").EnumerateArray().Select(f => f.GetProperty("numero").GetString()!).ToList();

    // ── 1. Cada campo filtra ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TodoCampoDelCatalogo_FiltraDeVerdad()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(Hoy);
        var (e1, e2) = (await EmpresaAsync("Gestora Uno"), await EmpresaAsync("Gestora Dos"));
        var organismos = await api.EnDbAsync(db => db.OrganismosTransito.Where(o => o.Departamento != null && o.Municipio != null).OrderBy(o => o.Codigo).Take(200).ToListAsync(Ct));
        var oa = organismos[0];
        var ob = organismos.First(o => o.Departamento != oa.Departamento && o.Municipio != oa.Municipio);
        var tipologias = await api.EnDbAsync(db => db.Tipologias.OrderBy(t => t.Codigo).Select(t => t.Id).Take(2).ToListAsync(Ct));
        var nitA = new IdentificacionMonitoreada { Id = Guid.NewGuid(), TenantId = e1, NumeroBase = "800111222", NombreTitular = "UNO S.A.S." };
        var nitB = new IdentificacionMonitoreada { Id = Guid.NewGuid(), TenantId = e2, NumeroBase = "800333444", NombreTitular = "DOS S.A.S." };

        // A y B difieren en TODOS los campos del catálogo.
        var a = Nuevo(e1, "11001000000000000001", c =>
        {
            (c.Placa, c.IdentificacionMonitoreadaId, c.OrganismoTransitoId, c.CodigoInfraccion) = ("AAA111", nitA.Id, oa.Id, "C29");
            (c.EnCobroCoactivo, c.Gestionado, c.Pagado, c.TieneDp, c.NotificadoFisicamente) = (true, true, true, true, true);
            c.EstadoGestion = EstadoGestion.DpEnviado;
            c.EstadoNotificacionConductor = EstadoNotificacionConductor.Aviso2;
            c.EstadoCambioContraventor = EstadoCambioContraventor.AceptadoOt;
            c.EstadoConsultaContraventor = EstadoConsultaContraventor.Encontrado;
        });
        var b = Nuevo(e2, "11001000000000000002", c =>
        {
            (c.Placa, c.IdentificacionMonitoreadaId, c.OrganismoTransitoId, c.CodigoInfraccion) = ("BBB222", nitB.Id, ob.Id, "D02");
            (c.EsFotomulta, c.TipoVehiculo, c.ServicioVehiculo) = (false, "MOTOCICLETA", "PUBLICO");
            (c.Activo, c.MotivoInactivacion) = (false, MotivoInactivacion.InactivadoOperador);
            // Vencido (pasó la gestión, no la caducidad) y sin descuento.
            (c.FechaLimiteGestion, c.FechaFinDescuento50, c.FechaFinDescuento25) = (new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 3));
        });
        await GuardarAsync(nitA, nitB, a, b,
            Dp(a, tipologias[0], "GDC-261005-0001", d => (d.Estado, d.RespuestaSentido, d.RespuestaFecha, d.EnvioAutomatico) = (EstadoDp.Respondido, SentidoRespuestaOt.Favorable, new DateOnly(2026, 10, 7), true)),
            Dp(b, tipologias[1], "GDC-261005-0002", d => d.RespuestaSentido = SentidoRespuestaOt.Desfavorable),
            Pago(a, TipoPago.CC), Pago(b, TipoPago.BC));

        var valorDeA = new Dictionary<string, string>
        {
            [F.Numero] = a.Numero, [F.Placa] = "aaa-111", [F.Nit] = nitA.Id.ToString(), [F.Empresa] = e1.ToString(),
            [F.CodigoInfraccion] = "c29", [F.Fotomulta] = "true", [F.TipoVehiculo] = "AUTOMOVIL", [F.ServicioVehiculo] = "PARTICULAR",
            [F.Organismo] = oa.Id.ToString(), [F.Departamento] = oa.Departamento!, [F.Municipio] = oa.Municipio!,
            [F.Activo] = "true", [F.EstadoLegal] = "EnTiempo", [F.VentanaDescuento] = "Descuento50", [F.CobroCoactivo] = "true",
            [F.Gestionado] = "true", [F.Pagado] = "true", [F.EstadoGestion] = "DpEnviado", [F.TieneDp] = "true",
            [F.DpEstado] = "Respondido", [F.DpSentido] = "Favorable", [F.DpTipologia] = tipologias[0].ToString(), [F.DpAutomatico] = "true",
            [F.DpRadicado] = "GDC261005-0001", [F.NotificacionConductor] = "Aviso2", [F.CambioContraventor] = "AceptadoOt",
            [F.ConsultaContraventor] = "Encontrado", [F.NotificadoFisicamente] = "true", [F.PagoTipo] = "CC",
        };
        F.TodasInstance.Fields.Select(f => f.Id).Should().BeEquivalentTo(valorDeA.Keys, "un campo nuevo del catálogo necesita su caso aquí");

        foreach (var (campo, valor) in valorDeA)
        {
            var r = await CorrerAsync(Super, Definicion([Es(campo, valor)]));
            Numeros(r).Should().Contain(a.Numero, $"«{campo}» = {valor} debe traer A").And.NotContain(b.Numero, $"«{campo}» = {valor} no debe traer B");
        }

        // REX: el validador acepta «TRUE» o «entiempo»; lo empujado a la base tiene que entenderlo igual que la memoria.
        Numeros(await CorrerAsync(Super, Definicion([Es(F.Pagado, "TRUE")]))).Should().Contain(a.Numero).And.NotContain(b.Numero);
        Numeros(await CorrerAsync(Super, Definicion([Es(F.EstadoLegal, "entiempo")]))).Should().Contain(a.Numero).And.NotContain(b.Numero);
        Numeros(await CorrerAsync(Super, Definicion([Es(F.VentanaDescuento, "descuento50")]))).Should().Contain(a.Numero).And.NotContain(b.Numero);

        // GIA: sin fecha de caducidad el motor dice «Vencido»; la base no puede descartarlo antes.
        var sinCaducidad = Nuevo(e1, "11001000000000000003", c => (c.FechaLimiteGestion, c.FechaCaducidad, c.FechaPrescripcion) = (new DateOnly(2026, 10, 2), null, null));
        await GuardarAsync(sinCaducidad);
        Numeros(await CorrerAsync(Super, Definicion([Es(F.EstadoLegal, "Vencido")]))).Should().Contain([sinCaducidad.Numero, b.Numero]).And.NotContain(a.Numero);

        // Y cada fecha del rango filtra por la suya (A cae en el rango; B se corre fuera en esa fecha).
        var fila = (await CorrerAsync(Super, Definicion([Es(F.Numero, a.Numero)]))).GetProperty("filas")[0];
        (fila.GetProperty("estadoLegal").GetString(), fila.GetProperty("ventanaDescuento").GetString(), fila.GetProperty("dpTipologia").GetString())
            .Should().Be(("EnTiempo", "Descuento50", await api.EnDbAsync(db => db.Tipologias.Where(t => t.Id == tipologias[0]).Select(t => t.Nombre).SingleAsync(Ct))));
        (fila.GetProperty("empresaNombre").GetString(), fila.GetProperty("nitMonitoreado").GetString(), fila.GetProperty("pagoTipo").GetString())
            .Should().Be(("Gestora Uno", "800111222", "CC"));
        foreach (var fecha in F.DateFieldOptions.Select(o => o.Value))
        {
            var dentro = await CorrerAsync(Super, Definicion([Es(F.Numero, a.Numero)], fecha, desde: new DateOnly(2026, 9, 1)));
            var fuera = await CorrerAsync(Super, Definicion([Es(F.Numero, a.Numero)], fecha, desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 12, 31)));
            Numeros(dentro).Should().Equal([a.Numero], $"la fecha «{fecha}» de A cae en el rango");
            Numeros(fuera).Should().BeEmpty($"la fecha «{fecha}» de A queda fuera de 2025");
        }
    }

    // ── 2. Aislamiento ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aislamiento_UnaGestoraNoVeOtra_YElSuperAdminConTodasQuedaAnotado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(Hoy);
        var (e1, e2) = (await EmpresaAsync("Aislada Uno"), await EmpresaAsync("Aislada Dos"));
        var (uno, dos) = (Nuevo(e1, "22001000000000000001", c => c.Placa = "ISO111"), Nuevo(e2, "22001000000000000002", c => c.Placa = "ISO111"));
        await GuardarAsync(uno, dos);
        var gestora = api.Cliente(Gestora(e1));

        var propia = await CorrerAsync(gestora, Definicion([Es(F.Placa, "ISO111")]));
        Numeros(propia).Should().Equal(uno.Numero);
        propia.GetProperty("filas")[0].GetProperty("empresaId").ValueKind.Should().Be(JsonValueKind.Null, "empresa* solo existe para el SuperAdmin");
        var otra = await gestora.PostAsJsonAsync($"{Ruta}/run?empresa={e2}", Definicion([]), Ct);
        otra.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await otra.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("OTRA_EMPRESA");
        var porEmpresa = await gestora.PostAsJsonAsync($"{Ruta}/run", Definicion([Es(F.Empresa, e2.ToString())]), Ct);
        porEmpresa.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await porEmpresa.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("CONSULTA_INVALIDA");
        var campos = await gestora.GetFromJsonAsync<JsonElement>($"{Ruta}/fields", Ct);
        campos.EnumerateArray().Select(f => f.GetProperty("id").GetString()).Should().NotContain(F.Empresa);
        campos.EnumerateArray().Single(f => f.GetProperty("id").GetString() == F.Placa).GetProperty("label").GetString().Should().Be("Placa");

        // La cobertura no delata comparendos de otra empresa: para la gestora 1, la placa de la 2 «no existe».
        await GuardarAsync(Nuevo(e2, "22001000000000000003", c => (c.Placa, c.FechaImposicion) = ("OTR222", new DateOnly(2024, 1, 1))));
        var ajena = await CorrerAsync(gestora, Definicion([Es(F.Placa, "OTR222")]));
        ajena.GetProperty("cobertura")[0].GetProperty("resultado").GetString().Should().Be(QueryCoverageResult.NoExiste);

        // Periodo anterior: número en la primera página, null en las siguientes (el export no lo necesita).
        propia.GetProperty("totalPeriodoAnterior").GetInt32().Should().Be(0);
        (await CorrerAsync(gestora, Definicion([Es(F.Placa, "ISO111")]), "?page=2")).GetProperty("totalPeriodoAnterior").ValueKind.Should().Be(JsonValueKind.Null);
        var rango = await gestora.PostAsJsonAsync($"{Ruta}/run", Definicion([], hasta: new DateOnly(9999, 12, 31)), Ct);
        rango.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "un rango absurdo es un 422, no un 500");

        // Sin el módulo de reportes o sin el permiso, nada.
        var sinModulo = await ComparendosDePrueba.EmpresaAsync(api);
        (await api.Cliente(Gestora(sinModulo)).GetAsync($"{Ruta}/fields", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Cliente(new UsuarioDePrueba(e1, [Permisos.ReportesVer])).GetAsync($"{Ruta}/fields", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // El SuperAdmin con «todas» ve las dos, con su empresa, y la lectura de cada una queda en la auditoría.
        var superAdmin = new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true);
        var todas = await CorrerAsync(api.Cliente(superAdmin), Definicion([Es(F.Placa, "ISO111")]));
        todas.GetProperty("filas").EnumerateArray().Select(f => f.GetProperty("empresaId").GetGuid()).Should().BeEquivalentTo([e1, e2]);
        var anotadas = await api.EnDbAsync(db => db.AuditoriasConfiguracion
            .Where(x => x.Entidad == "lectura_superadmin" && x.UsuarioId == superAdmin.Id && x.EntidadId.Contains("/reportes/consultas/run"))
            .Select(x => x.TenantId!.Value).ToListAsync(Ct));
        anotadas.Should().BeEquivalentTo([e1, e2]);
        (await api.Cliente(superAdmin).GetFromJsonAsync<JsonElement>($"{Ruta}/fields", Ct)).EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == F.Empresa).GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("label").GetString()).Should().Contain(["Aislada Uno", "Aislada Dos"]);
    }

    // ── 3. Cobertura con universo acotado ─────────────────────────────────────────────────────

    [Fact]
    public async Task Cobertura_FueraDelRango_FrenteA_NoExiste_ConUniversoAcotado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(Hoy);
        var empresa = await EmpresaAsync("Cobertura");
        var enRango = Nuevo(empresa, "33001000000000000001");
        var viejo = Nuevo(empresa, "33001000000000000002", c => c.FechaImposicion = new DateOnly(2024, 1, 10));
        var pagado = Nuevo(empresa, "33001000000000000003", c => c.Pagado = true);
        await GuardarAsync(enRango, viejo, pagado);

        // El número del rango se pide con prefijo y guiones, como lo pegan de Excel.
        var r = await CorrerAsync(api.Cliente(Gestora(empresa)), Definicion(
            [Es(F.Numero, "D-3300-1000000000000001", viejo.Numero, pagado.Numero, "99999999999999999999"), Es(F.Pagado, "false")],
            preset: QueryRangePreset.Ultimos30));

        Numeros(r).Should().Equal(enRango.Numero);
        var cobertura = r.GetProperty("cobertura").EnumerateArray().ToDictionary(i => i.GetProperty("valor").GetString()!);
        cobertura["D-3300-1000000000000001"].GetProperty("resultado").GetString().Should().Be(QueryCoverageResult.Encontrado, "la cobertura devuelve lo que escribió el usuario");
        cobertura[viejo.Numero].GetProperty("resultado").GetString().Should().Be(QueryCoverageResult.Excluido);
        cobertura[viejo.Numero].GetProperty("motivoCampo").GetString().Should().Be(F.FechaImposicion);
        cobertura[viejo.Numero].GetProperty("motivo").GetString().Should().Be("Existe, pero su fecha de imposición queda fuera del rango.");
        cobertura[pagado.Numero].GetProperty("motivoCampo").GetString().Should().Be(F.Pagado);
        cobertura["99999999999999999999"].GetProperty("resultado").GetString().Should().Be(QueryCoverageResult.NoExiste);
        cobertura["99999999999999999999"].GetProperty("motivo").GetString().Should().Be("No hay ningún comparendo con este valor en su empresa.");
    }

    [Fact]
    public async Task Cobertura_UnaPlacaConMuchasFilas_NoTapaALasDemas()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(Hoy);
        var empresa = await EmpresaAsync("Muchas filas");
        var viejo = new DateOnly(2024, 3, 1);
        await api.EnDbAsync(async db =>
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Comparendos.AddRange(Enumerable.Range(0, 600).Select(i => Nuevo(empresa, $"66{i:D18}", c => (c.Placa, c.FechaImposicion) = ("MUC111", viejo))));
            // La otra placa queda de última por id: con un tope sobre todas las filas, la primera se lo comería.
            db.Comparendos.Add(Nuevo(empresa, "67000000000000000001", c => (c.Id, c.Placa, c.FechaImposicion) = (Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff"), "ULT999", viejo)));
            await db.SaveChangesAsync(Ct);
        });

        var r = await CorrerAsync(api.Cliente(Gestora(empresa)), Definicion([Es(F.Placa, "MUC111", "ULT999")], preset: QueryRangePreset.Ultimos30));
        r.GetProperty("cobertura").EnumerateArray().Select(i => (i.GetProperty("valor").GetString(), i.GetProperty("resultado").GetString()))
            .Should().BeEquivalentTo([("MUC111", QueryCoverageResult.Excluido), ("ULT999", QueryCoverageResult.Excluido)]);
    }

    // ── 4. Tope ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PasarElTope_Responde422_ConsultaDemasiadoAmplia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(Hoy);
        var empresa = await EmpresaAsync("Muy grande");
        await api.EnDbAsync(async db =>
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Comparendos.AddRange(Enumerable.Range(0, QueryLimits.MaxUniverso + 1)
                .Select(i => Nuevo(empresa, $"44{i:D18}", c => (c.FechaImposicion, c.Pagado) = (new DateOnly(2025, 1, 1).AddDays(i % 365), i % 2 == 0))));
            await db.SaveChangesAsync(Ct);
        });
        var gestora = api.Cliente(Gestora(empresa));

        var amplia = await gestora.PostAsJsonAsync($"{Ruta}/run", Definicion([], desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 12, 31)), Ct);
        amplia.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await amplia.Content.ReadFromJsonAsync<JsonElement>(Ct);
        error.GetProperty("code").GetString().Should().Be("CONSULTA_DEMASIADO_AMPLIA");
        error.GetProperty("error").GetString().Should().Contain("comparendos").And.EndWith("Acótala eligiendo un NIT o un rango de fecha más corto.");

        // GIA: sin identificadores, la condición se empuja a la base y baja el universo del tope (lo que deja abrir Renting).
        (await CorrerAsync(gestora, Definicion([Es(F.Pagado, "false")], desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 12, 31))))
            .GetProperty("total").GetInt32().Should().Be(QueryLimits.MaxUniverso / 2);

        // Un mes cabe, y un identificador acota aunque el rango sea de todo el año.
        (await CorrerAsync(gestora, Definicion([], desde: new DateOnly(2025, 3, 1), hasta: new DateOnly(2025, 3, 31)))).GetProperty("total").GetInt32().Should().Be(31 * 55, "20.001 fechas repartidas en 365 días: 55 por cada día de marzo");
        Numeros(await CorrerAsync(gestora, Definicion([Es(F.Numero, $"44{7:D18}")], desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 12, 31))))
            .Should().Equal($"44{7:D18}");
    }

    // ── 5. Guardadas ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guardadas_CrudPorUsuario_YLasDeFabricaNoSeBorran()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(Hoy);
        var empresa = await EmpresaAsync("Guardadas");
        var yo = api.Cliente(Gestora(empresa));
        var otro = api.Cliente(Gestora(empresa));

        var inicial = await yo.GetFromJsonAsync<JsonElement>($"{Ruta}/saved", Ct);
        inicial.EnumerateArray().Select(q => q.GetProperty("nombre").GetString()).Should().Equal(ConsultasFabrica.Todas.Select(q => q.Nombre));
        inicial.EnumerateArray().Should().OnlyContain(q => q.GetProperty("deFabrica").GetBoolean());

        var creada = await (await yo.PostAsJsonAsync($"{Ruta}/saved", new { nombre = "Mis placas", definition = Definicion([Es(F.Placa, "ABC124")]) }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = creada.GetProperty("id").GetGuid();
        creada.GetProperty("deFabrica").GetBoolean().Should().BeFalse();
        var lista = await yo.GetFromJsonAsync<JsonElement>($"{Ruta}/saved", Ct);
        lista.EnumerateArray().Last().GetProperty("id").GetGuid().Should().Be(id, "primero las de fábrica, luego las del usuario");
        (await otro.GetFromJsonAsync<JsonElement>($"{Ruta}/saved", Ct)).GetArrayLength().Should().Be(ConsultasFabrica.Todas.Count, "es por usuario");

        var editada = await (await yo.PostAsJsonAsync($"{Ruta}/saved", new { id, nombre = "Mis placas", descripcion = "nota", definition = Definicion([]) }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        (editada.GetProperty("id").GetGuid(), editada.GetProperty("descripcion").GetString()).Should().Be((id, "nota"));
        var repetida = await yo.PostAsJsonAsync($"{Ruta}/saved", new { nombre = "MIS PLACAS", definition = Definicion([]) }, Ct);
        repetida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await repetida.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("NOMBRE_REPETIDO");
        var invalida = await yo.PostAsJsonAsync($"{Ruta}/saved", new { nombre = "Mala", definition = Definicion([Es("no_existe", "x")]) }, Ct);
        (await invalida.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("CONSULTA_INVALIDA");

        // Guardar sobre una de fábrica la duplica; borrarla da 409.
        var copia = await (await yo.PostAsJsonAsync($"{Ruta}/saved", new { id = ConsultasFabrica.SinGestion, nombre = "Sin gestión (mía)", definition = Definicion([]) }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        copia.GetProperty("id").GetGuid().Should().NotBe(ConsultasFabrica.SinGestion);
        var fabrica = await yo.DeleteAsync($"{Ruta}/saved/{ConsultasFabrica.SinGestion}", Ct);
        fabrica.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await fabrica.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("CONSULTA_DE_FABRICA");

        (await otro.DeleteAsync($"{Ruta}/saved/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "no es suya");
        (await yo.DeleteAsync($"{Ruta}/saved/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await yo.DeleteAsync($"{Ruta}/saved/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Tope de 50 por usuario.
        for (var i = 0; i < QueryLimits.MaxConsultasGuardadas - 1; i++)
            (await otro.PostAsJsonAsync($"{Ruta}/saved", new { nombre = $"Consulta {i}", definition = Definicion([]) }, Ct)).EnsureSuccessStatusCode();
        (await otro.PostAsJsonAsync($"{Ruta}/saved", new { nombre = "La 50", definition = Definicion([]) }, Ct)).EnsureSuccessStatusCode();
        var tope = await otro.PostAsJsonAsync($"{Ruta}/saved", new { nombre = "La 51", definition = Definicion([]) }, Ct);
        tope.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await tope.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("LIMITE_CONSULTAS");
    }

    [Fact]
    public async Task ConsultasDeFabrica_CorrenSinError()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(Hoy);
        var empresa = await EmpresaAsync("Fábrica");
        var c = Nuevo(empresa, "55001000000000000001", x => x.CodigoInfraccion = "D02");
        await GuardarAsync(c);
        var gestora = api.Cliente(Gestora(empresa));
        foreach (var q in (await gestora.GetFromJsonAsync<JsonElement>($"{Ruta}/saved", Ct)).EnumerateArray())
            await CorrerAsync(gestora, q.GetProperty("definition"));
        var guardada = (await gestora.GetFromJsonAsync<JsonElement>($"{Ruta}/saved", Ct)).EnumerateArray()
            .Single(q => q.GetProperty("id").GetGuid() == ConsultasFabrica.InfraccionesC35D02);
        Numeros(await CorrerAsync(gestora, guardada.GetProperty("definition"))).Should().Equal(c.Numero);
    }

    // ── 6. Siembra ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Siembra_EsIdempotente_YNoPisaLoQueYaExiste()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await api.EnDbAsync(async db =>
        {
            // El arranque ya sembró una vez; dos veces más no duplican.
            await SiembraConsultasFabrica.SembrarAsync(db, DateTimeOffset.UtcNow, Ct);
            await SiembraConsultasFabrica.SembrarAsync(db, DateTimeOffset.UtcNow, Ct);
            (await db.ConsultasGuardadas.CountAsync(q => q.DeFabrica, Ct)).Should().Be(8);
            (await db.ConsultasGuardadas.Where(q => q.DeFabrica).AllAsync(q => q.TenantId == null && q.UsuarioId == null, Ct)).Should().BeTrue();

            var una = await db.ConsultasGuardadas.SingleAsync(q => q.Id == ConsultasFabrica.DpSinRespuesta, Ct);
            una.Descripcion = "Ajustada a mano";
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            await SiembraConsultasFabrica.SembrarAsync(db, DateTimeOffset.UtcNow, Ct);
            (await db.ConsultasGuardadas.SingleAsync(q => q.Id == ConsultasFabrica.DpSinRespuesta, Ct)).Descripcion.Should().Be("Ajustada a mano");
        });
    }
}
