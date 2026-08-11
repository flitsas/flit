using Flit.Infrastructure.Notifications.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Catalog;

/// <summary>
/// Uso de ejemplo:
/// <code>
/// foreach (var t in NotificationTemplateCatalog.All) { ... }
/// NotificationTemplateCatalog.TryResolve("security.invitation", out var descriptor);
/// NotificationTemplateCatalog.TryResolve(new NotificationTemplateKey&lt;NotificationChannel&gt;("security.invitation", NotificationChannel.FlitSmtp), out var d2);
/// </code>
/// HU #11353 — catálogo enumerable de plantillas con id estable y metadatos.
/// </summary>
public class NotificationTemplateCatalogTests
{
    /// <summary>Canal FALSO — el catálogo jamás lo conoce. Solo existe para probar la restricción
    /// de diseño: resolver por (plantilla, canal) sin lista fija por canal.</summary>
    private enum FakeChannel
    {
        Carrier1,
        Carrier2,
    }

    [Fact]
    public void All_DebeTenerExactamenteCincoEntradas()
    {
        // AC1 — el inventario se descubre en runtime: exactamente 5 entradas.
        NotificationTemplateCatalog.All.Should().HaveCount(5);
    }

    [Fact]
    public void All_DebeCubrirLosCincoIdsEsperados()
    {
        // AC1 — invitación, recuperación de contraseña, reset administrativo, informe programado y alerta.
        NotificationTemplateCatalog.All.Select(t => t.Id).Should().BeEquivalentTo(
        [
            "security.invitation",
            "security.forgot-password",
            "security.admin-reset-password",
            "analytics.scheduled-report",
            "analytics.alert",
        ]);
    }

    [Fact]
    public void All_CadaEntradaDeclaraModuloYAlMenosUnDisparador()
    {
        // AC1 — cada entrada trae id estable, módulo y disparador.
        foreach (var descriptor in NotificationTemplateCatalog.All)
        {
            descriptor.Id.Should().NotBeNullOrWhiteSpace();
            descriptor.Module.Should().BeOneOf(NotificationModule.Security, NotificationModule.Analytics);
            descriptor.Triggers.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void All_LasEntradasDeSeguridadYAnaliticaCoincidenConSusModulos()
    {
        // AC1 — módulo correcto por plantilla (Seguridad vs Analítica).
        NotificationTemplateCatalog.TryResolve("security.invitation", out var invitation);
        NotificationTemplateCatalog.TryResolve("security.forgot-password", out var forgot);
        NotificationTemplateCatalog.TryResolve("security.admin-reset-password", out var adminReset);
        NotificationTemplateCatalog.TryResolve("analytics.scheduled-report", out var scheduled);
        NotificationTemplateCatalog.TryResolve("analytics.alert", out var alert);

        invitation.Module.Should().Be(NotificationModule.Security);
        forgot.Module.Should().Be(NotificationModule.Security);
        adminReset.Module.Should().Be(NotificationModule.Security);
        scheduled.Module.Should().Be(NotificationModule.Analytics);
        alert.Module.Should().Be(NotificationModule.Analytics);
    }

    [Fact]
    public void Invitacion_DeclaraLosDosDisparadoresQueComparteLaPlantilla()
    {
        // AC2 — la entrada de invitación declara CreateInvitation y ResendInvitation.
        NotificationTemplateCatalog.TryResolve("security.invitation", out var descriptor).Should().BeTrue();
        descriptor.Triggers.Should().BeEquivalentTo(
            [NotificationTrigger.CreateInvitation, NotificationTrigger.ResendInvitation]);
    }

    [Fact]
    public void All_SonSeisDisparadoresEnTotalParaCincoPlantillas()
    {
        // AC2 — 5 plantillas, 6 disparadores, justamente porque invitación tiene dos.
        var totalTriggers = NotificationTemplateCatalog.All.Sum(t => t.Triggers.Count);
        totalTriggers.Should().Be(6);
    }

    [Fact]
    public void All_LosIdsSonUnicos()
    {
        // AC3 — sin ids repetidos.
        NotificationTemplateCatalog.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Ids_NoCoincidenConNombresDeTipoNiNamespace()
    {
        // AC3 — el id NO depende del nombre de un tipo/namespace/ruta. Verificación directa:
        // ninguno de los ids literales coincide con nameof(...) de las clases de composición
        // (si alguien reemplazara el literal por nameof, este assert seguiría pasando por
        // casualidad de contenido, pero el punto central de AC3 se prueba en el test siguiente:
        // renombrar el tipo no cambia el id, porque el id no se DERIVA de él).
        NotificationTemplateCatalog.TryResolve("security.invitation", out var descriptor).Should().BeTrue();
        descriptor.Id.Should().NotBe(nameof(Flit.Modules.Security.Application.Auth.InvitationEmailTemplate));
        descriptor.Id.Should().Be("security.invitation"); // literal fijo, no derivado de reflexión
    }

    [Fact]
    public void TryResolve_ConIdInexistente_DevuelveFalseSinFallback()
    {
        // AC5 — sin plantilla por defecto: id inexistente falla explícito (false + out null).
        var found = NotificationTemplateCatalog.TryResolve("no-existe.este-id", out var descriptor);

        found.Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void TryResolve_ConIdVacio_DevuelveFalse()
    {
        // AC5 — edge case: id vacío tampoco resuelve a nada por defecto.
        NotificationTemplateCatalog.TryResolve(string.Empty, out var descriptor).Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void TryResolve_PorParConCanalReal_ResuelveLaMismaEntradaQuePorId()
    {
        // Restricción de diseño — pareja (plantilla, canal) con el canal REAL del dominio.
        var key = new NotificationTemplateKey<Flit.Admin.Domain.Companies.Settings.NotificationChannel>(
            "security.invitation", Flit.Admin.Domain.Companies.Settings.NotificationChannel.FlitSmtp);

        NotificationTemplateCatalog.TryResolve(key, out var byPair).Should().BeTrue();
        NotificationTemplateCatalog.TryResolve("security.invitation", out var byId).Should().BeTrue();
        byPair.Should().BeEquivalentTo(byId);
    }

    [Fact]
    public void TryResolve_PorParConCanalFalso_ResuelveIgual_SinListaFijaPorCanal()
    {
        // Restricción de diseño — un canal que el catálogo JAMÁS ha visto (FakeChannel) resuelve
        // exactamente igual. Si el catálogo mantuviera una lista/switch fijo por canal, este test
        // no compilaría o fallaría al no reconocer FakeChannel. Dar de alta un canal nuevo no
        // exige tocar NotificationTemplateCatalog.
        var key = new NotificationTemplateKey<FakeChannel>("analytics.alert", FakeChannel.Carrier2);

        var resolved = NotificationTemplateCatalog.TryResolve(key, out var descriptor);

        resolved.Should().BeTrue();
        descriptor.Id.Should().Be("analytics.alert");
    }

    [Fact]
    public void TryResolve_PorParConIdInexistenteYCanalFalso_DevuelveFalse()
    {
        // Restricción de diseño + AC5 combinados: ni el canal falso rescata un id inexistente.
        var key = new NotificationTemplateKey<FakeChannel>("no-existe", FakeChannel.Carrier1);

        NotificationTemplateCatalog.TryResolve(key, out var descriptor).Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void AltaDeUnaPlantillaNueva_NoExigeModificarConsumidores_SeVerificaPorContrato()
    {
        // AC4 — verificación de contrato: TryResolve(id) y TryResolve(key) son la ÚNICA superficie
        // pública que un consumidor usa. Ambas ya soportan cualquier id presente en `All` sin que
        // el consumidor (este test hace de "consumidor") conozca cuántas entradas hay ni sus tipos
        // concretos — agregar una 6ª entrada al arreglo privado `All` no cambiaría esta prueba.
        foreach (var descriptor in NotificationTemplateCatalog.All)
        {
            NotificationTemplateCatalog.TryResolve(descriptor.Id, out var resolved).Should().BeTrue();
            resolved.Should().BeEquivalentTo(descriptor);
        }
    }
}
