using Microsoft.EntityFrameworkCore;

namespace Flit.Platform.Sdk.IaConsumo;

/// <summary>
/// Una fila de <c>ia_consumo_log</c> (contrato ia-consumo v1): una por cada llamada HTTP real al proveedor de IA,
/// salga bien o mal. Nunca lleva el archivo, su base64, llaves ni encabezados.
/// </summary>
public sealed class IaConsumoLog
{
    public Guid Id { get; set; }
    public Guid CorrelacionId { get; set; }
    public short Intento { get; set; } = 1;
    public short Pasada { get; set; } = 1;
    public DateTimeOffset FechaInicio { get; set; }
    public DateTimeOffset FechaFin { get; set; }
    public int LatenciaMs { get; set; }
    public string Aplicativo { get; set; } = string.Empty;
    public string Servicio { get; set; } = string.Empty;
    public string Ambiente { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? UsuarioId { get; set; }
    public string? UsuarioLogin { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string Modulo { get; set; } = string.Empty;
    public string Proceso { get; set; } = string.Empty;
    public string TipoTarea { get; set; } = string.Empty;
    public string? Placa { get; set; }
    public string? EntidadTipo { get; set; }
    public string? EntidadId { get; set; }
    public string? ArchivoMime { get; set; }
    public long? ArchivoBytes { get; set; }
    public int? ArchivoPaginas { get; set; }
    public string? ArchivoSha256 { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? ModeloSolicitado { get; set; }
    public string? ModeloRespondido { get; set; }
    public string? IdRespuestaProveedor { get; set; }
    public int? MaxTokens { get; set; }
    public string? PromptCodigo { get; set; }
    public string? PromptVersion { get; set; }
    public string? PromptHash { get; set; }
    public int HttpStatus { get; set; }
    public string Resultado { get; set; } = string.Empty;
    public string? StopReason { get; set; }
    public string? ErrorCodigo { get; set; }
    public string? ErrorMensaje { get; set; }
    public int TokensEntrada { get; set; }
    public int TokensSalida { get; set; }
    public int TokensCacheLectura { get; set; }
    public int TokensCacheEscritura { get; set; }
    public decimal? CostoUsdEstimado { get; set; }
    /// <summary>JSON libre (texto); se guarda como <c>jsonb</c>.</summary>
    public string? Metadatos { get; set; }
}

/// <summary>Prompt y respuesta completos de una llamada; solo con <c>IA_CONSUMO_DETALLE=true</c> (contrato regla 5).</summary>
public sealed class IaConsumoDetalle
{
    public Guid ConsumoId { get; set; }
    public DateTimeOffset FechaInicio { get; set; }
    public string? PromptTexto { get; set; }
    public string? RespuestaTexto { get; set; }
}

/// <summary>Tarifa vigente de un modelo, en USD por millón de tokens (<c>ia_tarifa_modelo</c>).</summary>
public sealed class IaTarifaModelo
{
    public int Id { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public DateOnly VigenteDesde { get; set; }
    public decimal PrecioEntradaMtok { get; set; }
    public decimal PrecioSalidaMtok { get; set; }
    public decimal PrecioCacheLecturaMtok { get; set; }
    public decimal PrecioCacheEscrituraMtok { get; set; }
    public string Moneda { get; set; } = "USD";
}

public static class IaConsumoModelBuilderExtensions
{
    /// <summary>
    /// Mapea las tres tablas del contrato ia-consumo v1 en <paramref name="schema"/>. Nombres de columna explícitos para
    /// que no dependan de la convención del <c>DbContext</c> (mismo criterio que la outbox).
    /// </summary>
    /// <param name="excluirDeMigraciones">
    /// true cuando las tablas las crea un script DDL propio (core-api) y no la migración generada por EF.
    /// </param>
    public static ModelBuilder AddFlitIaConsumo(this ModelBuilder modelBuilder, string schema, bool excluirDeMigraciones = false)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        modelBuilder.Entity<IaConsumoLog>(e =>
        {
            e.ToTable("ia_consumo_log", schema, t =>
            {
                if (excluirDeMigraciones)
                    t.ExcludeFromMigrations();
            });
            e.HasKey(x => x.Id).HasName("pk_ia_consumo_log");
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CorrelacionId).HasColumnName("correlacion_id");
            e.Property(x => x.Intento).HasColumnName("intento").HasDefaultValue((short)1);
            e.Property(x => x.Pasada).HasColumnName("pasada").HasDefaultValue((short)1);
            e.Property(x => x.FechaInicio).HasColumnName("fecha_inicio");
            e.Property(x => x.FechaFin).HasColumnName("fecha_fin");
            e.Property(x => x.LatenciaMs).HasColumnName("latencia_ms");
            e.Property(x => x.Aplicativo).HasColumnName("aplicativo").HasColumnType("text").IsRequired();
            e.Property(x => x.Servicio).HasColumnName("servicio").HasColumnType("text").IsRequired();
            e.Property(x => x.Ambiente).HasColumnName("ambiente").HasColumnType("text").IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("text");
            e.Property(x => x.UsuarioId).HasColumnName("usuario_id").HasColumnType("text");
            e.Property(x => x.UsuarioLogin).HasColumnName("usuario_login").HasColumnType("text");
            e.Property(x => x.Origen).HasColumnName("origen").HasColumnType("text").IsRequired();
            e.Property(x => x.Modulo).HasColumnName("modulo").HasColumnType("text").IsRequired();
            e.Property(x => x.Proceso).HasColumnName("proceso").HasColumnType("text").IsRequired();
            e.Property(x => x.TipoTarea).HasColumnName("tipo_tarea").HasColumnType("text").IsRequired();
            e.Property(x => x.Placa).HasColumnName("placa").HasColumnType("text");
            e.Property(x => x.EntidadTipo).HasColumnName("entidad_tipo").HasColumnType("text");
            e.Property(x => x.EntidadId).HasColumnName("entidad_id").HasColumnType("text");
            e.Property(x => x.ArchivoMime).HasColumnName("archivo_mime").HasColumnType("text");
            e.Property(x => x.ArchivoBytes).HasColumnName("archivo_bytes");
            e.Property(x => x.ArchivoPaginas).HasColumnName("archivo_paginas");
            e.Property(x => x.ArchivoSha256).HasColumnName("archivo_sha256").HasColumnType("text");
            e.Property(x => x.Proveedor).HasColumnName("proveedor").HasColumnType("text").IsRequired();
            e.Property(x => x.ModeloSolicitado).HasColumnName("modelo_solicitado").HasColumnType("text");
            e.Property(x => x.ModeloRespondido).HasColumnName("modelo_respondido").HasColumnType("text");
            e.Property(x => x.IdRespuestaProveedor).HasColumnName("id_respuesta_proveedor").HasColumnType("text");
            e.Property(x => x.MaxTokens).HasColumnName("max_tokens");
            e.Property(x => x.PromptCodigo).HasColumnName("prompt_codigo").HasColumnType("text");
            e.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasColumnType("text");
            e.Property(x => x.PromptHash).HasColumnName("prompt_hash").HasColumnType("text");
            e.Property(x => x.HttpStatus).HasColumnName("http_status").HasDefaultValue(0);
            e.Property(x => x.Resultado).HasColumnName("resultado").HasColumnType("text").IsRequired();
            e.Property(x => x.StopReason).HasColumnName("stop_reason").HasColumnType("text");
            e.Property(x => x.ErrorCodigo).HasColumnName("error_codigo").HasColumnType("text");
            e.Property(x => x.ErrorMensaje).HasColumnName("error_mensaje").HasColumnType("text");
            e.Property(x => x.TokensEntrada).HasColumnName("tokens_entrada").HasDefaultValue(0);
            e.Property(x => x.TokensSalida).HasColumnName("tokens_salida").HasDefaultValue(0);
            e.Property(x => x.TokensCacheLectura).HasColumnName("tokens_cache_lectura").HasDefaultValue(0);
            e.Property(x => x.TokensCacheEscritura).HasColumnName("tokens_cache_escritura").HasDefaultValue(0);
            e.Property(x => x.CostoUsdEstimado).HasColumnName("costo_usd_estimado").HasPrecision(12, 6);
            e.Property(x => x.Metadatos).HasColumnName("metadatos").HasColumnType("jsonb");

            e.HasIndex(x => x.FechaInicio).HasDatabaseName("ix_ia_consumo_log_fecha");
            e.HasIndex(x => new { x.Modulo, x.FechaInicio }).HasDatabaseName("ix_ia_consumo_log_modulo_fecha");
            e.HasIndex(x => x.Placa).HasDatabaseName("ix_ia_consumo_log_placa").HasFilter("placa IS NOT NULL");
            e.HasIndex(x => new { x.UsuarioId, x.FechaInicio }).HasDatabaseName("ix_ia_consumo_log_usuario")
                .HasFilter("usuario_id IS NOT NULL");
            e.HasIndex(x => x.CorrelacionId).HasDatabaseName("ix_ia_consumo_log_correlacion");
        });

        modelBuilder.Entity<IaConsumoDetalle>(e =>
        {
            e.ToTable("ia_consumo_detalle", schema, t =>
            {
                if (excluirDeMigraciones)
                    t.ExcludeFromMigrations();
            });
            e.HasKey(x => x.ConsumoId).HasName("pk_ia_consumo_detalle");
            e.Property(x => x.ConsumoId).HasColumnName("consumo_id").ValueGeneratedNever();
            e.Property(x => x.FechaInicio).HasColumnName("fecha_inicio");
            e.Property(x => x.PromptTexto).HasColumnName("prompt_texto").HasColumnType("text");
            e.Property(x => x.RespuestaTexto).HasColumnName("respuesta_texto").HasColumnType("text");
            e.HasOne<IaConsumoLog>().WithOne().HasForeignKey<IaConsumoDetalle>(x => x.ConsumoId)
                .HasConstraintName("fk_ia_consumo_detalle_log").OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.FechaInicio).HasDatabaseName("ix_ia_consumo_detalle_fecha");
        });

        modelBuilder.Entity<IaTarifaModelo>(e =>
        {
            e.ToTable("ia_tarifa_modelo", schema, t =>
            {
                if (excluirDeMigraciones)
                    t.ExcludeFromMigrations();
            });
            e.HasKey(x => x.Id).HasName("pk_ia_tarifa_modelo");
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Proveedor).HasColumnName("proveedor").HasColumnType("text").IsRequired();
            e.Property(x => x.Modelo).HasColumnName("modelo").HasColumnType("text").IsRequired();
            e.Property(x => x.VigenteDesde).HasColumnName("vigente_desde");
            e.Property(x => x.PrecioEntradaMtok).HasColumnName("precio_entrada_mtok").HasPrecision(12, 6);
            e.Property(x => x.PrecioSalidaMtok).HasColumnName("precio_salida_mtok").HasPrecision(12, 6);
            e.Property(x => x.PrecioCacheLecturaMtok).HasColumnName("precio_cache_lectura_mtok").HasPrecision(12, 6).HasDefaultValue(0m);
            e.Property(x => x.PrecioCacheEscrituraMtok).HasColumnName("precio_cache_escritura_mtok").HasPrecision(12, 6).HasDefaultValue(0m);
            e.Property(x => x.Moneda).HasColumnName("moneda").HasColumnType("text").HasDefaultValue("USD").IsRequired();
            e.HasIndex(x => new { x.Proveedor, x.Modelo, x.VigenteDesde }).IsUnique()
                .HasDatabaseName("uq_ia_tarifa_modelo_proveedor_modelo_vigente");
        });

        return modelBuilder;
    }
}
