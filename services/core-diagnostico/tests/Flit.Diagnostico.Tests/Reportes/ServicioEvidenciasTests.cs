using System.Net;
using System.Text;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Tablero;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #13846 (F07-H1, F07-C8, F07-C11, F07-C12, R-18) — <see cref="ServicioEvidencias"/>: con la misma huella entrega los bytes
/// guardados sin generar; con otra, genera, guarda en el almacén y en una transacción deja la anterior con <c>vigente=false</c>
/// (conservada) y la nueva vigente; dos pedidos a la vez dejan una sola vigente y ambos reciben la ganadora. El filtro por
/// empresa aplica. Postgres real (base efímera de <see cref="ApiDePrueba"/>); el file-manager lo hace un almacén en memoria
/// (o <c>diagnostico.archivo_local</c>). Placas TST### y datos inventados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ServicioEvidenciasTests
/// </code>
/// </example>
public sealed class ServicioEvidenciasTests
{
    private static readonly string H1 = new('a', 64);
    private static readonly string H2 = new('b', 64);

    private const string SimitManual =
        """{"semaforo":"AMARILLO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0001","fecha":"2026-09-30","valor":"468500","estado":"PENDIENTE","organismo":"Organismo de prueba"}]}""";

    // AC1 (F07-C8) — vigente con huella H y se pide con H: los bytes guardados, sin invocar al generador (almacén en la base).
    [Fact]
    public async Task Misma_huella_no_regenera()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await SembrarAsync(empresas.A, "TST801", ct);
        var clave = ClaveEvidencia.Pdf(lote.Id, lote.Vehiculos[0].Id);
        var generador = new Generador("v1");
        var usuario = Guid.NewGuid();

        EvidenciaEntregada primera;
        await using (var db = empresas.A.Db())
            primera = await Servicio(db, new AlmacenEnBase(db)).ObtenerOGenerarAsync(clave, H1, usuario, generador.GenerarAsync, ct);
        EvidenciaEntregada segunda;
        await using (var db = empresas.A.Db())
            segunda = await Servicio(db, new AlmacenEnBase(db)).ObtenerOGenerarAsync(clave, H1, Guid.NewGuid(), generador.GenerarAsync, ct);

        generador.Llamadas.Should().Be(1, "la segunda petición con la misma huella no genera");
        primera.Reutilizada.Should().BeFalse();
        segunda.Reutilizada.Should().BeTrue();
        segunda.Id.Should().Be(primera.Id);
        segunda.Huella.Should().Be(H1);
        segunda.Contenido.Should().Equal(Generador.Bytes("v1"));
        var filas = await EvidenciasAsync(empresas.A, lote.Id, ct);
        var fila = filas.Should().ContainSingle().Subject;
        fila.Vigente.Should().BeTrue();
        fila.Estado.Should().Be(EstadoEvidencia.Lista);
        fila.ArchivoRef.Should().StartWith("db:", "sin file-manager, con la bandera de pruebas, el archivo queda en diagnostico.archivo_local");
        fila.TamanoBytes.Should().Be(Generador.Bytes("v1").Length);
        fila.SolicitadoPor.Should().Be(usuario);
        fila.TenantId.Should().Be(empresas.A.Id);
    }

    // AC2 (F07-C8, F07-C12, R-18) — vigente con H1 y se pide con H2: la anterior queda vigente=false y se conserva (fila y archivo);
    // la nueva queda vigente con solicitado_por y generado_en.
    [Fact]
    public async Task Huella_distinta_invalida_la_anterior_y_la_conserva()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await SembrarAsync(empresas.A, "TST802", ct);
        var clave = ClaveEvidencia.Pdf(lote.Id, lote.Vehiculos[0].Id);
        var almacen = new AlmacenEnMemoria();
        var (u1, u2) = (Guid.NewGuid(), Guid.NewGuid());

        await using (var db = empresas.A.Db())
            await Servicio(db, almacen).ObtenerOGenerarAsync(clave, H1, u1, new Generador("v1").GenerarAsync, ct);
        var antes = DateTimeOffset.UtcNow;
        var generador = new Generador("v2");
        EvidenciaEntregada nueva;
        await using (var db = empresas.A.Db())
            nueva = await Servicio(db, almacen).ObtenerOGenerarAsync(clave, H2, u2, generador.GenerarAsync, ct);

        generador.Llamadas.Should().Be(1);
        nueva.Reutilizada.Should().BeFalse();
        nueva.Contenido.Should().Equal(Generador.Bytes("v2"));
        var filas = await EvidenciasAsync(empresas.A, lote.Id, ct);
        filas.Should().HaveCount(2, "la anterior no se borra (R-18)");
        var anterior = filas.Single(f => f.Huella == H1);
        anterior.Vigente.Should().BeFalse();
        anterior.Estado.Should().Be(EstadoEvidencia.Lista);
        anterior.SolicitadoPor.Should().Be(u1);
        (await almacen.LeerAsync(anterior.ArchivoRef!, ct)).Should().Equal(Generador.Bytes("v1"), "el archivo anterior se conserva");
        var vigente = filas.Single(f => f.Huella == H2);
        vigente.Id.Should().Be(nueva.Id);
        vigente.Vigente.Should().BeTrue();
        vigente.SolicitadoPor.Should().Be(u2);
        vigente.GeneradoEn.Should().NotBeNull().And.BeOnOrAfter(antes.AddSeconds(-1));
        vigente.Estado.Should().Be(EstadoEvidencia.Lista);
        await using (var db = empresas.A.Db())
            (await Servicio(db, almacen).ObtenerVigenteAsync(clave, ct))!.Id.Should().Be(nueva.Id);
    }

    // AC1 — la vigente apunta a un archivo que el almacén ya no tiene: se regenera en vez de entregar nada.
    [Fact]
    public async Task Archivo_perdido_en_el_almacen_se_regenera()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await SembrarAsync(empresas.A, "TST803", ct);
        var clave = ClaveEvidencia.Xlsx(lote.Id);
        var almacen = new AlmacenEnMemoria();
        var generador = new Generador("xlsx");

        await using (var db = empresas.A.Db())
            await Servicio(db, almacen).ObtenerOGenerarAsync(clave, H1, Guid.NewGuid(), generador.GenerarAsync, ct);
        almacen.Perder((await EvidenciasAsync(empresas.A, lote.Id, ct)).Single().ArchivoRef!);
        EvidenciaEntregada otra;
        await using (var db = empresas.A.Db())
            otra = await Servicio(db, almacen).ObtenerOGenerarAsync(clave, H1, Guid.NewGuid(), generador.GenerarAsync, ct);

        generador.Llamadas.Should().Be(2);
        otra.Contenido.Should().Equal(Generador.Bytes("xlsx"));
        (await EvidenciasAsync(empresas.A, lote.Id, ct)).Should().HaveCount(2).And.ContainSingle(f => f.Vigente);
    }

    // AC4 (F07-C8) — dos pedidos a la vez (sin vigente, con vigente anterior y sin vehículo): una sola vigente y ambos la reciben.
    [Fact]
    public async Task Dos_pedidos_simultaneos_dejan_una_sola_vigente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await SembrarAsync(empresas.A, "TST804", ct);
        var almacen = new AlmacenEnMemoria();

        foreach (var (clave, huella) in new[]
                 {
                     (ClaveEvidencia.Pdf(lote.Id, lote.Vehiculos[0].Id), H1), // sin vigente: los dos insertan (23505)
                     (ClaveEvidencia.Pdf(lote.Id, lote.Vehiculos[0].Id), H2), // con vigente: los dos la invalidan (xmin)
                     (ClaveEvidencia.Xlsx(lote.Id), H1), // sin vehículo: NULLS NOT DISTINCT
                 })
        {
            var (uno, dos) = await SimultaneosAsync(empresas.A, almacen, clave, huella, ct);

            dos.Id.Should().Be(uno.Id, $"{clave.Tipo} {huella[..1]}: el perdedor relee y entrega la ganadora");
            dos.Contenido.Should().Equal(uno.Contenido);
            new[] { uno.Reutilizada, dos.Reutilizada }.Should().BeEquivalentTo([false, true]);
        }

        var filas = await EvidenciasAsync(empresas.A, lote.Id, ct);
        filas.Where(f => f.Tipo == TipoEvidencia.Pdf).Should().HaveCount(2).And.ContainSingle(f => f.Vigente).Which.Huella.Should().Be(H2);
        filas.Where(f => f.Tipo == TipoEvidencia.Xlsx).Should().ContainSingle().Which.Vigente.Should().BeTrue();
    }

    // AC3 (F07-C8) — PDF vigente de un vehículo; el backoffice llena a mano una celda suya: su huella cambia (la del otro no) y el
    // siguiente pedido regenera.
    [Fact]
    public async Task Llenado_manual_de_una_celda_cambia_la_huella_del_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, version: 5, vehiculos:
        [
            new("TST811", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)]),
            new("TST812", [new(Fuente.Runt, EstadoCelda.Completado, 3), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 4)]),
        ]);
        var (v1, v2) = (lote.Vehiculos[0].Id, lote.Vehiculos[1].Id);
        var encabezado = new Dictionary<string, string?> { ["empresa"] = "Empresa de prueba", ["creador"] = "Ana Prueba" };
        var almacen = new AlmacenEnMemoria();
        var huellaAntes = await HuellaAsync(empresas.A, ClaveEvidencia.Pdf(lote.Id, v1), encabezado, ct);
        var otroAntes = await HuellaAsync(empresas.A, ClaveEvidencia.Pdf(lote.Id, v2), encabezado, ct);
        await using (var db = empresas.A.Db())
            await Servicio(db, almacen).ObtenerOGenerarAsync(ClaveEvidencia.Pdf(lote.Id, v1), huellaAntes, Guid.NewGuid(), new Generador("antes").GenerarAsync, ct);

        var token = api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer, Permisos.ConsultasLlenar], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        var llenado = await api.Cliente(token).PutAsync(
            $"{Program.Prefijo}/backoffice/consultas/{lote.Celda("TST811", Fuente.SimitVehiculo).Id}/manual?empresa={empresas.A.Id}",
            new StringContent($$"""{"version":2,"resultado":{{SimitManual}}}""", Encoding.UTF8, "application/json"),
            ct);
        llenado.StatusCode.Should().Be(HttpStatusCode.OK, await llenado.Content.ReadAsStringAsync(ct));

        var huellaDespues = await HuellaAsync(empresas.A, ClaveEvidencia.Pdf(lote.Id, v1), encabezado, ct);
        huellaDespues.Should().NotBe(huellaAntes, "el llenado manual sube la versión de la celda y cambia su payload");
        (await HuellaAsync(empresas.A, ClaveEvidencia.Pdf(lote.Id, v2), encabezado, ct)).Should().Be(otroAntes, "el otro vehículo no cambió");
        (await HuellaAsync(empresas.A, ClaveEvidencia.Pdf(lote.Id, v1), encabezado, ct)).Should().Be(huellaDespues, "leída de nuevo de la base, la misma");

        var generador = new Generador("despues");
        await using (var db = empresas.A.Db())
            (await Servicio(db, almacen).ObtenerOGenerarAsync(ClaveEvidencia.Pdf(lote.Id, v1), huellaDespues, Guid.NewGuid(), generador.GenerarAsync, ct))
                .Reutilizada.Should().BeFalse();
        generador.Llamadas.Should().Be(1);
        (await EvidenciasAsync(empresas.A, lote.Id, ct)).Should().HaveCount(2).And.ContainSingle(f => f.Vigente).Which.Huella.Should().Be(huellaDespues);
    }

    // AC5 (F07-C11) — una evidencia de B no se ve con el filtro de A, ni A puede pedir la evidencia de un lote de B (404 sin generar).
    [Fact]
    public async Task Filtro_de_tenant_aplica_a_evidencias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var loteA = await SembrarAsync(empresas.A, "TST821", ct);
        var loteB = await SembrarAsync(empresas.B, "TST822", ct);
        var claveB = ClaveEvidencia.Pdf(loteB.Id, loteB.Vehiculos[0].Id);
        var almacen = new AlmacenEnMemoria();
        await using (var db = empresas.B.Db())
            await Servicio(db, almacen).ObtenerOGenerarAsync(claveB, H1, Guid.NewGuid(), new Generador("b").GenerarAsync, ct);

        await using (var db = empresas.A.Db())
        {
            (await db.Set<Evidencia>().AsNoTracking().CountAsync(ct)).Should().Be(0, "el filtro de A no devuelve la evidencia de B");
            (await Servicio(db, almacen).ObtenerVigenteAsync(claveB, ct)).Should().BeNull();
            var generador = new Generador("a");
            var pedir = () => Servicio(db, almacen).ObtenerOGenerarAsync(claveB, H1, Guid.NewGuid(), generador.GenerarAsync, ct);
            (await pedir.Should().ThrowAsync<ReglaException>()).Which.Status.Should().Be(404);
            var mezclada = () => Servicio(db, almacen).ObtenerOGenerarAsync(
                ClaveEvidencia.Pdf(loteA.Id, loteB.Vehiculos[0].Id), H1, Guid.NewGuid(), generador.GenerarAsync, ct);
            (await mezclada.Should().ThrowAsync<ReglaException>()).Which.Status.Should().Be(404, "el vehículo no es de ese lote ni de la empresa");
            generador.Llamadas.Should().Be(0);
        }

        await using (var db = empresas.B.Db())
        {
            (await db.Set<Evidencia>().AsNoTracking().CountAsync(ct)).Should().Be(1);
            (await Servicio(db, almacen).ObtenerVigenteAsync(claveB, ct)).Should().NotBeNull();
        }

        (await EvidenciasAsync(empresas.A, loteA.Id, ct)).Should().BeEmpty();
    }

    // Sin FILE_MANAGER_BASE_URL y sin la bandera de pruebas, el servicio no arranca; con la bandera usa la base y con URL el file-manager.
    [Fact]
    public async Task Sin_file_manager_ni_bandera_el_servicio_no_arranca()
    {
        var sinNada = () => new ServiceCollection().AddAlmacenArchivos(Configuracion());
        sinNada.Should().Throw<InvalidOperationException>().WithMessage("*FILE_MANAGER_BASE_URL*");

        var conBandera = new ServiceCollection().AddAlmacenArchivos(Configuracion((OpcionesAlmacen.GuardarEnBase, "true")));
        conBandera.Should().ContainSingle(d => d.ServiceType == typeof(IAlmacenArchivos)).Which.ImplementationType.Should().Be<AlmacenEnBase>();

        var conUrl = new ServiceCollection().AddAlmacenArchivos(Configuracion(("FileManager:BaseUrl", "http://file-manager.prueba")));
        var registro = conUrl.Should().ContainSingle(d => d.ServiceType == typeof(IAlmacenArchivos)).Subject;
        registro.ImplementationType.Should().NotBe<AlmacenEnBase>();
        registro.ImplementationType!.Name.Should().Be("AlmacenFileManagerConRespaldo");

        var arrancar = async () =>
        {
            await using var api = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration[OpcionesAlmacen.GuardarEnBase] = "false");
        };
        await arrancar.Should().ThrowAsync<InvalidOperationException>().WithMessage("*FILE_MANAGER_BASE_URL*");
    }

    private static IConfiguration Configuracion(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor))).Build();

    private static ServicioEvidencias Servicio(DiagnosticoDb db, IAlmacenArchivos almacen) =>
        new(db, almacen, TimeProvider.System, NullLogger<ServicioEvidencias>.Instance);

    private static Task<LoteSembrado> SembrarAsync(EmpresaDePrueba empresa, string placa, CancellationToken ct) =>
        TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new(placa, [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

    private static async Task<List<Evidencia>> EvidenciasAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Evidencia>().AsNoTracking().Where(e => e.DiagnosticoId == lote).OrderBy(e => e.CreadoEn).ThenBy(e => e.Id).ToListAsync(ct);
    }

    private static async Task<string> HuellaAsync(EmpresaDePrueba empresa, ClaveEvidencia clave, Dictionary<string, string?> encabezado, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return HuellaEvidencia.Calcular(await HuellaEvidencia.LeerEntradaAsync(db, clave, versionPlantilla: 1, encabezado, ct));
    }

    /// <summary>Dos peticiones con su propio contexto que generan a la vez: ninguna guarda antes de que las dos hayan leído.</summary>
    private static async Task<(EvidenciaEntregada Uno, EvidenciaEntregada Dos)> SimultaneosAsync(
        EmpresaDePrueba empresa, AlmacenEnMemoria almacen, ClaveEvidencia clave, string huella, CancellationToken ct)
    {
        var llegados = 0;
        var ambos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ArchivoGenerado> Generar(string marca, CancellationToken c)
        {
            if (Interlocked.Increment(ref llegados) == 2)
                ambos.SetResult();
            await ambos.Task.WaitAsync(TimeSpan.FromSeconds(15), c);
            return new ArchivoGenerado(Generador.Bytes(marca), $"{marca}.pdf", "application/pdf");
        }

        await using var db1 = empresa.Db();
        await using var db2 = empresa.Db();
        var t1 = Task.Run(() => Servicio(db1, almacen).ObtenerOGenerarAsync(clave, huella, Guid.NewGuid(), c => Generar($"uno-{huella[..1]}", c), ct), ct);
        var t2 = Task.Run(() => Servicio(db2, almacen).ObtenerOGenerarAsync(clave, huella, Guid.NewGuid(), c => Generar($"dos-{huella[..1]}", c), ct), ct);
        var resultados = await Task.WhenAll(t1, t2);
        return (resultados[0], resultados[1]);
    }

    /// <summary>Generador de prueba: cuenta las llamadas y devuelve bytes que identifican la versión.</summary>
    private sealed class Generador(string marca)
    {
        private int _llamadas;

        public int Llamadas => _llamadas;

        public static byte[] Bytes(string marca) => Encoding.UTF8.GetBytes($"%PDF-prueba {marca}");

        public Task<ArchivoGenerado> GenerarAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _llamadas);
            return Task.FromResult(new ArchivoGenerado(Bytes(marca), $"{marca}.pdf", "application/pdf"));
        }
    }
}
