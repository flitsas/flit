using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;
using Flit.Comparendos.Api.Modulos.Reportes;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Tests.Api;
using Flit.Queries.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas.ComparendoQueryFieldCatalog;

namespace Flit.Comparendos.Tests.ConsultasPersonalizadas;

/// <summary>Reportes 2.0, ronda 2 — envío programado por correo de una consulta guardada (CONTRATO §4 /programaciones).</summary>
public sealed class ProgramacionesTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string Ruta = "/api/v1/comparendos/reportes/consultas";
    private static readonly DateOnly Hoy = new(2026, 10, 9);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UsuarioDePrueba Admin(Guid empresa) => new(empresa, [Permisos.ReportesConsultas, Permisos.ReportesProgramar]);

    /// <summary>Instante de Colombia (UTC-5) en UTC.</summary>
    private static DateTimeOffset Col(int anio, int mes, int dia, int hora, int minuto = 0) =>
        new DateTimeOffset(anio, mes, dia, hora, minuto, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    // ── Próxima ejecución ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProximaEjecucion_SemanalIsoYMensualConCruceDeMesYDeAnio()
    {
        // Viernes 9-oct-2026, 10:00 de Colombia.
        var ahora = Col(2026, 10, 9, 10);
        Programaciones.Proxima(Programaciones.Semanal, 7, null, new TimeOnly(8, 0), ahora).Should().Be(Col(2026, 10, 11, 8), "7 es domingo (ISO), no el lunes");
        Programaciones.Proxima(Programaciones.Semanal, 1, null, new TimeOnly(8, 0), ahora).Should().Be(Col(2026, 10, 12, 8));
        Programaciones.Proxima(Programaciones.Semanal, 5, null, new TimeOnly(18, 0), ahora).Should().Be(Col(2026, 10, 9, 18), "hoy, más tarde");
        Programaciones.Proxima(Programaciones.Semanal, 5, null, new TimeOnly(9, 0), ahora).Should().Be(Col(2026, 10, 16, 9), "hoy ya pasó: la otra semana");
        Programaciones.Proxima(Programaciones.Mensual, null, 28, new TimeOnly(7, 30), Col(2026, 10, 30, 12)).Should().Be(Col(2026, 11, 28, 7, 30));
        Programaciones.Proxima(Programaciones.Mensual, null, 28, new TimeOnly(7, 30), Col(2026, 12, 29, 12)).Should().Be(Col(2027, 1, 28, 7, 30), "cruza el año");
        Programaciones.Proxima(Programaciones.Mensual, null, 9, new TimeOnly(23, 0), ahora).Should().Be(Col(2026, 10, 9, 23), "este mes todavía");
        // 23:00 de Colombia es el día siguiente en UTC: la cuenta se hace en Colombia.
        Programaciones.Proxima(Programaciones.Semanal, 6, null, new TimeOnly(1, 0), Col(2026, 10, 9, 23)).Should().Be(Col(2026, 10, 10, 1));
    }

    // ── Endpoints ─────────────────────────────────────────────────────────────────────────────

    private async Task<(Guid Empresa, HttpClient Cliente, Guid Consulta)> EmpresaConConsultaAsync(string nombre, object? definicion = null)
    {
        api.Reloj.Hoy(Hoy);
        var empresa = await ComparendosDePrueba.EmpresaAsync(api, reportes: true);
        await api.EnDbAsync(async db =>
        {
            (await db.ConfiguracionesEmpresa.SingleAsync(e => e.TenantId == empresa, Ct)).EmpresaNombre = nombre;
            await db.SaveChangesAsync(Ct);
        });
        var cliente = api.Cliente(Admin(empresa));
        var guardada = await (await cliente.PostAsJsonAsync($"{Ruta}/saved", new
        {
            nombre = $"Mi consulta {nombre}",
            definition = definicion ?? new
            {
                fechas = new { campo = F.FechaImposicion, preset = QueryRangePreset.Personalizado, from = new DateOnly(2026, 9, 1), to = Hoy },
                condiciones = Array.Empty<object>(),
                columnas = new[] { "numero", "estadoLegal", "ventanaDescuento", "pagado", "valorTotal" },
            },
        }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (empresa, cliente, guardada.GetProperty("id").GetGuid());
    }

    private static object Semanal(Guid consulta, params string[] destinatarios) =>
        new { consultaId = consulta, periodicidad = "semanal", diaSemana = 5, diaMes = (int?)null, hora = "08:00", destinatarios, activa = true };

    private static async Task<string?> CodigoAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    [Fact]
    public async Task Programaciones_ValidanYSoloSobreConsultasPropiasDeLaEmpresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, cliente, consulta) = await EmpresaConConsultaAsync("Valida");
        var (_, _, ajena) = await EmpresaConConsultaAsync("Otra");

        var ok = await (await cliente.PostAsJsonAsync($"{Ruta}/programaciones", Semanal(consulta, "Gerencia@Cliente.test"), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        ok.GetProperty("proximaEjecucion").GetDateTimeOffset().Should().Be(Col(2026, 10, 16, 8), "el viernes 9 a las 9:00 ya pasaron las 8:00");
        (ok.GetProperty("hora").GetString(), ok.GetProperty("destinatarios")[0].GetString(), ok.GetProperty("consultaNombre").GetString())
            .Should().Be(("08:00", "gerencia@cliente.test", "Mi consulta Valida"));
        (await cliente.GetFromJsonAsync<JsonElement>($"{Ruta}/programaciones", Ct)).GetArrayLength().Should().Be(1);

        async Task Rechaza(object cuerpo, string codigo, HttpStatusCode estado = HttpStatusCode.UnprocessableEntity)
        {
            var r = await cliente.PostAsJsonAsync($"{Ruta}/programaciones", cuerpo, Ct);
            r.StatusCode.Should().Be(estado);
            (await CodigoAsync(r)).Should().Be(codigo);
        }

        await Rechaza(Semanal(ConsultasFabrica.SinGestion, "a@b.test"), "PROGRAMACION_INVALIDA");
        await Rechaza(Semanal(ajena, "a@b.test"), "PROGRAMACION_INVALIDA");
        await Rechaza(new { consultaId = consulta, periodicidad = "semanal", diaSemana = 5, diaMes = 3, hora = "08:00", destinatarios = new[] { "a@b.test" } }, "PROGRAMACION_INVALIDA");
        await Rechaza(new { consultaId = consulta, periodicidad = "mensual", diaSemana = (int?)null, diaMes = 29, hora = "08:00", destinatarios = new[] { "a@b.test" } }, "PROGRAMACION_INVALIDA");
        await Rechaza(new { consultaId = consulta, periodicidad = "semanal", diaSemana = 8, diaMes = (int?)null, hora = "08:00", destinatarios = new[] { "a@b.test" } }, "PROGRAMACION_INVALIDA");
        await Rechaza(new { consultaId = consulta, periodicidad = "diaria", diaSemana = 1, diaMes = (int?)null, hora = "08:00", destinatarios = new[] { "a@b.test" } }, "PROGRAMACION_INVALIDA");
        await Rechaza(Semanal(consulta, Enumerable.Range(0, 21).Select(i => $"p{i}@b.test").ToArray()), "PROGRAMACION_INVALIDA");
        await Rechaza(Semanal(consulta, "no-es-correo"), "CORREO_INVALIDO");
        await Rechaza(new { consultaId = consulta, periodicidad = "semanal", diaSemana = 5, diaMes = (int?)null, hora = "8 am", destinatarios = new[] { "a@b.test" } }, "PROGRAMACION_INVALIDA");

        // El SuperAdmin ve, pero no programa por la empresa; sin el permiso de programar, nada.
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.GetFromJsonAsync<JsonElement>($"{Ruta}/programaciones?empresa={empresa}", Ct)).GetArrayLength().Should().Be(1);
        (await super.PostAsJsonAsync($"{Ruta}/programaciones", Semanal(consulta, "a@b.test"), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ReportesConsultas])).GetAsync($"{Ruta}/programaciones", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Otra empresa no la borra; la propia sí.
        var id = ok.GetProperty("id").GetGuid();
        var (otraEmpresa, otroCliente, _) = await EmpresaConConsultaAsync("Borra");
        otraEmpresa.Should().NotBe(empresa);
        (await otroCliente.DeleteAsync($"{Ruta}/programaciones/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.DeleteAsync($"{Ruta}/programaciones/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── El trabajo ────────────────────────────────────────────────────────────────────────────

    private async Task CorrerTrabajoAsync()
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().Single(e => e.Atiende(TipoTrabajo.ConsultasProgramadas));
        await ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.ConsultasProgramadas, Disparo.Programado), CancellationToken.None);
    }

    /// <summary>La deja vencida (proxima_ejecucion en el pasado) para que el trabajo la tome ya.</summary>
    private async Task<Guid> ProgramadaVencidaAsync(HttpClient cliente, Guid consulta, string destinatario)
    {
        var p = await (await cliente.PostAsJsonAsync($"{Ruta}/programaciones", Semanal(consulta, destinatario), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = p.GetProperty("id").GetGuid();
        await api.EnDbAsync(db => db.ConsultasProgramadas.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProximaEjecucion, api.Reloj.Ahora.AddMinutes(-1)), Ct));
        return id;
    }

    private static Comparendo Comparendo(Guid tenant, string numero, Action<Comparendo>? ajustar = null)
    {
        var c = new Comparendo
        {
            Id = Guid.NewGuid(), TenantId = tenant, Numero = numero, NumeroNormalizado = numero, TipoIngreso = TipoIngreso.SimitNit, Placa = "PRG123",
            FechaImposicion = new DateOnly(2026, 9, 28), FechaNotificacion = new DateOnly(2026, 10, 1), EsFotomulta = true, ValorTotal = 604100m,
            FechaLimiteGestion = new DateOnly(2026, 10, 16), FechaFinDescuento50 = new DateOnly(2026, 10, 16), FechaFinDescuento25 = new DateOnly(2026, 11, 6),
            FechaCaducidad = new DateOnly(2027, 10, 1), FechaPrescripcion = new DateOnly(2029, 10, 1),
            CreadoEn = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero), UltimoEventoEn = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero),
        };
        ajustar?.Invoke(c);
        return c;
    }

    private async Task<List<List<string>>> HojaAsync(Guid empresa, string hoja)
    {
        var adjunto = await api.EnDbAsync(db => (from c in db.Comunicaciones
                                                 join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId
                                                 join f in db.Archivos on a.ArchivoId equals f.Id
                                                 where c.TenantId == empresa && c.Tipo == TipoComunicacion.ConsultaProgramada
                                                 select f.Referencia).SingleAsync(Ct));
        await using var scope = api.App.Services.CreateAsyncScope();
        var bytes = await scope.ServiceProvider.GetRequiredService<IAlmacenArchivos>().LeerAsync(adjunto, Ct);
        using var libro = SpreadsheetDocument.Open(new MemoryStream(bytes!), false);
        var parte = libro.WorkbookPart!;
        var id = parte.Workbook.Descendants<Sheet>().Single(s => s.Name == hoja).Id!.Value!;
        var compartidas = parte.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
        string Texto(DocumentFormat.OpenXml.Spreadsheet.Cell c) =>
            c.DataType?.Value == CellValues.SharedString ? compartidas[int.Parse(c.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture)]
            : c.InlineString?.InnerText ?? c.CellValue?.Text ?? string.Empty;
        return ((WorksheetPart)parte.GetPartById(id)).Worksheet.Descendants<Row>().Select(r => r.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().Select(Texto).ToList()).ToList();
    }

    [Fact]
    public async Task Trabajo_EnviaCadaUnaConSuEmpresa_UnaSolaVez_YConEtiquetasDeNegocio()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (a, clienteA, consultaA) = await EmpresaConConsultaAsync("Envia A");
        var (b, clienteB, consultaB) = await EmpresaConConsultaAsync("Envia B");
        await api.EnDbAsync(async db =>
        {
            db.Comparendos.AddRange(Comparendo(a, "77001000000000000001"), Comparendo(b, "77001000000000000002", c => c.Pagado = true));
            await db.SaveChangesAsync(Ct);
        });
        var pa = await ProgramadaVencidaAsync(clienteA, consultaA, "samuel.cardenas@flitsas.com");
        var pb = await ProgramadaVencidaAsync(clienteB, consultaB, "samuel.cardenas@flitsas.com");

        await CorrerTrabajoAsync();
        await CorrerTrabajoAsync();

        var correos = await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.Tipo == TipoComunicacion.ConsultaProgramada && (c.TenantId == a || c.TenantId == b)).ToListAsync(Ct));
        correos.Select(c => c.TenantId).Should().BeEquivalentTo([a, b], "una por programación y una sola vez aunque el trabajo corra dos veces");
        correos.Single(c => c.TenantId == a).Asunto.Should().Contain("Mi consulta Envia A").And.Contain("Envia A");

        // El Excel de A solo trae lo de A, con etiquetas de negocio y no valores.
        var filas = await HojaAsync(a, "Comparendos");
        filas[0].Should().Equal("No. comparendo", "Estado legal", "Descuento vigente", "Pagado", "Valor total");
        filas.Skip(1).Select(f => f[0]).Should().Equal("77001000000000000001");
        filas[1].Skip(1).Take(3).Should().Equal("En tiempo para gestionar", "Descuento del 50 % vigente", "No");
        (await HojaAsync(a, "Notas")).Should().Contain(f => f[0] == "Comparendos" && f[1] == "1");

        var estado = await api.EnDbAsync(db => db.ConsultasProgramadas.AsNoTracking().SingleAsync(x => x.Id == pa, Ct));
        (estado.UltimoResultado, estado.ProximaEjecucion > api.Reloj.Ahora, estado.UltimaEjecucion).Should().Be(("Enviado a 1 destinatario", true, api.Reloj.Ahora));
        (await api.EnDbAsync(db => db.ConsultasProgramadas.AsNoTracking().SingleAsync(x => x.Id == pb, Ct))).UltimoResultado.Should().Be("Enviado a 1 destinatario");
    }

    [Fact]
    public async Task Trabajo_SiPasaElTope_NoEnvia_LoDiceEnNegocio_YSigueConLaSiguiente()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (grande, clienteGrande, consultaGrande) = await EmpresaConConsultaAsync("Tope grande");
        var (chica, clienteChica, consultaChica) = await EmpresaConConsultaAsync("Tope chica");
        await api.EnDbAsync(async db =>
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Comparendos.AddRange(Enumerable.Range(0, QueryLimits.MaxUniverso + 1).Select(i => Comparendo(grande, $"88{i:D18}")));
            db.Comparendos.Add(Comparendo(chica, "89000000000000000001"));
            await db.SaveChangesAsync(Ct);
        });
        var pg = await ProgramadaVencidaAsync(clienteGrande, consultaGrande, "samuel.cardenas@flitsas.com");
        var pc = await ProgramadaVencidaAsync(clienteChica, consultaChica, "samuel.cardenas@flitsas.com");

        await CorrerTrabajoAsync();

        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == grande && c.Tipo == TipoComunicacion.ConsultaProgramada, Ct))).Should().Be(0);
        var resultado = (await api.EnDbAsync(db => db.ConsultasProgramadas.AsNoTracking().SingleAsync(x => x.Id == pg, Ct))).UltimoResultado;
        resultado.Should().StartWith("No se envió: la consulta trae demasiados comparendos (unos 20.001)");
        resultado.Should().NotContainAny("API", "SQL", "Exception", "MaxUniverso");
        (await api.EnDbAsync(db => db.ConsultasProgramadas.AsNoTracking().SingleAsync(x => x.Id == pc, Ct))).UltimoResultado.Should().Be("Enviado a 1 destinatario");
    }
}
