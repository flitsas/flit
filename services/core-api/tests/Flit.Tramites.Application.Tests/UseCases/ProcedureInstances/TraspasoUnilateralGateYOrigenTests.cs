using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0051 Decisión 5 — las dos preguntas que el ADR escaló al Líder Técnico, ya resueltas y
/// fijadas aquí para que no se relajen por accidente:
/// <list type="number">
///   <item><b>Completitud del vendedor sincronizado.</b> El gate NO se relaja: una parte vendedora
///   sin nombre sigue bloqueando la finalización del borrador, aunque nadie la haya tecleado. Es lo
///   que impide radicar un FUR con la sección del propietario en blanco; el hueco se corrige
///   revelando el formulario (Decisión 6), no bajando el listón.</item>
///   <item><b>Habeas Data.</b> La fila que se persiste sin captura consentida queda marcada con su
///   origen, y esa marca desaparece en cuanto el gestor la guarda a mano — porque desde ese momento
///   el dato sí viene de una captura del trámite.</item>
/// </list>
/// </summary>
public sealed class TraspasoUnilateralGateYOrigenTests
{
    [Fact]
    public void VendedorSincronizadoSinNombre_SigueBloqueandoLaFinalizacion()
    {
        var instance = Tramite(ProcedureTypeFixture.TraspasoUnilateral);
        instance.Actors.Add(Actor("comprador", "CC", "1020304050", "Ana Locataria"));
        // Lookup fallido: la fila existe con el documento, pero el nombre no resolvió.
        instance.Actors.Add(Actor("vendedor", "NIT", "900123456", nombre: ""));

        FinalizeDraftGate.Evaluate(instance, documentosCompletosOverride: true)
            .Should().Contain(FinalizeDraftGate.ActoresIncompletos);
    }

    [Fact]
    public void VendedorSincronizadoConNombre_NoBloqueaPorActores()
    {
        var instance = Tramite(ProcedureTypeFixture.TraspasoUnilateral);
        instance.Actors.Add(Actor("comprador", "CC", "1020304050", "Ana Locataria"));
        instance.Actors.Add(Actor("vendedor", "NIT", "900123456", "Leasing S.A."));

        FinalizeDraftGate.Evaluate(instance, documentosCompletosOverride: true)
            .Should().NotContain(FinalizeDraftGate.ActoresIncompletos);
    }

    [Fact]
    public void SinParteVendedora_ElGateNoLaInventa()
    {
        // Control: un tipo que no declara `requiresSeller` no puede empezar a exigir un vendedor.
        var instance = Tramite(ProcedureTypeFixture.Matricula);
        instance.Actors.Add(Actor("comprador", "CC", "1020304050", "Ana Compradora"));

        FinalizeDraftGate.Evaluate(instance, documentosCompletosOverride: true)
            .Should().NotContain(FinalizeDraftGate.ActoresIncompletos);
    }

    [Fact]
    public void MarcaDeOrigen_DistingueLaViaPorLaQueSeResolvioElDato()
    {
        var rues = ActorMetadataReader.Serialize(null, null, null, null, ActorOrigenes.RuesSync);
        var runt = ActorMetadataReader.Serialize(null, null, null, null, ActorOrigenes.RuntSync);

        ActorMetadataReader.GetOrigen(rues).Should().Be("rues_sync");
        ActorMetadataReader.GetOrigen(runt).Should().Be("runt_sync");
    }

    [Fact]
    public void MarcaDeOrigen_ConviveConElRestoDelMetadata()
    {
        var metadata = ActorMetadataReader.Serialize(
            "Bogotá", "Calle 1 # 2-3", null, null, ActorOrigenes.RuesSync);

        var (ciudad, direccion, _, _) = ActorMetadataReader.Parse(metadata);
        ciudad.Should().Be("Bogotá");
        direccion.Should().Be("Calle 1 # 2-3");
        ActorMetadataReader.GetOrigen(metadata).Should().Be("rues_sync");
    }

    [Fact]
    public void CapturaPorFormulario_NoDejaMarcaDeOrigen()
    {
        // El guardado del gestor reserializa SIN origen: es el mismo `Serialize` de cuatro argumentos
        // que usa PutActorsHandler. Que la marca se pierda ahí es el comportamiento buscado.
        var manual = ActorMetadataReader.Serialize("Bogotá", "Calle 1 # 2-3", null);

        ActorMetadataReader.GetOrigen(manual).Should().BeNull();
    }

    [Fact]
    public void GetOrigen_EsRobustoAnteMetadataAusenteOCorrupto()
    {
        ActorMetadataReader.GetOrigen(null).Should().BeNull();
        ActorMetadataReader.GetOrigen("{}").Should().BeNull();
        ActorMetadataReader.GetOrigen("no-json").Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProcedureInstance Tramite(ProcedureType tipo)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = tipo,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = tipo.Id,
            ReferenceNumber = "TRM-2026-000042",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // El organismo lo impone el RUNT en este tipo; se siembra para aislar el gate de actores.
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        return instance;
    }

    private static ProcedureInstanceActor Actor(
        string parte, string tipoDoc, string documento, string nombre) =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = parte,
            DocumentType = tipoDoc,
            DocumentNumber = documento,
            FullName = nombre,
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
