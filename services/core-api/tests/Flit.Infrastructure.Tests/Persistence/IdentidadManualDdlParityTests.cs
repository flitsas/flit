using System.Reflection;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13283 — el DDL 130 y las constantes del dominio no se desalinean: cada estado/proveedor de código está en el
/// CHECK, el modelo EF mapea las columnas nuevas y las alertas de "atascados de Kyverum" no cuentan los estados manuales.
/// El comportamiento contra el motor lo cubre <c>IdentidadManualSchemaMigrationTests</c> en Flit.Integration.Tests.
/// </summary>
public sealed class IdentidadManualDdlParityTests
{
    private static string LoadDdl()
    {
        const string resource = "Flit.Infrastructure.Persistence.Sql.Ddl.130-HU13283-identidad-manual.sql";
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(l =>
        {
            var corte = l.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? l[..corte] : l;
        }));
    }

    [Fact]
    public void ElCheckDeEstadosYDeProveedoresListaTodosLosValoresDelCodigo()
    {
        var ddl = LoadDdl();

        foreach (var estado in BiometricEstados.Todos)
        {
            ddl.Should().Contain($"'{estado}'");
        }

        foreach (var proveedor in BiometricProviders.Todos)
        {
            ddl.Should().Contain($"'{proveedor}'");
        }

        ddl.Should().Contain("ck_biometric_validations_status").And.Contain("ck_biometric_validations_provider");
        ddl.Should().Contain("DROP CONSTRAINT IF EXISTS ck_biometric_validations_status", "idempotente");
    }

    [Fact]
    public void ElDdlNoCreaTablaNiColumnaDeFirma_ReutilizaLaDelAdr0054()
    {
        var ddl = LoadDdl();

        ddl.Should().NotContain("CREATE TABLE");
        ddl.Should().NotContain("signature");
    }

    [Fact]
    public void ElModeloEfMapeaLasColumnasNuevasYElStatusCabeElEstadoMasLargo()
    {
        using var db = new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(nameof(IdentidadManualDdlParityTests)).Options);
        var entity = db.Model.FindEntityType(typeof(ProcedureInstanceBiometricValidation))!;

        var columnas = new Dictionary<string, string>
        {
            [nameof(ProcedureInstanceBiometricValidation.ApprovalOrigin)] = "approval_origin",
            [nameof(ProcedureInstanceBiometricValidation.ManualActivatedBy)] = "manual_activated_by",
            [nameof(ProcedureInstanceBiometricValidation.ManualActivatedAt)] = "manual_activated_at",
            [nameof(ProcedureInstanceBiometricValidation.ConsentAt)] = "consent_at",
            [nameof(ProcedureInstanceBiometricValidation.ConsentIp)] = "consent_ip",
            [nameof(ProcedureInstanceBiometricValidation.ConsentTextVersion)] = "consent_text_version",
            [nameof(ProcedureInstanceBiometricValidation.ReviewedBy)] = "reviewed_by",
            [nameof(ProcedureInstanceBiometricValidation.ReviewedAt)] = "reviewed_at",
            [nameof(ProcedureInstanceBiometricValidation.RejectionReasonCode)] = "rejection_reason_code",
        };
        foreach (var (propiedad, columna) in columnas)
        {
            var p = entity.FindProperty(propiedad);
            p.Should().NotBeNull(propiedad);
            p!.GetColumnName().Should().Be(columna);
            p!.IsNullable.Should().BeTrue($"{columna} es NULL");
        }

        entity.FindProperty(nameof(ProcedureInstanceBiometricValidation.Status))!.GetMaxLength()!.Value
            .Should().BeGreaterThanOrEqualTo(BiometricEstados.Todos.Max(e => e.Length));
    }

    [Fact]
    public void LasAlertasDeAtascadosDeKyverumNoContanLosEstadosManuales()
    {
        var tipo = typeof(FlitDbContext).Assembly
            .GetType("Flit.Infrastructure.Analytics.Scheduling.AlertMetricsReadRepository")!;
        var campo = tipo.GetField("PendingBiometricStatuses", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pendientes = (string)campo.GetRawConstantValue()!;

        pendientes.Should().NotContain(BiometricEstados.ManualActivo);
        pendientes.Should().NotContain(BiometricEstados.PendienteRevisionManual);
    }
}
