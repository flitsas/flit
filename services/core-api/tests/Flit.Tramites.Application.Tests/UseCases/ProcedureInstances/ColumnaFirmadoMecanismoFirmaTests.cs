using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11670 — <b>la columna «Firmado» del listado respeta el mecanismo de firma elegido.</b>
///
/// <para><b>Qué se corrige.</b> <c>DeriveFirmaParte</c> acreditaba por firma de baúl vigente sin pasar
/// por <see cref="FirmaBaulCobertura.Aplica"/>: ni comprobaba que el actor fuera jurídico ni qué
/// mecanismo de firma eligió el gestor. Con la HU #11667 el chip de identidad pasó a respetarlo, así
/// que las DOS superficies de la misma fila se contradecían: chip «pendiente» y columna «firmado»
/// para un actor jurídico con <c>mecanismoFirma = identidad</c> y baúl vigente. Es la raíz del
/// Bug #11141: consumidores que resuelven el baúl por su cuenta en vez de delegar en el predicado único.</para>
///
/// <para><b>Qué se ejercita.</b> El listado real (<see cref="ListProcedureInstancesHandler"/>) y la
/// ruta filtrada real (<see cref="ListProcedureInstancesFilteredHandler"/>), que comparte el mismo
/// mapeo. Las aserciones no miran la columna aislada sino que <b>columna y chip de la misma fila
/// coinciden</b>, que es lo que fallaba.</para>
///
/// <para>Uso de ejemplo:
/// <c>var fila = await FilaAsync("NIT", baulVigente: true, mecanismo: MecanismoFirma.Identidad);</c>
/// ⇒ <c>fila.FirmaCompradorEstado == FirmaParteEstados.Pendiente</c>.</para>
/// </summary>
public sealed class ColumnaFirmadoMecanismoFirmaTests
{
    private const string Documento = "900123456";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    [Fact]
    public async Task JuridicoConBaulVigentePeroMecanismoIdentidad_LaColumnaNoDiceFirmado()
    {
        // El caso del bug: el gestor eligió el sello de validación de identidad, así que el baúl NO se
        // va a consumir y la parte no está acreditada — la columna no puede decir lo contrario.
        var fila = await FilaAsync("NIT", baulVigente: true, mecanismo: MecanismoFirma.Identidad);

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task JuridicoConBaulVigentePeroMecanismoIdentidad_LaColumnaCoincideConElChipDeLaMismaFila()
    {
        // Las dos superficies contiguas salen del mismo predicado: si el chip dice que la identidad
        // sigue pendiente, la columna no puede acreditar a la parte.
        var fila = await FilaAsync("NIT", baulVigente: true, mecanismo: MecanismoFirma.Identidad);

        fila.IdentityValidationStatus.Should().NotBe(BiometricEstados.Aprobado);
        fila.FirmaCompradorEstado.Should().NotBe(FirmaParteEstados.Firmado);
        fila.CanSubmit.Should().BeFalse("sin identidad aprobada el trámite no se puede radicar");
    }

    [Fact]
    public async Task JuridicoConBaulVigenteYSinMecanismoElegido_LaColumnaDiceFirmado()
    {
        // Sin elección explícita manda la precedencia del baúl (HU #11031): el comportamiento previo
        // se conserva intacto.
        var fila = await FilaAsync("NIT", baulVigente: true, mecanismo: null);

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
        fila.IdentityValidationStatus.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task JuridicoConBaulVigenteYMecanismoBaul_LaColumnaDiceFirmado()
    {
        var fila = await FilaAsync("NIT", baulVigente: true, mecanismo: MecanismoFirma.Baul);

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
        fila.IdentityValidationStatus.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task PersonaNatural_IgnoraElBaulAunqueSuLlaveEsteVigente()
    {
        // Control: con la MISMA llave vigente en el diccionario, un comprador con cédula no queda
        // acreditado. El baúl solo lo consume el representante legal de una persona jurídica.
        var fila = await FilaAsync("CC", baulVigente: true, mecanismo: null);

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task MecanismoIdentidadConBaulVENCIDO_NoRechazaPorElBaul()
    {
        // La otra mitad de la delegación: cuando el baúl no procede no cuenta en NINGUNO de los dos
        // sentidos. Antes, un baúl caducado marcaba la parte como «rechazado» aunque el gestor
        // hubiera elegido validar por identidad y esa identidad solo estuviera pendiente.
        var fila = await FilaAsync("NIT", baulVigente: false, mecanismo: MecanismoFirma.Identidad);

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task LaRutaFiltradaDelListadoRecibeElMismoTrato()
    {
        // ListProcedureInstancesFilteredHandler reutiliza ToSummary: comprobado aquí para que nadie
        // duplique el mapeo más adelante y deje una de las dos rutas atrás (que es como nació el bug).
        var ct = TestContext.Current.CancellationToken;
        var instance = Tramite("NIT", MecanismoFirma.Identidad);
        Preparar(instance, baulVigente: true);
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstance>)[instance], 1));

        var (items, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = instance.TenantId }, ct);

        items.Should().ContainSingle().Which.FirmaCompradorEstado
            .Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task NoIntroduceNingunaConsultaNueva()
    {
        // El predicado es puro: lee el metadata del actor que ya viene en el grafo. Si alguien lo
        // resuelve consultando el baúl por fila, esto lo delata.
        var ct = TestContext.Current.CancellationToken;
        var instance = Tramite("NIT", MecanismoFirma.Identidad);
        Preparar(instance, baulVigente: true);

        await new ListProcedureInstancesHandler(_repo).HandleAsync(instance.TenantId, ct);

        await _repo.Received(1).ListFirmaBaulVigenciaKeysAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).ListVigenteApprovedIdentityKeysAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().FindVigenteApprovedByDocumentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<InstanceSummaryDto> FilaAsync(
        string tipoDocumento, bool baulVigente, string? mecanismo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Tramite(tipoDocumento, mecanismo);
        Preparar(instance, baulVigente);

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(instance.TenantId, ct);

        return filas.Should().ContainSingle().Subject;
    }

    private void Preparar(ProcedureInstance instance, bool baulVigente)
    {
        _repo.ListWithSummaryGraphAsync(
                instance.TenantId, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([instance]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        // Misma llave canónica que la identidad: tenant|TIPO|NÚMERO. El valor es la VIGENCIA.
        var actor = instance.Actors.First();
        var clave = BiometricRules.IdentidadKey(instance.TenantId, actor.DocumentType, actor.DocumentNumber);
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>(StringComparer.Ordinal) { [clave] = baulVigente });

        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
    }

    /// <summary>
    /// Matrícula inicial en borrador con un solo actor (comprador): el vendedor no existe en esta
    /// modalidad y su columna sale null, así que la única que se mueve es la del comprador.
    /// </summary>
    private static ProcedureInstance Tramite(string tipoDocumento, string? mecanismo)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000042",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "vin",
            ValueText = "1HGCM82633A004352",
            Source = "user",
        });
        instance.PreflightSnapshots.Add(new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(),
            Overall = "green",
            Checks = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = tipoDocumento,
            DocumentNumber = Documento,
            FullName = "Renting SAS",
            Email = "renting@x.com",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize(
                "Bogotá",
                "Calle 1 # 2-3",
                new ActorRepresentanteLegal("CC", "1090123456", "Ana Representante", "ana@x.com", null, mecanismo)),
        });
        foreach (var tipo in new[] { "factura", "aduana", "impronta" })
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
            });

        return instance;
    }
}
