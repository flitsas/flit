using Flit.Admin.Domain.Companies.Settings;
using Flit.Api.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>HU #13344 — lo que core-api manda a Consultas es la configuración de la empresa, campo por campo.</summary>
public sealed class GrpcConsultasConfigSyncTests
{
    [Fact]
    public void ToProto_LlevaCadenasFailoverFuenteYAvaluos()
    {
        var defaults = TenantSettings.Default(Guid.NewGuid());
        var settings = new TenantSettings
        {
            TenantId = defaults.TenantId,
            AllowInitialRegistration = defaults.AllowInitialRegistration,
            AllowMiscNewVehicles = defaults.AllowMiscNewVehicles,
            OnlyOwnVehicles = defaults.OnlyOwnVehicles,
            SignatureVaultEnabled = defaults.SignatureVaultEnabled,
            NotificationChannel = defaults.NotificationChannel,
            NotificationTarget = defaults.NotificationTarget,
            PaymentMethods = defaults.PaymentMethods,
            RuntFailoverTimeoutMs = 15000,
            FinesQuerySource = "internal",
            ConsultationProviderConfig = new ConsultationProviderConfig(new Dictionary<string, ConsultationProviderSelection>
            {
                ["vehicle_plate"] = new("verifik", ["kyverum_runt"]),
                ["conductor"] = new("kyverum_runt_conductor", []),
            }),
            AvaluoProviderConfig = new AvaluoProviderConfig("base_gravable", ["base_gravable"]),
        };

        var proto = GrpcConsultasConfigSync.ToProto(settings);

        proto.FailoverTimeoutMs.Should().Be(15000);
        proto.FuenteMultas.Should().Be("internal");
        proto.Cadenas["vehicle_plate"].Principal.Should().Be("verifik");
        proto.Cadenas["vehicle_plate"].Respaldo.Should().Equal("kyverum_runt");
        proto.Cadenas["conductor"].Respaldo.Should().BeEmpty();
        proto.AvaluoPrincipal.Should().Be("base_gravable");
        proto.AvaluosHabilitados.Should().Equal("fasecolda", "base_gravable");
    }
}
