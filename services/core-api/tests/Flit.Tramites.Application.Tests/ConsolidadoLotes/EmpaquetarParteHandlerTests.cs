using System.IO.Compression;
using System.Text;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13378 (Épica #13216, ADR-0070 D4) — <see cref="EmpaquetarParteHandler"/> con dobles en memoria: ZIP en archivo
/// temporal (PDF sin compresión, CSV Optimal), cifrado a un segundo temporal, subida, cierre condicionado, plan B del
/// snapshot (AC4), fallo del lote por DEK (AC3), descarte por cancelación (AC6) y nombres duplicados.
/// El cifrado es un doble de identidad con cabecera: el FLZ1 real lo cubre <c>ConsolidadoLoteCipherTests</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var h = new EmpaquetarParteHandler(empaquetado, cipher, partes, adjuntos, actual, temporales, logger);
/// var desenlace = await h.HandleAsync(new ParteLoteReclamada(lote, parte), maxIntentos: 3, ct);
/// </code>
/// </remarks>
public sealed class EmpaquetarParteHandlerTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flit-13378-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeEmpaquetado _empaquetado = new();
    private readonly FakeCipher _cipher = new();
    private readonly FakeParteStorage _partes;
    private readonly LoteFakeStorage _adjuntos = new();
    private readonly FakeAdjuntoActual _actual = new();
    private readonly ConsolidadoLoteTemporales _temporales;

    public EmpaquetarParteHandlerTests()
    {
        _temporales = new ConsolidadoLoteTemporales(_dir);
        _partes = new FakeParteStorage(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private EmpaquetarParteHandler Handler() =>
        new(_empaquetado, _cipher, _partes, _adjuntos, _actual, _temporales, NullLogger<EmpaquetarParteHandler>.Instance);

    private static ParteLoteReclamada Reclamada(short parte = 1, short intentos = 0, string tipo = "consolidado")
    {
        var lote = new ConsolidadoExportBatch
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            Status = ConsolidadoExportStatus.Empaquetando,
            DocumentType = tipo,
            DekWrapped = [1, 2, 3],
        };
        return new ParteLoteReclamada(lote, new ConsolidadoExportBatchPart
        {
            Id = Guid.NewGuid(),
            BatchId = lote.Id,
            PartNumber = parte,
            Status = ConsolidadoExportPartStatus.Empaquetando,
            Attempts = intentos,
            LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(10),
        });
    }

    private PdfDeParte Pdf(int pos, string radicado, string? placa, byte[]? contenido = null)
    {
        var ruta = $"fm/{Guid.NewGuid():N}";
        if (contenido is not null)
            _adjuntos.Files[ruta] = contenido;
        return new PdfDeParte(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), pos, radicado, placa, ruta);
    }

    private static OmitidoDeParte Omitido(int pos, string radicado, string? placa, string motivo) =>
        new(Guid.NewGuid(), pos, radicado, placa, motivo);

    /// <summary>Abre el ZIP subido (el doble de cifrado es identidad tras su cabecera).</summary>
    private ZipArchive ZipSubido() =>
        new(new MemoryStream(FakeCipher.Descifrar(_partes.Subidas.Single().Bytes)), ZipArchiveMode.Read);

    private static string Leer(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_TresPdfYUnOmitido_ZipConLosTresPdfYOmitidosCsvDeUnaFila_YCierraCifrada()
    {
        var reclamada = Reclamada(parte: 2, intentos: 1);
        _empaquetado.Contenido = new ContenidoParteLote(
            [Pdf(0, "R-1", "abc123", "pdf-1"u8.ToArray()), Pdf(1, "R-2", null, "pdf-2"u8.ToArray()), Pdf(2, "R-3", "XYZ9", "pdf-3"u8.ToArray())],
            [Omitido(3, "R-4", "QWE1", "Acceso revocado")]);

        var desenlace = await Handler().HandleAsync(reclamada, maxIntentos: 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Cerrada);
        using var zip = ZipSubido();
        zip.Entries.Select(e => e.FullName).Should().Equal(
            "R-1_ABC123.pdf", "R-2_SIN-PLACA.pdf", "R-3_XYZ9.pdf", ConsolidadoLoteNombres.OmitidosCsv);
        Leer(zip.Entries[0]).Should().Be("pdf-1");
        zip.Entries.Take(3).Should().AllSatisfy(e => e.CompressedLength.Should().Be(e.Length, "PDF sin compresión"));
        var csv = Leer(zip.Entries[3]).TrimStart('﻿');
        csv.Should().Be("radicado;placa;motivo\r\nR-4;QWE1;Acceso revocado\r\n", "una fila por omitido");

        var cierre = _empaquetado.Cierres.Single();
        cierre.LoteId.Should().Be(reclamada.Lote.Id);
        cierre.PartNumber.Should().Be(2);
        cierre.IntentosReclamados.Should().Be(1, "el cierre exige los intentos del reclamo");
        cierre.BytesEnClaro.Should().Be(FakeCipher.Descifrar(_partes.Subidas.Single().Bytes).Length, "plain_size_bytes = ZIP en claro");
        cierre.Almacenado.Should().Be(_partes.Subidas.Single().Resultado, "stored_* = objeto cifrado subido");
        cierre.ItemsNoDisponibles.Should().BeEmpty();
        _cipher.Partes.Should().Equal(2);
        _empaquetado.Finalizados.Should().Equal([reclamada.Lote.Id], "tras cerrar se intenta finalizar el lote");
        Directory.EnumerateFiles(_dir).Should().BeEmpty("los temporales se borran al terminar");
    }

    [Fact]
    public async Task AC1_LoteSinItems_ParteSoloConCsvConEncabezado()
    {
        _empaquetado.Contenido = new ContenidoParteLote([], []);

        var desenlace = await Handler().HandleAsync(Reclamada(), 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Cerrada);
        using var zip = ZipSubido();
        zip.Entries.Select(e => e.FullName).Should().Equal(ConsolidadoLoteNombres.OmitidosCsv);
        Leer(zip.Entries[0]).TrimStart('﻿').Should().Be("radicado;placa;motivo\r\n");
    }

    [Fact]
    public async Task M2_DiscoTemporal_AlSubirSoloQuedaElCifrado_ElZipEnClaroYaSeBorro()
    {
        _empaquetado.Contenido = new ContenidoParteLote([Pdf(0, "R-1", "A1", new byte[300_000])], []);

        await Handler().HandleAsync(Reclamada(), 3, Ct);

        _partes.ArchivosAlSubir.Should().ContainSingle("como mucho 2 × M: al subir solo vive el cifrado")
            .Which.Should().EndWith(ConsolidadoLoteTemporales.ExtensionCifrado);
        _partes.Subidas.Single().Ruta.Should().StartWith(Path.GetFullPath(_dir), "se sube desde el archivo temporal, no desde memoria");
    }

    // ── Duplicados ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Duplicados_DosPdfConElMismoNombre_SufijoDeterministaEnElOrdenDeLaParte()
    {
        _empaquetado.Contenido = new ContenidoParteLote(
            [Pdf(0, "R-1", "ABC1", "a"u8.ToArray()), Pdf(1, "R-1", "abc-1", "b"u8.ToArray())], []);

        await Handler().HandleAsync(Reclamada(), 3, Ct);

        using var zip = ZipSubido();
        zip.Entries.Select(e => e.FullName).Should().Equal("R-1_ABC1.pdf", "R-1_ABC1_2.pdf", ConsolidadoLoteNombres.OmitidosCsv);
        Leer(zip.Entries[1]).Should().Be("b", "el segundo en orden de la parte lleva el sufijo");
    }

    [Fact]
    public void Duplicados_NombresDeEntrada_SinChoqueConUnSufijoYaUsado_NiConElCsv_NiPorMayusculas()
    {
        var nombres = new NombresDeEntradaZip();

        nombres.Reservar("X_2.pdf").Should().Be("X_2.pdf");
        nombres.Reservar("X.pdf").Should().Be("X.pdf");
        nombres.Reservar("x.PDF").Should().Be("x_3.PDF", "X_2 ya existe y la comparación ignora mayúsculas (Windows)");
        nombres.Reservar(ConsolidadoLoteNombres.OmitidosCsv).Should().Be("omitidos_2.csv", "omitidos.csv está reservado");
    }

    // ── AC4 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_SnapshotIlegible_UsaElAdjuntoActualDelMismoTipo()
    {
        var reclamada = Reclamada(tipo: ConsolidadoExportDocumentType.ConsolidadoMaestro);
        var pdf = Pdf(0, "R-1", "ABC1"); // snapshot sin binario
        _adjuntos.Files["fm/actual"] = "actual"u8.ToArray();
        _actual.Rutas[pdf.ProcedureInstanceId] = "fm/actual";
        _empaquetado.Contenido = new ContenidoParteLote([pdf], []);

        var desenlace = await Handler().HandleAsync(reclamada, 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Cerrada);
        _actual.Consultas.Should().ContainSingle()
            .Which.Should().Be((pdf.ProcedureInstanceId, pdf.TenantId, ConsolidadoExportDocumentType.ConsolidadoMaestro),
                "se busca con la compañía del ítem y el tipo del lote");
        using var zip = ZipSubido();
        Leer(zip.GetEntry("R-1_ABC1.pdf")!).Should().Be("actual");
        _empaquetado.Cierres.Single().ItemsNoDisponibles.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_Negativo_SinSnapshotNiAdjuntoActual_ElItemPasaAOmitidoYElPdfNoEntra()
    {
        var bueno = Pdf(0, "R-1", "ABC1", "ok"u8.ToArray());
        var perdido = Pdf(2, "R-2", "DEF2");
        _empaquetado.Contenido = new ContenidoParteLote([bueno, perdido], [Omitido(1, "R-9", "ZZZ9", "Acceso revocado")]);

        var desenlace = await Handler().HandleAsync(Reclamada(), 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Cerrada);
        _empaquetado.Cierres.Single().ItemsNoDisponibles.Should().Equal(perdido.ItemId);
        using var zip = ZipSubido();
        zip.Entries.Select(e => e.FullName).Should().Equal("R-1_ABC1.pdf", ConsolidadoLoteNombres.OmitidosCsv);
        Leer(zip.GetEntry(ConsolidadoLoteNombres.OmitidosCsv)!).TrimStart('﻿').Should().Be(
            "radicado;placa;motivo\r\nR-9;ZZZ9;Acceso revocado\r\n"
            + $"R-2;DEF2;{ConsolidadoErrorTextos.ParaLote(ConsolidadoLoteOmisiones.AdjuntoNoDisponible)}\r\n",
            "el ítem no disponible entra al CSV por su posición");
    }

    [Fact]
    public void AC4_Contrato_ElEmpaquetadoNoDependeDeNingunGenerador()
    {
        var dependencias = typeof(EmpaquetarParteHandler).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).ToList();

        dependencias.Should().NotContain(typeof(GenerarConsolidadoHandler), "nunca se genera un PDF en el empaquetado");
        dependencias.Should().NotContain(typeof(GenerarConsolidadoMaestroHandler));
        dependencias.Should().NotContain(typeof(ILoteItemEntregador));
        typeof(ConsolidadoLoteAdjuntoActual).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).Should().NotContain(typeof(GenerarConsolidadoHandler));
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_Negativo_DekQueNoSeDesenvuelve_LoteFallido_SinSubir()
    {
        _empaquetado.Contenido = new ContenidoParteLote([Pdf(0, "R-1", "A1", "x"u8.ToArray())], []);
        _cipher.Error = ConsolidadoLoteCifradoError.DekInvalida;
        var reclamada = Reclamada();

        var desenlace = await Handler().HandleAsync(reclamada, 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.LoteFallido);
        _empaquetado.LotesFallidos.Should().Equal((reclamada.Lote.Id, ConsolidadoLoteErrores.DekInvalida));
        _partes.Subidas.Should().BeEmpty("sin partes descargables");
        _empaquetado.Cierres.Should().BeEmpty();
        Directory.EnumerateFiles(_dir).Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_FalloTecnico_RegistraElIntentoConsumido()
    {
        _empaquetado.Contenido = new ContenidoParteLote([Pdf(0, "R-1", "A1", "x"u8.ToArray())], []);
        _partes.Fallar = true;
        _empaquetado.DesenlaceFallo = FalloParteDesenlace.LoteFallido;
        var reclamada = Reclamada(intentos: 2);

        var desenlace = await Handler().HandleAsync(reclamada, maxIntentos: 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.LoteFallido, "el puerto decide: 3 de 3 agota la parte");
        _empaquetado.Fallos.Should().ContainSingle().Which.Should().Be(
            new FalloParteLote(reclamada.Lote.Id, 1, IntentosReclamados: 2, Intentos: 3, MaxIntentos: 3));
        Directory.EnumerateFiles(_dir).Should().BeEmpty("los temporales se borran también en el fallo");
    }

    // ── AC6 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_Negativo_LoteCanceladoAntesDeSubir_NoSeSube_YQuedaDescartada()
    {
        _empaquetado.Contenido = new ContenidoParteLote([Pdf(0, "R-1", "A1", "x"u8.ToArray())], []);
        _empaquetado.LoteInactivo = true;

        var desenlace = await Handler().HandleAsync(Reclamada(), 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Descartada);
        _partes.Subidas.Should().BeEmpty();
        _empaquetado.Cierres.Should().BeEmpty();
        _empaquetado.Finalizados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_CanceladoEntreSubidaYCierre_ElCierreLaDescarta_YNoSeFinaliza()
    {
        _empaquetado.Contenido = new ContenidoParteLote([Pdf(0, "R-1", "A1", "x"u8.ToArray())], []);
        _empaquetado.DesenlaceCierre = CierreParteDesenlace.Descartada;

        var desenlace = await Handler().HandleAsync(Reclamada(), 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Descartada);
        _empaquetado.Finalizados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_DekAusente_LoteCancelado_DescartaSinFallarElLote()
    {
        _empaquetado.Contenido = new ContenidoParteLote([], []);
        _cipher.Error = ConsolidadoLoteCifradoError.DekAusente;
        _empaquetado.LoteInactivo = true;

        var desenlace = await Handler().HandleAsync(Reclamada(), 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Descartada);
        _empaquetado.LotesFallidos.Should().BeEmpty();
    }

    // ── AC5 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AC5_LimpiarHuerfanos_BorraSoloLosTemporalesDelCarril()
    {
        Directory.CreateDirectory(_dir);
        var zip = Path.Combine(_dir, "a" + ConsolidadoLoteTemporales.ExtensionZip);
        var cifrado = Path.Combine(_dir, "b" + ConsolidadoLoteTemporales.ExtensionCifrado);
        var ajeno = Path.Combine(_dir, "otro.txt");
        foreach (var f in new[] { zip, cifrado, ajeno })
            File.WriteAllText(f, "x");

        _temporales.LimpiarHuerfanos().Should().Be(2);

        File.Exists(zip).Should().BeFalse();
        File.Exists(cifrado).Should().BeFalse();
        File.Exists(ajeno).Should().BeTrue("solo se tocan los archivos del carril");
    }

    [Fact]
    public void AC5_LimpiarHuerfanos_SinDirectorio_NoFalla() =>
        new ConsolidadoLoteTemporales(Path.Combine(_dir, "no-existe")).LimpiarHuerfanos().Should().Be(0);

    // ── Dobles ──────────────────────────────────────────────────────────────────────────────

    private sealed class FakeEmpaquetado : IConsolidadoLoteEmpaquetado
    {
        public ContenidoParteLote Contenido { get; set; } = new([], []);
        public bool LoteInactivo { get; set; }
        public CierreParteDesenlace DesenlaceCierre { get; set; } = CierreParteDesenlace.Cerrada;
        public FalloParteDesenlace DesenlaceFallo { get; set; } = FalloParteDesenlace.Reprogramada;
        public List<CierreParteLote> Cierres { get; } = [];
        public List<FalloParteLote> Fallos { get; } = [];
        public List<(Guid, string)> LotesFallidos { get; } = [];
        public List<Guid> Finalizados { get; } = [];

        public Task<ParteLoteReclamada?> ReclamarSiguienteParteAsync(int leaseSegundos, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ContenidoParteLote> LeerContenidoAsync(Guid loteId, short partNumber, CancellationToken ct = default) =>
            Task.FromResult(Contenido);

        public Task<bool> DescartarSiLoteInactivoAsync(Guid loteId, short partNumber, short intentos, CancellationToken ct = default) =>
            Task.FromResult(LoteInactivo);

        public Task<CierreParteDesenlace> CerrarParteAsync(CierreParteLote cierre, CancellationToken ct = default)
        {
            Cierres.Add(cierre);
            return Task.FromResult(DesenlaceCierre);
        }

        public Task<FalloParteDesenlace> RegistrarFalloParteAsync(FalloParteLote fallo, CancellationToken ct = default)
        {
            Fallos.Add(fallo);
            return Task.FromResult(DesenlaceFallo);
        }

        public Task<bool> FallarLoteAsync(Guid loteId, string codigoError, CancellationToken ct = default)
        {
            LotesFallidos.Add((loteId, codigoError));
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<Guid>> ObtenerLotesParaFinalizarAsync(int maximo, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LoteFinalizado?> FinalizarLoteAsync(Guid loteId, CancellationToken ct = default)
        {
            Finalizados.Add(loteId);
            return Task.FromResult<LoteFinalizado?>(null);
        }
    }

    /// <summary>Cifrado de identidad con una cabecera de 4 bytes: permite leer el ZIP subido.</summary>
    private sealed class FakeCipher : IConsolidadoLoteCipher
    {
        private static readonly byte[] Cabecera = "FAKE"u8.ToArray();

        public ConsolidadoLoteCifradoError? Error { get; set; }
        public List<int> Partes { get; } = [];

        public static byte[] Descifrar(byte[] cifrado) => cifrado[Cabecera.Length..];

        public byte[] GenerarDekEnvuelta() => [1];

        public async Task<ConsolidadoLoteCifradoResultado> CifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenEnClaro, Stream destinoCifrado, CancellationToken ct = default)
        {
            if (Error is { } e)
                throw new ConsolidadoLoteCifradoException(e);
            Partes.Add(partNumber);
            await destinoCifrado.WriteAsync(Cabecera, ct);
            var antes = destinoCifrado.Position;
            await origenEnClaro.CopyToAsync(destinoCifrado, ct);
            var claro = destinoCifrado.Position - antes;
            return new ConsolidadoLoteCifradoResultado(claro, claro + Cabecera.Length, 1);
        }

        public Task<long> DescifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenCifrado, Stream destinoEnClaro, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeParteStorage(string dir) : IConsolidadoLoteParteStorage
    {
        public bool Fallar { get; set; }
        public List<(string Ruta, byte[] Bytes, StoredFile Resultado)> Subidas { get; } = [];
        public List<string> ArchivosAlSubir { get; } = [];

        public async Task<StoredFile> SubirAsync(Guid loteId, int partNumber, string rutaArchivoCifrado, CancellationToken ct = default)
        {
            ArchivosAlSubir.AddRange(Directory.EnumerateFiles(dir));
            if (Fallar)
                throw new HttpRequestException("file-manager caído (simulado)");
            var bytes = await File.ReadAllBytesAsync(rutaArchivoCifrado, ct);
            var resultado = new StoredFile($"fm-parte-{partNumber}", new string('a', 64), bytes.Length);
            Subidas.Add((rutaArchivoCifrado, bytes, resultado));
            return resultado;
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();

        public void Delete(string storagePath)
        {
        }
    }

    private sealed class FakeAdjuntoActual : IConsolidadoLoteAdjuntoActual
    {
        public Dictionary<Guid, string> Rutas { get; } = new();
        public List<(Guid, Guid, string)> Consultas { get; } = [];

        public Task<string?> StoragePathActualAsync(Guid procedureInstanceId, Guid tenantId, string tipoDocumento, CancellationToken ct = default)
        {
            Consultas.Add((procedureInstanceId, tenantId, tipoDocumento));
            return Task.FromResult(Rutas.TryGetValue(procedureInstanceId, out var r) ? r : null);
        }
    }
}
