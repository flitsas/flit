using Flit.Impuestos.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>De dónde salió la credencial de la compañía (columna <c>origen</c>).</summary>
public enum OrigenCredencial
{
    /// <summary>La devolvió el alta del proveedor.</summary>
    Alta,

    /// <summary>Se copió de la llave de Diagnóstico (DA-3 residual).</summary>
    CopiaDiagnostico,

    /// <summary>La cargó el Super Admin en el backoffice.</summary>
    CargaManual,
}

/// <summary>
/// Credenciales del API de liquidación de una compañía (DA-2=b): api_key y webhook_secret cifrados con el Data Protection del
/// servicio, un propósito por compañía y por código (plan §4.2). Nunca se devuelven ni se registran en claro.
/// </summary>
public sealed class CredencialEmpresa : IEntidadDeEmpresa
{
    public Guid TenantId { get; set; }

    public byte[] ApiKeyCifrada { get; set; } = [];

    /// <summary>Primeros caracteres de la api_key, para soporte; no es secreto.</summary>
    public string? ApiKeyPrefijo { get; set; }

    public byte[]? WebhookSecretCifrado { get; set; }

    /// <summary><c>company.id</c> en el API; opcional.</summary>
    public string? EmpresaApiId { get; set; }

    /// <summary>
    /// El webhook_secret anterior, con el mismo cifrado y propósito que <see cref="WebhookSecretCifrado"/>, mientras dura la ventana
    /// de rotación (M07b, plan §5.8 punto 7). Nunca se devuelve ni se registra.
    /// </summary>
    public byte[]? WebhookSecretAnteriorCifrado { get; set; }

    /// <summary>Hasta cuándo se acepta <see cref="WebhookSecretAnteriorCifrado"/> al verificar un aviso.</summary>
    public DateTimeOffset? WebhookSecretAnteriorHasta { get; set; }

    public OrigenCredencial Origen { get; set; }

    public long Version { get; set; } = 1;

    public DateTimeOffset ActualizadoEn { get; set; }

    public Guid? ActualizadoPor { get; set; }
}

/// <summary><c>impuestos.credenciales_empresa</c> (plan §4.1).</summary>
internal sealed class CredencialEmpresaConfig : IEntityTypeConfiguration<CredencialEmpresa>
{
    public void Configure(EntityTypeBuilder<CredencialEmpresa> builder)
    {
        builder.ToTable("credenciales_empresa", t =>
        {
            t.HasComment("Credenciales del API de liquidación por compañía, cifradas con Data Protection (DA-2=b).");
            t.HasCheckConstraint("ck_credenciales_empresa_origen", $"origen IN ({CodigosDeEnum.ListaSql<OrigenCredencial>()})");
        });
        builder.HasKey(x => x.TenantId).HasName("pk_credenciales_empresa");
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.HasOne<AprovisionamientoEmpresa>().WithOne().HasForeignKey<CredencialEmpresa>(x => x.TenantId)
            .HasConstraintName("fk_credenciales_empresa_aprovisionamientos").OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ApiKeyCifrada).HasComment("@secret Data Protection, purpose impuestos.credenciales.v1.{tenant}.api_key.");
        builder.Property(x => x.ApiKeyPrefijo).HasMaxLength(12).HasComment("Prefijo de la api_key para soporte; no es secreto.");
        builder.Property(x => x.WebhookSecretCifrado).HasComment("@secret Data Protection, purpose impuestos.credenciales.v1.{tenant}.webhook_secret.");
        builder.Property(x => x.EmpresaApiId).HasMaxLength(64);
        builder.Property(x => x.WebhookSecretAnteriorCifrado)
            .HasComment("@secret Data Protection, purpose impuestos.credenciales.v1.{tenant}.webhook_secret. Secreto anterior durante la ventana de rotación.");
        builder.Property(x => x.WebhookSecretAnteriorHasta).HasComment("Fin de la ventana en la que se acepta el secreto anterior (24 h).");
        builder.Property(x => x.Origen).HasConversion(CodigosDeEnum.Convertidor<OrigenCredencial>()).HasMaxLength(24);
        builder.Property(x => x.Version).HasDefaultValue(1L).ValueGeneratedNever();
    }
}
