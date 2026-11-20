using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.personas_vehiculo</c> (HU #13790, F05-C3, decisión Q1): cada propietario y cada locatario del vehículo, con
/// el documento cifrado (<c>@pii:high</c>) y sin nombres. Clase (a) de ADR-0072: <c>xmin</c>, sin borrado lógico, FK compuesta
/// <c>(tenant_id, vehiculo_id)</c> contra <c>uq_vehiculos_tenant_id</c> con <c>RESTRICT</c>, y expone
/// <c>uq_personas_vehiculo_tenant_id</c> para la FK compuesta de <c>intentos_consulta.persona_id</c>. Una sola fila por vehículo,
/// rol y orden (<c>ux_personas_vehiculo_rol_orden</c>, que empieza por <c>tenant_id</c> y cubre la FK). Solo el locatario puede
/// venir declarado por el cliente.
/// </summary>
internal sealed class PersonaVehiculoConfig : IEntityTypeConfiguration<PersonaVehiculo>
{
    public void Configure(EntityTypeBuilder<PersonaVehiculo> builder)
    {
        builder.ToTable("personas_vehiculo", t =>
        {
            t.HasComment("Propietarios y locatarios del vehículo con documento cifrado y sin nombres (HU #13790, F05-C3).");
            t.HasCheckConstraint("ck_personas_vehiculo_rol", $"rol IN ({CodigosDeEnum.ListaSql<RolPersona>()})");
            t.HasCheckConstraint("ck_personas_vehiculo_origen", $"origen IN ({CodigosDeEnum.ListaSql<OrigenDocumento>()})");
            t.HasCheckConstraint("ck_personas_vehiculo_orden", "orden >= 1");
            t.HasCheckConstraint(
                "ck_personas_vehiculo_declarado_solo_locatario",
                $"origen = '{CodigosDeEnum.Codigo(OrigenDocumento.Proveedor)}' OR rol = '{CodigosDeEnum.Codigo(RolPersona.Locatario)}'");
        });
        builder.HasKey(x => x.Id).HasName("pk_personas_vehiculo");
        // Sostiene la FK compuesta de intentos_consulta.persona_id (ADR-0072 (c)).
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_personas_vehiculo_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del vehículo.");
        builder.Property(x => x.VehiculoId).HasComment("Vehículo al que pertenece la persona.");
        builder.Property(x => x.Rol).HasConversion(CodigosDeEnum.Convertidor<RolPersona>()).HasMaxLength(16).HasComment("PROPIETARIO o LOCATARIO.");
        builder.Property(x => x.Orden).HasComment("Posición dentro del rol, desde 1, en el orden del proveedor.");
        builder.Property(x => x.DocTipo).HasMaxLength(8).HasComment("Tipo de documento (CC, CE, NIT, PAS, TI).");
        builder.Property(x => x.DocCifrado).HasComment("@pii:high Número de documento cifrado con Data Protection (HU #13751). El nombre no se guarda.");
        builder.Property(x => x.Origen)
            .HasConversion(CodigosDeEnum.Convertidor<OrigenDocumento>()).HasMaxLength(20)
            .HasComment("PROVEEDOR (RUNT) o DECLARADO_CLIENTE (locatario declarado cuando el proveedor no trae ninguno).");
        builder.Property(x => x.CreadoEn).HasComment("Instante en que se registró (cierre de la celda RUNT).");

        builder.HasOne<VehiculoDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehiculoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_personas_vehiculo_vehiculos")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.VehiculoId, x.Rol, x.Orden }).IsUnique().HasDatabaseName("ux_personas_vehiculo_rol_orden");

        // Concurrencia optimista con la columna de sistema xmin: no agrega columnas a la tabla.
        builder.Property(x => x.Version).IsRowVersion();
    }
}
