using Flit.Infrastructure.Notifications.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Catalog;

/// <summary>
/// Catálogo enumerable de plantillas con id estable y metadatos.
/// </summary>
public class NotificationTemplateCatalogTests
{
    private enum FakeChannel
    {
        Carrier1,
        Carrier2,
    }

    [Fact]
    public void All_DebeTenerExactamenteNueveEntradas()
    {
        NotificationTemplateCatalog.All.Should().HaveCount(9);
    }

    [Fact]
    public void All_DebeCubrirLosNueveIdsEsperados()
    {
        NotificationTemplateCatalog.All.Select(t => t.Id).Should().BeEquivalentTo(
        [
            "security.invitation",
            "security.forgot-password",
            "security.admin-reset-password",
            "security.welcome-registration",
            "analytics.scheduled-report",
            "analytics.alert",
            "tramites.aprobado",
            "tramites.rechazado",
            "tramites.asignacion-placa",
        ]);
    }

    [Fact]
    public void All_CadaEntradaDeclaraModuloYAlMenosUnDisparador()
    {
        foreach (var descriptor in NotificationTemplateCatalog.All)
        {
            descriptor.Id.Should().NotBeNullOrWhiteSpace();
            descriptor.Module.Should().BeOneOf(
                NotificationModule.Security,
                NotificationModule.Analytics,
                NotificationModule.Tramites);
            descriptor.Triggers.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void All_LasEntradasCoincidenConSusModulos()
    {
        NotificationTemplateCatalog.TryResolve("security.invitation", out var invitation);
        NotificationTemplateCatalog.TryResolve("security.forgot-password", out var forgot);
        NotificationTemplateCatalog.TryResolve("security.admin-reset-password", out var adminReset);
        NotificationTemplateCatalog.TryResolve("security.welcome-registration", out var welcome);
        NotificationTemplateCatalog.TryResolve("analytics.scheduled-report", out var scheduled);
        NotificationTemplateCatalog.TryResolve("analytics.alert", out var alert);
        NotificationTemplateCatalog.TryResolve("tramites.aprobado", out var aprobado);
        NotificationTemplateCatalog.TryResolve("tramites.rechazado", out var rechazado);
        NotificationTemplateCatalog.TryResolve("tramites.asignacion-placa", out var asignacion);

        invitation.Module.Should().Be(NotificationModule.Security);
        forgot.Module.Should().Be(NotificationModule.Security);
        adminReset.Module.Should().Be(NotificationModule.Security);
        welcome.Module.Should().Be(NotificationModule.Security);
        scheduled.Module.Should().Be(NotificationModule.Analytics);
        alert.Module.Should().Be(NotificationModule.Analytics);
        aprobado.Module.Should().Be(NotificationModule.Tramites);
        rechazado.Module.Should().Be(NotificationModule.Tramites);
        asignacion.Module.Should().Be(NotificationModule.Tramites);
    }

    [Fact]
    public void Invitacion_DeclaraLosDosDisparadoresQueComparteLaPlantilla()
    {
        NotificationTemplateCatalog.TryResolve("security.invitation", out var descriptor).Should().BeTrue();
        descriptor.Triggers.Should().BeEquivalentTo(
            [NotificationTrigger.CreateInvitation, NotificationTrigger.ResendInvitation]);
    }

    [Fact]
    public void TramitesAprobadoYRechazado_DeclaranProcedureStatusChanged()
    {
        NotificationTemplateCatalog.TryResolve("tramites.aprobado", out var aprobado).Should().BeTrue();
        aprobado.Name.Should().Be("Trámite Aprobado");
        aprobado.Triggers.Should().BeEquivalentTo([NotificationTrigger.ProcedureStatusChanged]);

        NotificationTemplateCatalog.TryResolve("tramites.rechazado", out var rechazado).Should().BeTrue();
        rechazado.Name.Should().Be("Trámite Rechazado");
        rechazado.Triggers.Should().BeEquivalentTo([NotificationTrigger.ProcedureStatusChanged]);
    }

    [Fact]
    public void WelcomeRegistration_DeclaraWelcomeRegistration()
    {
        NotificationTemplateCatalog.TryResolve("security.welcome-registration", out var descriptor).Should().BeTrue();
        descriptor.Name.Should().Be("Gracias por registrarte");
        descriptor.Triggers.Should().BeEquivalentTo([NotificationTrigger.WelcomeRegistration]);
    }

    [Fact]
    public void AsignacionPlaca_DeclaraPlateAssigned()
    {
        NotificationTemplateCatalog.TryResolve("tramites.asignacion-placa", out var descriptor).Should().BeTrue();
        descriptor.Name.Should().Be("Asignación placa");
        descriptor.Triggers.Should().BeEquivalentTo([NotificationTrigger.PlateAssigned]);
    }

    [Fact]
    public void All_SonDiezDisparadoresEnTotalParaNuevePlantillas()
    {
        // Invitación declara 2; el resto 1 cada una → 10.
        var totalTriggers = NotificationTemplateCatalog.All.Sum(t => t.Triggers.Count);
        totalTriggers.Should().Be(10);
    }

    [Fact]
    public void All_LosIdsSonUnicos()
    {
        NotificationTemplateCatalog.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Ids_NoCoincidenConNombresDeTipoNiNamespace()
    {
        NotificationTemplateCatalog.TryResolve("security.invitation", out var descriptor).Should().BeTrue();
        descriptor.Id.Should().NotBe(nameof(Flit.Modules.Security.Application.Auth.InvitationEmailTemplate));
        descriptor.Id.Should().Be("security.invitation");
    }

    [Fact]
    public void TryResolve_ConIdInexistente_DevuelveFalseSinFallback()
    {
        var found = NotificationTemplateCatalog.TryResolve("no-existe.este-id", out var descriptor);

        found.Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void TryResolve_ConIdVacio_DevuelveFalse()
    {
        NotificationTemplateCatalog.TryResolve(string.Empty, out var descriptor).Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void TryResolve_PorParConCanalReal_ResuelveLaMismaEntradaQuePorId()
    {
        var key = new NotificationTemplateKey<Flit.Admin.Domain.Companies.Settings.NotificationChannel>(
            "security.invitation", Flit.Admin.Domain.Companies.Settings.NotificationChannel.FlitSmtp);

        NotificationTemplateCatalog.TryResolve(key, out var byPair).Should().BeTrue();
        NotificationTemplateCatalog.TryResolve("security.invitation", out var byId).Should().BeTrue();
        byPair.Should().BeEquivalentTo(byId);
    }

    [Fact]
    public void TryResolve_PorParConCanalFalso_ResuelveIgual_SinListaFijaPorCanal()
    {
        var key = new NotificationTemplateKey<FakeChannel>("analytics.alert", FakeChannel.Carrier2);

        var resolved = NotificationTemplateCatalog.TryResolve(key, out var descriptor);

        resolved.Should().BeTrue();
        descriptor.Id.Should().Be("analytics.alert");
    }

    [Fact]
    public void TryResolve_PorParConIdInexistenteYCanalFalso_DevuelveFalse()
    {
        var key = new NotificationTemplateKey<FakeChannel>("no-existe", FakeChannel.Carrier1);

        NotificationTemplateCatalog.TryResolve(key, out var descriptor).Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void AltaDeUnaPlantillaNueva_NoExigeModificarConsumidores_SeVerificaPorContrato()
    {
        foreach (var descriptor in NotificationTemplateCatalog.All)
        {
            NotificationTemplateCatalog.TryResolve(descriptor.Id, out var resolved).Should().BeTrue();
            resolved.Should().BeEquivalentTo(descriptor);
        }
    }
}
