using System.Diagnostics.Metrics;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Reportes.Catalogo;
using Flit.Diagnostico.Tests.Tablero;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #14582 (be1-b; schema.md §1, contrato-api §5 H-A3; SP-1, SP-3) — backfill de <c>cambio_propietario</c> en las filas migradas
/// por la S3: solo vehículos con propietario declarado, sin valor y con propietarios del RUNT; por lotes, idempotente, en la
/// compañía fijada (la otra no se toca), descifrando solo el declarado y los propietarios de cada fila candidata, y sin documentos
/// en los logs. El comando del host (<see cref="ComandoBackfillCambioPropietario"/>) recibe las compañías y el tamaño del lote. CR-4:
/// un tipo del RUNT fuera de la tabla de sinónimos suma al contador <c>tipos_no_homologados</c> sin tipo ni número. Documentos
/// ficticios; PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~BackfillCambioPropietarioTests"
/// </code>
/// </example>
public sealed class BackfillCambioPropietarioTests
{
    private const string Declarado = "1020304050";
    private const string Otro = "79111222";

    // H-A3 — coincide → false, no coincide → true, tipo del RUNT no homologable → null (revisado), sin propietarios o sin declarado
    // → no candidato (ni se descifra). Lotes de 1: recorre todo. La compañía B no se toca. Ningún log trae un documento.
    [Fact]
    public async Task Backfill_fija_el_valor_por_lotes_solo_en_la_compania_y_sin_descifrar_de_mas()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var real = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var (coincide, noCoincide, sinPersonas, sinDeclarado, noHomologable) = await SembrarAsync(empresas.A, real, ct);
        var (deB, _, _, _, _) = await SembrarAsync(empresas.B, real, ct);
        var espia = new ProtectorDePrueba(real);

        ResultadoBackfill resultado;
        await using (var db = empresas.A.Db())
        {
            resultado = await new BackfillCambioPropietario(db, espia, api.App.Services.GetRequiredService<ILogger<BackfillCambioPropietario>>())
                .EjecutarAsync(1, ct);
        }

        resultado.Revisados.Should().Be(3);
        resultado.Fijados.Should().Be(2);
        resultado.SinResultado.Should().Be(1);
        resultado.Lotes.Should().BeGreaterThanOrEqualTo(3, "con lotes de 1 recorre las 3 candidatas una por una");
        espia.Llamadas.Should().Be(6, "SP-1: el declarado y el propietario de cada una de las 3 candidatas; nada de las demás");
        var valores = await ValoresAsync(empresas.A, ct);
        valores[coincide].Should().BeFalse();
        valores[noCoincide].Should().BeTrue();
        valores[noHomologable].Should().BeNull("SP-3: un tipo fuera de la tabla no da true");
        valores[sinPersonas].Should().BeNull();
        valores[sinDeclarado].Should().BeNull();
        (await ValoresAsync(empresas.B, ct))[deB].Should().BeNull("el backfill corre en la compañía fijada");
        logs.Mensajes.Should().NotContain(m => m.Contains(Declarado, StringComparison.Ordinal) || m.Contains(Otro, StringComparison.Ordinal));
    }

    // H-A3 — idempotente: la segunda corrida solo vuelve a mirar lo que quedó null y no cambia lo ya fijado.
    [Fact]
    public async Task Segunda_corrida_no_cambia_lo_fijado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var (coincide, noCoincide, _, _, _) = await SembrarAsync(empresa, protector, ct);
        var log = api.App.Services.GetRequiredService<ILogger<BackfillCambioPropietario>>();

        await using (var db = empresa.Db())
            await new BackfillCambioPropietario(db, protector, log).EjecutarAsync(100, ct);
        ResultadoBackfill segunda;
        await using (var db = empresa.Db())
            segunda = await new BackfillCambioPropietario(db, protector, log).EjecutarAsync(100, ct);

        segunda.Revisados.Should().Be(1, "solo la no homologable sigue en null");
        segunda.Fijados.Should().Be(0);
        var valores = await ValoresAsync(empresa, ct);
        valores[coincide].Should().BeFalse();
        valores[noCoincide].Should().BeTrue();
    }

    // Lanzamiento por ambiente — el comando lee las compañías y el lote; sin compañía o con un lote inválido falla con
    // ArgumentException. Ejecutado sobre los servicios del host fija la empresa de cada corrida y devuelve 0.
    [Fact]
    public async Task Comando_lee_sus_argumentos_y_corre_por_compania()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        ComandoBackfillCambioPropietario.EsComando([ComandoBackfillCambioPropietario.Nombre, "--empresa", a.ToString()]).Should().BeTrue();
        ComandoBackfillCambioPropietario.EsComando([]).Should().BeFalse();
        var opciones = ComandoBackfillCambioPropietario.Leer(
            [ComandoBackfillCambioPropietario.Nombre, "--empresa", a.ToString(), "--empresa", b.ToString(), "--lote", "50"]);
        opciones.Empresas.Should().Equal(a, b);
        opciones.TamanoLote.Should().Be(50);
        ComandoBackfillCambioPropietario.Leer([ComandoBackfillCambioPropietario.Nombre, "--empresa", a.ToString()]).TamanoLote
            .Should().Be(ComandoBackfillCambioPropietario.LotePorDefecto);
        FluentActions.Invoking(() => ComandoBackfillCambioPropietario.Leer([ComandoBackfillCambioPropietario.Nombre])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ComandoBackfillCambioPropietario.Leer([ComandoBackfillCambioPropietario.Nombre, "--empresa", "x"])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ComandoBackfillCambioPropietario.Leer([ComandoBackfillCambioPropietario.Nombre, "--empresa", a.ToString(), "--lote", "0"]))
            .Should().Throw<ArgumentException>();

        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var (coincide, noCoincide, _, _, _) = await SembrarAsync(empresas.A, protector, ct);
        var (deB, _, _, _, _) = await SembrarAsync(empresas.B, protector, ct);

        var codigo = await ComandoBackfillCambioPropietario.EjecutarAsync(
            api.App.Services, new OpcionesComandoBackfill([empresas.A.Id], 2), ct);

        codigo.Should().Be(0);
        var valores = await ValoresAsync(empresas.A, ct);
        valores[coincide].Should().BeFalse();
        valores[noCoincide].Should().BeTrue();
        (await ValoresAsync(empresas.B, ct))[deB].Should().BeNull("solo la compañía pedida");
    }

    // CR-4 (§1.4) — un propietario del RUNT con un tipo fuera de la tabla suma 1 a tipos_no_homologados con el id del vehículo
    // como etiqueta; ni el tipo ni el número van en la medición. La regla pura no cambia (sigue null).
    [Fact]
    public void Tipo_del_runt_no_homologado_suma_al_contador_sin_valores()
    {
        var protector = new ProtectorDocumentoPersona(new EphemeralDataProtectionProvider());
        var vehiculo = new VehiculoDiagnostico
        {
            IdentificadorOriginal = "BKF901",
            IdentificadorNorm = "BKF901",
            TipoIdentificador = TipoIdentificador.PlacaCarro,
            PropietarioDeclaradoDocTipo = "CC",
            PropietarioDeclaradoDocCifrado = protector.Proteger(Declarado),
        };
        var mediciones = new List<(long Valor, KeyValuePair<string, object?>[] Etiquetas)>();
        using var oyente = new MeterListener();
        oyente.InstrumentPublished = (instrumento, l) =>
        {
            if (instrumento.Meter.Name == MetricasDiagnostico.Medidor && instrumento.Name == MetricasDiagnostico.TiposNoHomologados)
                l.EnableMeasurementEvents(instrumento);
        };
        oyente.SetMeasurementEventCallback<long>((_, valor, etiquetas, _) =>
        {
            var copia = etiquetas.ToArray();
            if (copia.Any(e => Equals(e.Value, vehiculo.Id.ToString())))
            {
                lock (mediciones)
                    mediciones.Add((valor, copia));
            }
        });
        oyente.Start();

        DocumentosVehiculo.FijarCambioPropietario(vehiculo, [("XX", Otro), ("CC", "55555555")], protector);

        vehiculo.CambioPropietario.Should().BeNull("SP-3: con un propietario no homologable y sin coincidencia el resultado es null");
        var medicion = mediciones.Should().ContainSingle().Subject;
        medicion.Valor.Should().Be(1);
        medicion.Etiquetas.Select(e => e.Value?.ToString()).Should().NotContain(v => v == "XX" || v == Otro || v == "55555555" || v == Declarado);
    }

    /// <summary>
    /// Cinco vehículos con la celda RUNT completada: declarado que coincide con el propietario, que no coincide, declarado sin
    /// propietarios, propietario sin declarado y propietario con un tipo no homologable. Ninguno con <c>cambio_propietario</c>.
    /// </summary>
    private static async Task<(Guid Coincide, Guid NoCoincide, Guid SinPersonas, Guid SinDeclarado, Guid NoHomologable)> SembrarAsync(
        EmpresaDePrueba empresa, IProtectorDocumentoPersona protector, CancellationToken ct)
    {
        CeldaSembrada[] runt = [new(Fuente.Runt, EstadoCelda.Completado, 1)];
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos:
            [new("BKF001", runt), new("BKF002", runt), new("BKF003", runt), new("BKF004", runt), new("BKF005", runt)]);
        var ids = lote.Vehiculos.Select(v => v.Id).ToArray();
        await using var db = empresa.Db();
        var vehiculos = await db.Set<VehiculoDiagnostico>().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var id in new[] { ids[0], ids[1], ids[2], ids[4] })
        {
            vehiculos[id].PropietarioDeclaradoDocTipo = "CC";
            vehiculos[id].PropietarioDeclaradoDocCifrado = protector.Proteger(Declarado);
        }

        db.AddRange(
            Persona(ids[0], "C", Declarado, protector),
            Persona(ids[1], "CC", Otro, protector),
            Persona(ids[3], "CC", Declarado, protector),
            Persona(ids[4], "XX", Declarado, protector));
        await db.SaveChangesAsync(ct);
        return (ids[0], ids[1], ids[2], ids[3], ids[4]);
    }

    private static PersonaVehiculo Persona(Guid vehiculo, string tipo, string numero, IProtectorDocumentoPersona protector) => new()
    {
        VehiculoId = vehiculo,
        Rol = RolPersona.Propietario,
        Orden = 1,
        DocTipo = tipo,
        DocCifrado = protector.Proteger(numero),
        Origen = OrigenDocumento.Proveedor,
        CreadoEn = DateTimeOffset.UtcNow,
    };

    private static async Task<Dictionary<Guid, bool?>> ValoresAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<VehiculoDiagnostico>().AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.CambioPropietario, ct);
    }
}
