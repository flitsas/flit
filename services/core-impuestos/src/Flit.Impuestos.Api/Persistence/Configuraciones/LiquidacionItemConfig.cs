using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Una placa de una liquidación con su estado (plan §3.2), su correlación con el API (<c>consulta_api_id</c>, hash del token) y el
/// resultado. El documento y el nombre del propietario y el cuerpo completo del resultado van cifrados (DA-7, Ley 1581); los
/// montos y estados van aparte, sin PII. Concurrencia optimista con <c>xmin</c>.
/// </summary>
public sealed class LiquidacionItem : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid LiquidacionId { get; set; }

    public Guid TenantId { get; set; }

    public int? Fila { get; set; }

    /// <summary>Placa normalizada (S16).</summary>
    public string Placa { get; set; } = string.Empty;

    public EstadoItem Estado { get; set; } = EstadoItem.Pendiente;

    public string? ErrorCodigo { get; set; }

    public ClaseError? ErrorClase { get; set; }

    public string? ErrorMensaje { get; set; }

    public string? CampoFaltante { get; set; }

    public string? DepartamentoCodigo { get; set; }

    public string? DepartamentoNombre { get; set; }

    public string? SecretariaCiudad { get; set; }

    /// <summary>Nombre de la secretaría (<c>Item.secretaria.nombre</c>, 1.1.0; lo llena la homologación de M09).</summary>
    public string? SecretariaNombre { get; set; }

    public string? DocumentoTipo { get; set; }

    public byte[]? DocumentoNumeroCifrado { get; set; }

    public byte[]? PropietarioNombreCifrado { get; set; }

    public string? ConsultaApiId { get; set; }

    /// <summary>SHA-256 del token opaco del <c>callbackUrl</c>; el token no se guarda.</summary>
    public byte[]? TokenHash { get; set; }

    public bool ConCallback { get; set; }

    public int IntentosRunt { get; set; }

    public int IntentosReenvio { get; set; }

    public DateTimeOffset? ProximoIntentoEn { get; set; }

    public DateTimeOffset? EnviadoEn { get; set; }

    public DateTimeOffset? ProximoPollEn { get; set; }

    public int AttemptApiMax { get; set; }

    public string? ConsultaStatus { get; set; }

    public string? ResultadoStatus { get; set; }

    public bool Parcial { get; set; }

    /// <summary>Total a pagar; null no es 0 (F2-CF14).</summary>
    public decimal? TotalAPagar { get; set; }

    public byte[]? ResultadoCifrado { get; set; }

    public string[] Warnings { get; set; } = [];

    public bool ResultadoTardio { get; set; }

    public Guid? RelanzadoDesde { get; set; }

    public bool PosibleDuplicado { get; set; }

    /// <summary>Intento en curso del ítem (R3): 1 al crear; cada relanzamiento cierra el actual en <c>liquidacion_item_intentos</c> y suma 1.</summary>
    public int NumeroIntento { get; set; } = 1;

    public DateTimeOffset ActualizadoEn { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>).</summary>
    public uint Version { get; set; }
}

/// <summary><c>impuestos.liquidacion_items</c> (plan §4.1).</summary>
internal sealed class LiquidacionItemConfig : IEntityTypeConfiguration<LiquidacionItem>
{
    /// <summary>Estados que ocupan crédito: los de <c>DespachadorCredito.EnVuelo</c>, que el índice parcial repite.</summary>
    internal static readonly EstadoItem[] EnVuelo = [EstadoItem.ConsultandoRunt, EstadoItem.Enviando];

    private static string ListaSql(IEnumerable<EstadoItem> estados) => string.Join(",", estados.Select(e => $"'{CodigosDeEnum.Codigo(e)}'"));

    public void Configure(EntityTypeBuilder<LiquidacionItem> builder)
    {
        builder.ToTable("liquidacion_items", t =>
        {
            t.HasComment("Placas de cada liquidación con su estado y resultado. PII cifrada (DA-7).");
            t.HasCheckConstraint("ck_liquidacion_items_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoItem>()})");
            t.HasCheckConstraint("ck_liquidacion_items_error_clase", $"error_clase IS NULL OR error_clase IN ({CodigosDeEnum.ListaSql<ClaseError>()})");
        });
        builder.HasKey(x => x.Id).HasName("pk_liquidacion_items");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<Liquidacion>().WithMany().HasForeignKey(x => x.LiquidacionId)
            .HasConstraintName("fk_liquidacion_items_liquidaciones").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiquidacionItem>().WithMany().HasForeignKey(x => x.RelanzadoDesde)
            .HasConstraintName("fk_liquidacion_items_relanzado_desde").OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Placa).HasMaxLength(10).HasComment("@pii:low Placa normalizada (S16).");
        builder.Property(x => x.Estado).HasConversion(CodigosDeEnum.Convertidor<EstadoItem>()).HasMaxLength(32);
        builder.Property(x => x.ErrorCodigo).HasMaxLength(48);
        builder.Property(x => x.ErrorClase).HasConversion(CodigosDeEnum.Convertidor<ClaseError>()).HasMaxLength(16);
        builder.Property(x => x.ErrorMensaje).HasMaxLength(500);
        builder.Property(x => x.CampoFaltante).HasMaxLength(64);
        builder.Property(x => x.DepartamentoCodigo).HasMaxLength(2);
        builder.Property(x => x.DepartamentoNombre).HasMaxLength(80);
        builder.Property(x => x.SecretariaCiudad).HasMaxLength(80);
        builder.Property(x => x.SecretariaNombre).HasMaxLength(120).HasComment("@pii:none Nombre de la secretaría de tránsito (Item.secretaria.nombre).");
        builder.Property(x => x.DocumentoTipo).HasMaxLength(3);
        builder.Property(x => x.DocumentoNumeroCifrado).HasComment("@pii:high Número de documento del propietario, cifrado (impuestos.pii.v1).");
        builder.Property(x => x.PropietarioNombreCifrado).HasComment("@pii:high Nombre del propietario, cifrado (impuestos.pii.v1).");
        builder.Property(x => x.ConsultaApiId).HasMaxLength(64);
        builder.Property(x => x.TokenHash).HasComment("SHA-256 del token del callbackUrl; el token no se guarda.");
        builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_liquidacion_items_token_hash");
        builder.Property(x => x.ConCallback).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.IntentosRunt).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.IntentosReenvio).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.AttemptApiMax).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.ConsultaStatus).HasMaxLength(12);
        builder.Property(x => x.ResultadoStatus).HasMaxLength(12);
        builder.Property(x => x.Parcial).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.TotalAPagar).HasPrecision(18, 2).HasComment("NULL no es 0 (F2-CF14).");
        builder.Property(x => x.ResultadoCifrado).HasComment("@pii:high Cuerpo completo del resultado, cifrado (impuestos.resultado.v1).");
        builder.Property(x => x.Warnings).HasColumnType("text[]").HasDefaultValueSql("'{}'").ValueGeneratedNever();
        builder.Property(x => x.ResultadoTardio).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.PosibleDuplicado).HasDefaultValue(false).ValueGeneratedNever()
            .HasComment("Solo filas anteriores a la 1.3.0: desde entonces relanzar abre un nuevo intento del mismo ítem (R3) y no se marca.");
        builder.Property(x => x.NumeroIntento).HasDefaultValue(1).ValueGeneratedNever()
            .HasComment("Intento en curso (1..n). Los cerrados están en liquidacion_item_intentos (R3).");
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => new { x.LiquidacionId, x.Estado }).HasDatabaseName("ix_items_liq");
        builder.HasIndex(x => x.ProximoPollEn).HasFilter("estado IN ('ENVIADO','INCIERTO')").HasDatabaseName("ix_items_poll");
        builder.HasIndex(x => new { x.TenantId, x.Placa }).HasDatabaseName("ix_items_tenant_placa");
        // Conteo del crédito en vuelo (DespachadorCredito.EnVuelo) y búsqueda de los pendientes a liberar.
        builder.HasIndex(x => x.TenantId).HasFilter($"estado IN ({ListaSql(EnVuelo)})").HasDatabaseName("ix_liquidacion_items_en_vuelo");
        builder.HasIndex(x => new { x.TenantId, x.LiquidacionId }).HasFilter($"estado = {ListaSql([EstadoItem.Pendiente])}")
            .HasDatabaseName("ix_liquidacion_items_pendientes");
    }
}
