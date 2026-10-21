using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.vehiculos</c> (HU #13735, plan §5, MOD02): vehículos del lote. La base garantiza que el identificador
/// normalizado no se repite en el lote (<c>ux_vehiculo_por_lote</c>), que va en mayúsculas y sin espacios, que su clase es
/// conocida (incluida <c>PLACA_MAQUINARIA</c>, R-19) y que el documento del locatario cifrado siempre trae su origen
/// (<c>ck_locatario_origen</c>, R-3). La FK es compuesta <c>(tenant_id, diagnostico_id)</c> contra el alterno de la cabecera:
/// un vehículo no cuelga de un lote de otra empresa ni por SQL directo. El índice único empieza por <c>tenant_id</c> y
/// cubre la FK. Desviaciones del checklist de datos: ADR-0072.
/// </summary>
internal sealed class VehiculoDiagnosticoConfig : IEntityTypeConfiguration<VehiculoDiagnostico>
{
    public void Configure(EntityTypeBuilder<VehiculoDiagnostico> builder)
    {
        builder.ToTable("vehiculos", t =>
        {
            t.HasComment("Vehículos del lote (MOD02, HU #13735): identificador normalizado único por lote y documentos cifrados.");
            t.HasCheckConstraint("ck_vehiculos_tipo_identificador", $"tipo_identificador IN ({CodigosDeEnum.ListaSql<TipoIdentificador>()})");
            t.HasCheckConstraint(
                "ck_vehiculos_locatario_doc_origen",
                $"locatario_doc_origen IS NULL OR locatario_doc_origen IN ({CodigosDeEnum.ListaSql<OrigenDocumento>()})");
            t.HasCheckConstraint("ck_locatario_origen", "(locatario_doc_cifrado IS NULL) = (locatario_doc_origen IS NULL)");
            t.HasCheckConstraint("ck_vehiculos_identificador_norm", @"identificador_norm = upper(identificador_norm) AND identificador_norm !~ '\s'");
            t.HasCheckConstraint("ck_vehiculos_datos_consolidados_objeto", "jsonb_typeof(datos_consolidados) = 'object'");
        });
        builder.HasKey(x => x.Id).HasName("pk_vehiculos");
        // Sostiene las FK compuestas de las celdas y los intentos (HU #13723, ADR-0072 (c)).
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_vehiculos_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote (la misma de la cabecera).");
        builder.Property(x => x.DiagnosticoId).HasComment("Cabecera del lote.");
        builder.Property(x => x.IdentificadorOriginal).HasMaxLength(32).HasComment("@pii:low Identificador tal como lo escribió el cliente (placa o VIN).");
        builder.Property(x => x.IdentificadorNorm).HasMaxLength(17).HasComment("@pii:low Identificador en mayúsculas y sin espacios; único en el lote.");
        builder.Property(x => x.TipoIdentificador)
            .HasConversion(CodigosDeEnum.Convertidor<TipoIdentificador>()).HasMaxLength(16)
            .HasComment("PLACA_CARRO, PLACA_MOTO, PLACA_REMOLQUE, PLACA_MAQUINARIA o VIN (R-19).");
        builder.Property(x => x.Placa).HasMaxLength(10).HasComment("@pii:low Placa del vehículo.");
        builder.Property(x => x.Vin).HasMaxLength(17).HasComment("@pii:low VIN del vehículo.");
        builder.Property(x => x.Servicio).HasMaxLength(32).HasComment("Servicio normalizado (vehicle_service del RUNT).");
        builder.Property(x => x.OrganismoNombre).HasMaxLength(200).HasComment("Organismo de tránsito (transit_office_name del RUNT).");
        builder.Property(x => x.DepartamentoCodigo).HasMaxLength(8).HasComment("Departamento derivado de la réplica de organismos.");
        builder.Property(x => x.PropietarioDocTipo).HasMaxLength(8).HasComment("Tipo de documento del propietario, hidratado del RUNT (D-02).");
        builder.Property(x => x.PropietarioDocCifrado).HasComment("@pii:high Documento del propietario cifrado (plan §9.3). El nombre no se guarda.");
        builder.Property(x => x.LocatarioDocTipo).HasMaxLength(8).HasComment("Tipo de documento del locatario.");
        builder.Property(x => x.LocatarioDocCifrado).HasComment("@pii:high Documento del locatario cifrado, como el del propietario (R-3).");
        builder.Property(x => x.LocatarioDocOrigen)
            .HasConversion(CodigosDeEnum.Convertidor<OrigenDocumento>()).HasMaxLength(20)
            .HasComment("PROVEEDOR (autollenado de Intempo) o DECLARADO_CLIENTE (respaldo de la captura); null sin documento.");
        builder.Property(x => x.DatosConsolidados)
            .HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").ValueGeneratedNever()
            .HasComment("@pii:low Datos consolidados de las fuentes (objeto JSON), sin documentos en claro.");
        builder.Property(x => x.RuntConsultadoEn).HasComment("Instante de la última consulta al RUNT; reutilización en Trámites (R-14).");
        builder.Property(x => x.EstadoConsultas)
            .HasMaxLength(16).HasDefaultValueSql("'PENDIENTE'").ValueGeneratedNever()
            .HasComment("Estado derivado de las celdas del vehículo.");
        builder.Property(x => x.Orden).HasComment("Posición del vehículo en el lote.");
        builder.Property(x => x.DiagnosticoId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_vehiculos_diagnosticos")
            .OnDelete(DeleteBehavior.Restrict);

        // Deduplicación por lote (12641 #2). Empieza por tenant_id y cubre la FK compuesta.
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.IdentificadorNorm }).IsUnique().HasDatabaseName("ux_vehiculo_por_lote");
        // Reutilización del mismo día por compañía y placa (D-10, HU #13723).
        builder.HasIndex(x => new { x.TenantId, x.Placa }).HasDatabaseName("ix_vehiculos_tenant_placa");

        // Concurrencia optimista con la columna de sistema xmin: no agrega columnas a la tabla.
        builder.Property(x => x.Version).IsRowVersion();
    }
}
