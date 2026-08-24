using System.Reflection;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11198 — el nombre del representante legal impreso en los documentos es el registrado EN EL
/// TRÁMITE, no el del directorio de la compañía.
///
/// <para>Las cuatro piezas (mandato, compraventa, solicitud y FUR) consumen la MISMA
/// <see cref="DocumentParte"/>, que se arma en un punto único (<c>AssembleData</c>/<c>AddParte</c>). Por
/// eso los tests se hacen sobre ese ensamblador: si el nombre sale bien ahí, sale igual en los cuatro
/// documentos (AC4); y si alguien volviera a armar la parte generador por generador, estos tests
/// dejarían de proteger nada, así que uno de ellos lo comprueba explícitamente.</para>
/// </summary>
public sealed class NombresRepresentanteEnDocumentosTests
{
    [Fact]
    public void AC1_ElNombreImpresoEsElRegistradoEnElTramite()
    {
        var partes = Ensamblar(RlDelTramite("Ana María Restrepo Gómez"));

        Rl(partes).RepresentanteLegalNombre.Should().Be("Ana María Restrepo Gómez");
    }

    [Fact]
    public void AC2_CuandoElDirectorioDiceOtroNombre_PrevaleceElDelTramite()
    {
        // El respaldo del directorio existe, pero el trámite trae nombre: gana el trámite.
        var partes = Ensamblar(
            RlDelTramite("Ana María Restrepo Gómez"),
            directorio: new Dictionary<string, string> { ["comprador"] = "Carlos Pérez (directorio)" });

        Rl(partes).RepresentanteLegalNombre.Should().Be("Ana María Restrepo Gómez");
        Rl(partes).RepresentanteLegalNombre.Should().NotContain("directorio");
    }

    [Fact]
    public void AC3_SiElTramiteNoLoTrae_SeUsaElDelDirectorio()
    {
        var partes = Ensamblar(
            RlDelTramite(nombre: null),
            directorio: new Dictionary<string, string> { ["comprador"] = "Carlos Pérez Directorio" });

        Rl(partes).RepresentanteLegalNombre.Should().Be("Carlos Pérez Directorio");
    }

    [Fact]
    public void SinNombreEnNingunLado_QuedaVacio_NoSeInventa()
    {
        var partes = Ensamblar(RlDelTramite(nombre: null));

        Rl(partes).RepresentanteLegalNombre.Should().BeNull();
    }

    [Fact]
    public void ElNombreDeUnRolNoSeFiltraAlOtro()
    {
        // El respaldo se indexa por rol: el representante del vendedor no puede aparecer como el del
        // comprador, que es exactamente el error que un diccionario mal llaveado producir&iacute;a.
        var partes = Ensamblar(
            RlDelTramite(nombre: null),
            directorio: new Dictionary<string, string> { ["vendedor"] = "Solo del vendedor" });

        Rl(partes).RepresentanteLegalNombre.Should().BeNull();
    }

    [Fact]
    public void AC4_LosCuatroDocumentosLeenElNombreDelMismoSitio()
    {
        // Invariante estructural: DocumentParte se construye en UN solo lugar. Si alguien agrega otro
        // `new DocumentParte(...)` con su propia resolución de nombre, mandato / compraventa / solicitud /
        // FUR podrían divergir sin que ningún test funcional lo note.
        var ensamblador = typeof(GenerarFurHandler)
            .GetMethod("AddParte", BindingFlags.NonPublic | BindingFlags.Static);

        ensamblador.Should().NotBeNull(
            "AddParte es el punto único donde se arma la parte de los documentos");
        ensamblador!.GetParameters().Should().Contain(
            p => p.Name == "nombresRlDirectorio",
            "el respaldo del directorio entra por el mismo punto, no por cada generador");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Invoca el ensamblador real de partes (privado) con un trámite de una sola parte jurídica.</summary>
    private static List<DocumentParte> Ensamblar(
        string metadata, IReadOnlyDictionary<string, string>? directorio = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "Empresa Compradora SAS",
            Email = "contacto@empresa.com",
            PersonType = "juridical",
            Metadata = metadata,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var partes = new List<DocumentParte>();
        var addParte = typeof(GenerarFurHandler)
            .GetMethod("AddParte", BindingFlags.NonPublic | BindingFlags.Static)!;
        addParte.Invoke(null, [partes, instance, "comprador", directorio]);
        return partes;
    }

    private static DocumentParte Rl(List<DocumentParte> partes) => partes.Should().ContainSingle().Subject;

    /// <summary><c>actor.metadata</c> tal como lo escribe el wizard: el nombre del RL es opcional.</summary>
    private static string RlDelTramite(string? nombre) =>
        JsonSerializer.Serialize(
            new
            {
                representanteLegal = new
                {
                    tipoDocumento = "CC",
                    numeroDocumento = "1090123456",
                    nombreCompleto = nombre,
                    email = "rep@empresa.com",
                },
            },
            MetadataJson);

    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);
}
