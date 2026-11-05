using System.Text;
using Flit.Infrastructure.Notifications.Identity;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Identity;

/// <summary>
/// HU #14305 (Feature #14304, Épica #14271) — cifrado del token de la captura manual (Data Protection, propósito propio), la URL
/// única del enlace (la misma del correo) y el mapeo/DDL de <c>manual_capture_token_encrypted</c> (DDL 170).
/// <para>Uso: <c>var cifrado = protector.Protect(token); protector.TryUnprotect(cifrado)</c>.</para>
/// </summary>
public sealed class ManualCaptureTokenProtectionTests
{
    private static readonly string Token = new('t', 30);

    private static DataProtectionManualCaptureTokenProtector Protector(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void Cifra_y_descifra_de_ida_y_vuelta_sin_dejar_el_token_en_claro()
    {
        var protector = Protector();

        var cifrado = protector.Protect(Token);

        cifrado.Should().NotBeNullOrWhiteSpace().And.NotContain(Token);
        protector.TryUnprotect(cifrado).Should().Be(Token);
    }

    [Fact]
    public void Con_llaves_distintas_o_dato_corrupto_devuelve_null_sin_lanzar()
    {
        var mismo = new EphemeralDataProtectionProvider();
        var cifrado = Protector(mismo).Protect(Token);

        Protector().TryUnprotect(cifrado).Should().BeNull("llaves rotadas: otro anillo de llaves");
        Protector(mismo).TryUnprotect(cifrado).Should().Be(Token);
        Protector().TryUnprotect("CfDJ8-basura").Should().BeNull("dato corrupto");
        Protector().TryUnprotect("no-es-un-payload").Should().BeNull();
    }

    [Fact]
    public void El_proposito_es_propio_un_blob_del_secreto_del_webhook_no_se_descifra_aqui()
    {
        var provider = new EphemeralDataProtectionProvider();
        var ajeno = provider.CreateProtector("Flit.Kyverum.WebhookSecret.v1").Protect(Token);

        new DataProtectionManualCaptureTokenProtector(provider).TryUnprotect(ajeno).Should().BeNull();
    }

    [Fact]
    public void La_url_es_la_misma_que_arma_el_correo_al_titular()
    {
        var builder = new ManualCaptureLinkUrlBuilder(new EmailThemePublicBrandingOptions { PublicBaseUrl = "https://app.flit.example/" });

        var url = builder.Build("tok en/claro");

        url.Should().Be(ManualCaptureEmailComposer.BuildLink("https://app.flit.example/", "tok en/claro"));
        url.Should().Be("https://app.flit.example/verificacion/tok%20en%2Fclaro");
    }

    [Fact]
    public void El_modelo_EF_mapea_la_columna_y_el_DDL_170_es_aditivo_e_idempotente()
    {
        using var db = new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(nameof(ManualCaptureTokenProtectionTests)).Options);
        var p = db.Model.FindEntityType(typeof(ProcedureInstanceBiometricValidation))!
            .FindProperty(nameof(ProcedureInstanceBiometricValidation.ManualCaptureTokenEncrypted))!;
        p.GetColumnName().Should().Be("manual_capture_token_encrypted");
        p.IsNullable.Should().BeTrue();
        p.GetMaxLength().Should().Be(2000);

        const string resource = "Flit.Infrastructure.Persistence.Sql.Ddl.170-HU14305-token-captura-manual-cifrado.sql";
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        var ddl = string.Join('\n', reader.ReadToEnd().Split('\n').Select(l =>
        {
            var corte = l.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? l[..corte] : l;
        }));
        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS manual_capture_token_encrypted varchar(2000)");
        ddl.Should().NotContain("DROP").And.NotContain("CREATE TABLE").And.NotContain("UPDATE");
    }
}
