using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.vehiculos</c> (HU #13735, plan §5, MOD02): vehículos del lote. La base garantiza que el identificador
/// normalizado no se repite en el lote (<c>ux_vehiculo_por_lote</c>), que va en mayúsculas y sin espacios, que su clase es
/// conocida (incluida <c>PLACA_MAQUINARIA</c>, R-19) y que el documento del locatario cifrado siempre trae su origen
/// (<c>ck_locatario_origen</c>, R-3). El propietario actual declarado (HU #14582) va en <c>propietario_declarado_doc_*</c>, con tipo y
/// documento juntos, y <c>cambio_propietario</c> solo existe con declarado. La FK es compuesta <c>(tenant_id, diagnostico_id)</c> contra el alterno de la cabecera:
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
            // Propietario actual declarado (HU #14582, schema.md §1): tipo y documento juntos; cambio_propietario solo con declarado.
            t.HasCheckConstraint("ck_vehiculos_propietario_declarado", "(propietario_declarado_doc_tipo IS NULL) = (propietario_declarado_doc_cifrado IS NULL)");
            t.HasCheckConstraint("ck_vehiculos_cambio_propietario", "cambio_propietario IS NULL OR propietario_declarado_doc_cifrado IS NOT NULL");
            t.HasCheckConstraint("ck_vehiculos_identificador_norm", @"identificador_norm = upper(identificador_norm) AND identificador_norm !~ '\s'");
            t.HasCheckConstraint("ck_vehiculos_datos_consolidados_objeto", "jsonb_typeof(datos_consolidados) = 'object'");
            // Versión de fila del tablero (HU #14563, ADR-0086 D-1).
            t.HasCheckConstraint("ck_vehiculos_version_tablero", "version_tablero >= 0");
            // Copia del estado del saneamiento no anulado (HU #14576, ADR-0087): nunca ANULADO (sin saneamiento vigente es null).
            t.HasCheckConstraint("ck_vehiculos_saneamiento_estado",
                $"saneamiento_estado IS NULL OR saneamiento_estado IN ('{CodigosDeEnum.Codigo(EstadoSaneamiento.Solicitado)}', '{CodigosDeEnum.Codigo(EstadoSaneamiento.EnGestion)}', '{CodigosDeEnum.Codigo(EstadoSaneamiento.Saneado)}', '{CodigosDeEnum.Codigo(EstadoSaneamiento.NoProcede)}')");
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
            .HasComment("PLACA_CARRO, PLACA_MOTO, PLACA_REMOLQUE, PLACA_MAQUINARIA, PLACA_MOTOCARRO o VIN (R-19).");
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
            .HasComment("PROVEEDOR (autollenado de Intempo); DECLARADO_CLIENTE solo en filas anteriores a HU #14582 (S3). Null sin documento.");
        builder.Property(x => x.PropietarioDeclaradoDocTipo).HasMaxLength(8)
            .HasComment("Tipo de documento del propietario actual declarado por el cliente (opcional, Express/Masivo). Solo para compararlo con el propietario RUNT; no alimenta consultas.");
        builder.Property(x => x.PropietarioDeclaradoDocCifrado)
            .HasComment("@pii:high Documento del propietario actual declarado por el cliente, cifrado con Data Protection (mismo IProtectorDocumentoPersona, HU #13751). Nunca en claro ni en logs.");
        builder.Property(x => x.CambioPropietario)
            .HasComment("true si el declarado (homologado) no coincide con ningún propietario RUNT (personas_vehiculo rol PROPIETARIO); false si coincide; null sin declarado o sin RUNT cerrado.");
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

        // Punto 4 (ADR-0084): el borrador creado en Trámites; se fija una vez y es la fuente de «Ver trámite #N» (y del índice del ZIP).
        builder.Property(x => x.TramiteId)
            .HasComment("Borrador creado en Trámites (origin=diagnostico, external_ref=id). Se fija una vez (R-16).");
        builder.Property(x => x.TramiteNumero).HasMaxLength(32).HasComment("Número (reference_number) del borrador en Trámites.");
        builder.Property(x => x.TramiteCreadoEn).HasComment("Instante en que Diagnóstico registró el borrador.");

        // Versión de fila del tablero (HU #14563, ADR-0086 D-1): sondeo ?desde= por fila; mismo patrón que la cabecera.
        builder.Property(x => x.VersionTablero)
            .HasDefaultValueSql("0").ValueGeneratedNever()
            .HasComment("Valor de diagnosticos.version_tablero en el último cambio de la FILA (dictamen nuevo o ajustado, trámite creado/omitido/fallido). 0 = sin cambios de fila. Polling ?desde=. [ADR-0086]");
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.VersionTablero }).HasDatabaseName("ix_vehiculos_tablero");

        // Saneamiento (HU #14576, ADR-0087): copia del estado para la grilla del lote y la de placas, sin join por fila.
        builder.Property(x => x.SaneamientoEstado)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoSaneamiento>()).HasMaxLength(16)
            .HasComment("Estado del saneamiento no anulado del vehículo (copia); null si no hay. Lo escribe solo EscritorSaneamiento, en la transacción de la transición. [ADR-0087]");
        builder.HasIndex(x => new { x.TenantId, x.SaneamientoEstado }).HasFilter("saneamiento_estado IS NOT NULL").HasDatabaseName("ix_vehiculos_saneamiento");
        builder.HasIndex(x => new { x.TenantId, x.IdentificadorNorm }, "ix_vehiculos_identificador_prefijo")
            .HasOperators("uuid_ops", "varchar_pattern_ops").HasDatabaseName("ix_vehiculos_identificador_prefijo");

        // Concurrencia optimista con la columna de sistema xmin: no agrega columnas a la tabla.
        builder.Property(x => x.Version).IsRowVersion();
    }
}
