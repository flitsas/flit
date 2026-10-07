using System.Net;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13379 (Épica #13216, AC2/AC3/AC4) — <see cref="DescargarParteHandler"/>: solo el dueño, solo partes
/// <c>cerrada</c> de un lote descargable, <c>parte_descargada</c> registrada ANTES del primer byte (503 sin bytes si
/// falla), descifrado en streaming hacia el destino y ningún generador de PDF entre sus dependencias.
/// <para>Uso de ejemplo:
/// <c>var r = await handler.PrepararAsync(new DescargarParteQuery(loteId, 1, sub, "Radicador"), ct);</c>
/// ⇒ <c>r.Estado == DescargarParteEstado.Lista</c>; luego <c>await handler.EscribirAsync(r.Descarga!, body, ct)</c>.</para>
/// </summary>
public sealed class DescargarParteHandlerTests
{
    private static readonly Guid Dueno = Guid.NewGuid();
    private static readonly byte[] Claro = [.. Enumerable.Range(0, 1000).Select(i => (byte)(i % 251))];

    private readonly IConsolidadoLoteLectura _lectura = Substitute.For<IConsolidadoLoteLectura>();
    private readonly IConsolidadoLoteParteStorage _storage = Substitute.For<IConsolidadoLoteParteStorage>();
    private readonly CifradorEspejo _cifrador = new();
    private readonly List<string> _orden = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DescargarParteHandler Handler() => new(_lectura, _storage, _cifrador, reloj: new RelojFijo(LoteConsulta.Ahora));

    private ConsolidadoExportBatch Preparar(
        string estado = ConsolidadoExportStatus.Completado, DateTimeOffset? expira = null, DateTimeOffset? purgado = null,
        bool auditoriaOk = true)
    {
        var lote = LoteConsulta.Lote(estado, Dueno, partes: 3, expira: expira, purgado: purgado);
        _lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);
        _lectura.ObtenerPartesAsync(lote.Id, Arg.Any<CancellationToken>()).Returns(
        [
            LoteConsulta.Parte(lote, 1, bytes: Claro.Length),
            LoteConsulta.Parte(lote, 2, bytes: Claro.Length),
            LoteConsulta.Parte(lote, 3, ConsolidadoExportPartStatus.Purgada),
        ]);
        _storage.OpenReadAsync("fm/parte-1", Arg.Any<CancellationToken>())
            .Returns(_ => new StreamQueAvisa(Claro, () => _orden.Add("primer_byte_leido")));
        _lectura.RegistrarDescargaAsync(Arg.Any<ParteDescargadaRegistro>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _orden.Add("auditoria");
                return auditoriaOk;
            });
        return lote;
    }

    private static DescargarParteQuery Query(ConsolidadoExportBatch lote, int numero = 1, Guid? usuario = null) =>
        new(lote.Id, numero, usuario ?? Dueno, "Radicador", IPAddress.Parse("10.1.2.3"), "flit-tests/13379");

    // ── AC2 — descarga del dueño ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Dueno_LoteTerminadoNoExpirado_AuditaAntesDelPrimerByte_YDescifraEnStreamingAlDestino()
    {
        var lote = Preparar(ConsolidadoExportStatus.CompletadoConOmitidos);

        var r = await Handler().PrepararAsync(Query(lote), Ct);

        r.Estado.Should().Be(DescargarParteEstado.Lista);
        _orden.Should().Equal("auditoria");
        await using var descarga = r.Descarga!;
        descarga.NombreArchivo.Should().Be("consolidados_20261006_1430_parte-01-de-03.zip");
        descarga.BytesEnClaro.Should().Be(Claro.Length);
        await _lectura.Received(1).RegistrarDescargaAsync(
            Arg.Is<ParteDescargadaRegistro>(a => a.Lote == lote && a.PartNumber == 1 && a.RolCodigo == "Radicador"
                && a.ClientIp!.Equals(IPAddress.Parse("10.1.2.3")) && a.UserAgent == "flit-tests/13379"
                && a.OcurridoEn == LoteConsulta.Ahora),
            Arg.Any<CancellationToken>());

        using var destino = new MemoryStream();
        var escritos = await Handler().EscribirAsync(descarga, destino, Ct);

        escritos.Should().Be(Claro.Length);
        destino.ToArray().Should().Equal(Claro);
        _orden.Should().Equal("auditoria", "primer_byte_leido");
        _cifrador.Llamadas.Should().ContainSingle().Which.Should().Be((lote.Id, 1, lote.DekWrapped));
    }

    [Fact]
    public void AC2_Contrato_NoDependeDeNingunGeneradorDePdf()
    {
        var dependencias = typeof(DescargarParteHandler).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        dependencias.Should().BeEquivalentTo(
        [
            typeof(IConsolidadoLoteLectura), typeof(IConsolidadoLoteParteStorage), typeof(IConsolidadoLoteCipher),
            typeof(ILogger<DescargarParteHandler>), typeof(TimeProvider),
        ], "descargar nunca genera un PDF: solo lee la parte cifrada y la descifra");
    }

    [Fact]
    public async Task AC2_Borde_ElDescifradoEscribeMenosQuePlainSizeBytes_LanzaParteCorrupta()
    {
        var lote = Preparar();
        _cifrador.Recortar = 10;
        var r = await Handler().PrepararAsync(Query(lote), Ct);
        await using var descarga = r.Descarga!;

        var act = () => Handler().EscribirAsync(descarga, Stream.Null, Ct);

        (await act.Should().ThrowAsync<ConsolidadoLoteCifradoException>()).Which.Error
            .Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
    }

    // ── AC3 — otro usuario ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_OtroUsuario_NoEncontrada_SinAuditarNiAbrirElAlmacenamiento()
    {
        var lote = Preparar();
        var otro = Guid.NewGuid();
        _lectura.ObtenerDelDuenoAsync(lote.Id, otro, Arg.Any<CancellationToken>()).Returns((ConsolidadoExportBatch?)null);

        var r = await Handler().PrepararAsync(Query(lote, usuario: otro), Ct);

        r.Estado.Should().Be(DescargarParteEstado.NoEncontrada);
        r.Descarga.Should().BeNull();
        await _storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
        await _lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, Ct);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(40000)]
    public async Task AC3_ParteInexistente_NoEncontrada(int numero)
    {
        var lote = Preparar();

        (await Handler().PrepararAsync(Query(lote, numero), Ct)).Estado.Should().Be(DescargarParteEstado.NoEncontrada);
        await _lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, Ct);
    }

    // ── AC4 — estados que impiden descargar ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsolidadoExportStatus.EnCola)]
    [InlineData(ConsolidadoExportStatus.EnProceso)]
    [InlineData(ConsolidadoExportStatus.Empaquetando)]
    public async Task AC4_LoteNoTerminado_NoTerminado_SinBytes(string estado)
    {
        var lote = Preparar(estado);

        (await Handler().PrepararAsync(Query(lote), Ct)).Estado.Should().Be(DescargarParteEstado.NoTerminado);
        await _storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
        await _lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, Ct);
    }

    [Fact]
    public async Task AC4_LotePurgado_Expirada()
    {
        var lote = Preparar(ConsolidadoExportStatus.Expirado, purgado: LoteConsulta.Ahora.AddHours(-2));

        (await Handler().PrepararAsync(Query(lote), Ct)).Estado.Should().Be(DescargarParteEstado.Expirada);
        await _storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
    }

    [Fact]
    public async Task AC4_Borde_VencidoAunSinPurgar_Expirada()
    {
        var lote = Preparar(expira: LoteConsulta.Ahora);

        (await Handler().PrepararAsync(Query(lote), Ct)).Estado.Should().Be(DescargarParteEstado.Expirada);
    }

    [Fact]
    public async Task AC4_Borde_ParteYaPurgada_Expirada()
    {
        var lote = Preparar();

        (await Handler().PrepararAsync(Query(lote, 3), Ct)).Estado.Should().Be(DescargarParteEstado.Expirada);
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.Fallido)]
    [InlineData(ConsolidadoExportStatus.Cancelado)]
    public async Task AC4_LoteFallidoOCancelado_NoEncontrada_NuncaDescargable(string estado)
    {
        var lote = Preparar(estado);

        (await Handler().PrepararAsync(Query(lote), Ct)).Estado.Should().Be(DescargarParteEstado.NoEncontrada);
        await _storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
    }

    [Fact]
    public async Task AC4_AuditoriaNoRegistrada_SinBytes_CierraElObjetoAbierto()
    {
        var lote = Preparar(auditoriaOk: false);
        StreamQueAvisa? abierto = null;
        _storage.OpenReadAsync("fm/parte-1", Arg.Any<CancellationToken>())
            .Returns(_ => abierto = new StreamQueAvisa(Claro, () => _orden.Add("primer_byte_leido")));

        var r = await Handler().PrepararAsync(Query(lote), Ct);

        r.Estado.Should().Be(DescargarParteEstado.AuditoriaNoRegistrada);
        r.Descarga.Should().BeNull();
        abierto!.Dispuesto.Should().BeTrue();
        _orden.Should().NotContain("primer_byte_leido");
        _cifrador.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_Borde_LaAuditoriaLanza_TambienEsAuditoriaNoRegistrada()
    {
        var lote = Preparar();
        _lectura.RegistrarDescargaAsync(Arg.Any<ParteDescargadaRegistro>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bd caida"));

        (await Handler().PrepararAsync(Query(lote), Ct)).Estado.Should().Be(DescargarParteEstado.AuditoriaNoRegistrada);
    }

    [Fact]
    public async Task M2_ObjetoAusenteOAlmacenamientoCaido_ParteNoDisponible_SinAuditar()
    {
        var lote = Preparar();
        _storage.OpenReadAsync("fm/parte-1", Arg.Any<CancellationToken>()).Returns((Stream?)null);
        (await Handler().PrepararAsync(Query(lote), Ct)).Estado.Should().Be(DescargarParteEstado.ParteNoDisponible);

        _storage.OpenReadAsync("fm/parte-1", Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("502"));
        (await Handler().PrepararAsync(Query(lote), Ct)).Estado.Should().Be(DescargarParteEstado.ParteNoDisponible);

        await _lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, Ct);
    }

    /// <summary>Cifrador de prueba: «descifra» copiando tal cual; <see cref="Recortar"/> simula un descifrado incompleto.</summary>
    private sealed class CifradorEspejo : IConsolidadoLoteCipher
    {
        public List<(Guid Lote, int Parte, byte[]? Dek)> Llamadas { get; } = [];
        public int Recortar { get; set; }

        public byte[] GenerarDekEnvuelta() => [9];

        public Task<ConsolidadoLoteCifradoResultado> CifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenEnClaro, Stream destinoCifrado, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async Task<long> DescifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenCifrado, Stream destinoEnClaro, CancellationToken ct = default)
        {
            Llamadas.Add((loteId, partNumber, dekEnvuelta));
            using var copia = new MemoryStream();
            var buffer = new byte[256];
            int n;
            while ((n = await origenCifrado.ReadAsync(buffer, ct)) > 0)
                copia.Write(buffer, 0, n);
            var bytes = copia.ToArray()[..^Recortar];
            await destinoEnClaro.WriteAsync(bytes, ct);
            return bytes.Length;
        }
    }

    /// <summary>Stream de lectura que avisa al leer su primer byte y recuerda si se dispuso.</summary>
    private sealed class StreamQueAvisa(byte[] datos, Action alLeer) : MemoryStream(datos, writable: false)
    {
        private bool _avisado;

        public bool Dispuesto { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Avisar();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Avisar();
            return base.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Avisar();
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Avisar();
            return base.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Dispuesto = true;
            base.Dispose(disposing);
        }

        private void Avisar()
        {
            if (_avisado)
                return;
            _avisado = true;
            alLeer();
        }
    }
}
