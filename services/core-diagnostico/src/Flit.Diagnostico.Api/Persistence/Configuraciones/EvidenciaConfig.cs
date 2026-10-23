using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.evidencias</c> (HU #13846, plan §5 ajustado, MOD07, F07-C8/C11/C12, R-18): cada PDF, Excel y ZIP generado con la
/// huella de sus entradas y la referencia del archivo. Clase (a) de ADR-0072: <c>xmin</c>, sin borrado lógico ni físico (invalidar es
/// <c>vigente=false</c>), FK compuestas <c>(tenant_id, diagnostico_id)</c> y <c>(tenant_id, vehiculo_id)</c> con <c>RESTRICT</c>.
/// Una sola vigente por lote, vehículo y tipo (<c>ux_evidencias_vigente</c>, <c>NULLS NOT DISTINCT</c> porque el Excel y el ZIP no
/// tienen vehículo; Postgres 16 en los tres ambientes). El PDF es el único con vehículo y una LISTA es la única con archivo.
/// Sin origen de las celdas, usuario manual, proveedor ni transacción (R-20).
/// </summary>
internal sealed class EvidenciaConfig : IEntityTypeConfiguration<Evidencia>
{
    public void Configure(EntityTypeBuilder<Evidencia> builder)
    {
        var lista = CodigosDeEnum.Codigo(EstadoEvidencia.Lista);
        var enCola = $"'{CodigosDeEnum.Codigo(EstadoEvidencia.Pendiente)}', '{CodigosDeEnum.Codigo(EstadoEvidencia.Generando)}'";
        builder.ToTable("evidencias", t =>
        {
            t.HasComment("PDF, Excel y ZIP generados con la huella de sus entradas y su archivo en el file-manager (MOD07, HU #13846, R-18).");
            t.HasCheckConstraint("ck_evidencias_tipo", $"tipo IN ({CodigosDeEnum.ListaSql<TipoEvidencia>()})");
            t.HasCheckConstraint("ck_evidencias_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoEvidencia>()})");
            t.HasCheckConstraint("ck_evidencias_huella", "huella ~ '^[0-9a-f]{64}$'");
            // El PDF es del vehículo; el Excel y el ZIP, del lote.
            t.HasCheckConstraint("ck_evidencias_pdf_vehiculo", $"(tipo = '{CodigosDeEnum.Codigo(TipoEvidencia.Pdf)}') = (vehiculo_id IS NOT NULL)");
            // Lista si y solo si hay archivo; la lista además tiene tamaño y fecha de generación.
            t.HasCheckConstraint(
                "ck_evidencias_lista_archivo",
                $"(estado = '{lista}') = (archivo_ref IS NOT NULL) AND (estado <> '{lista}' OR (tamano_bytes IS NOT NULL AND generado_en IS NOT NULL))");
            t.HasCheckConstraint("ck_evidencias_tipo_error", $"tipo_error IS NULL OR estado = '{CodigosDeEnum.Codigo(EstadoEvidencia.Error)}'");
            t.HasCheckConstraint("ck_evidencias_tamano", "tamano_bytes IS NULL OR tamano_bytes >= 0");
            t.HasCheckConstraint("ck_evidencias_intentos", "intentos >= 0");
            t.HasCheckConstraint("ck_evidencias_vehiculos_zip", "vehiculos_incluidos >= 0 AND vehiculos_pendientes >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_evidencias");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_evidencias_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote.");
        builder.Property(x => x.DiagnosticoId).HasComment("Lote de la evidencia.");
        builder.Property(x => x.VehiculoId).HasComment("Vehículo del PDF; null en el Excel y el ZIP del lote.");
        builder.Property(x => x.Tipo)
            .HasConversion(CodigosDeEnum.Convertidor<TipoEvidencia>()).HasMaxLength(8)
            .HasComment("PDF, XLSX o ZIP.");
        builder.Property(x => x.Huella)
            .HasColumnType("char(64)")
            .HasComment("SHA-256 hex de las entradas del archivo (plantilla, cabecera, encabezado y celdas con su versión); si no cambia, no se regenera.");
        builder.Property(x => x.Estado)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoEvidencia>()).HasMaxLength(16)
            .HasComment("PENDIENTE, GENERANDO, LISTA o ERROR; PDF y XLSX nacen LISTA, el ZIP pasa por la cola (F07-H5).");
        builder.Property(x => x.ArchivoRef).HasMaxLength(256).HasComment("Referencia del archivo en el file-manager (o db: en pruebas); solo con estado LISTA.");
        builder.Property(x => x.TamanoBytes).HasComment("Tamaño del archivo en bytes; obligatorio con estado LISTA.");
        builder.Property(x => x.SolicitadoPor).HasComment("@pii:low Usuario que pidió el archivo (claim sub).");
        builder.Property(x => x.EmpresaNombre)
            .HasMaxLength(200)
            .HasComment("Nombre de la empresa del claim company_name para el ZIP, que se genera sin sesión (F07-H5).");
        builder.Property(x => x.Vigente).HasComment("La que se entrega hoy para su clave; false = reemplazada y conservada (R-18).");
        builder.Property(x => x.Intentos).HasDefaultValue(0).ValueGeneratedNever().HasComment("Intentos de generación (cola del ZIP).");
        builder.Property(x => x.LeaseHasta).HasComment("Cola del ZIP: vencimiento del lease (GENERANDO) o inicio del reintento con backoff (PENDIENTE); null si nadie lo tiene.");
        builder.Property(x => x.VehiculosIncluidos).HasDefaultValue(0).ValueGeneratedNever()
            .HasComment("ZIP: vehículos con su PDF en el archivo (al pedirlo, los esperados; LISTA, los que trae). 0 en PDF y XLSX.");
        builder.Property(x => x.VehiculosPendientes).HasDefaultValue(0).ValueGeneratedNever()
            .HasComment("ZIP: vehículos fuera del archivo por no haber terminado (van en el índice). 0 en PDF y XLSX.");
        builder.Property(x => x.TipoError).HasMaxLength(32).HasComment("Causa del fallo de generación; solo con estado ERROR.");
        builder.Property(x => x.CreadoEn).HasComment("Instante del pedido (timestamptz).");
        builder.Property(x => x.GeneradoEn).HasComment("Instante en que el archivo quedó listo; null mientras no lo está.");
        builder.Property(x => x.DiagnosticoId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.VehiculoId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.Tipo).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.Huella).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_evidencias_diagnosticos")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VehiculoDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehiculoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_evidencias_vehiculos")
            .OnDelete(DeleteBehavior.Restrict);

        // Una sola vigente por clave; NULLS NOT DISTINCT para que el Excel y el ZIP (sin vehículo) tampoco se dupliquen.
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.VehiculoId, x.Tipo })
            .IsUnique().AreNullsDistinct(false).HasFilter("vigente").HasDatabaseName("ux_evidencias_vigente");
        // Cubren las FK (el índice de vigentes es parcial) y la lectura de las evidencias de un lote.
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.Tipo }).HasDatabaseName("ix_evidencias_diagnostico");
        builder.HasIndex(x => new { x.TenantId, x.VehiculoId }).HasDatabaseName("ix_evidencias_vehiculo");
        // Cola del ZIP de todas las empresas (F07-H5), como ix_cola de consultas.
        builder.HasIndex(x => new { x.Estado, x.LeaseHasta })
            .HasFilter($"tipo = '{CodigosDeEnum.Codigo(TipoEvidencia.Zip)}' AND estado IN ({enCola})")
            .HasDatabaseName("ix_evidencias_cola");

        // HU #13850: un solo ZIP en la cola por lote y huella; dos pedidos a la vez chocan aquí (23505) y el perdedor entrega el ganador.
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.Huella })
            .IsUnique()
            .HasFilter($"tipo = '{CodigosDeEnum.Codigo(TipoEvidencia.Zip)}' AND estado IN ({enCola})")
            .HasDatabaseName("ux_evidencias_zip_en_cola");

        // Concurrencia optimista con la columna de sistema xmin: dos pedidos no invalidan a la vez la misma vigente.
        builder.Property(x => x.Version).IsRowVersion();
    }
}
