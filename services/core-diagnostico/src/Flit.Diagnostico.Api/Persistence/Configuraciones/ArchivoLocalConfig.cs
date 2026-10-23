using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.archivo_local</c> (HU #13846, patrón <c>comparendos.archivo_local</c>): los bytes de los archivos generados cuando
/// no hay file-manager, solo con <see cref="OpcionesAlmacen.GuardarEnBase"/> (pruebas automáticas). De la empresa del token (filtro
/// global de ADR-0072); solo se inserta.
/// </summary>
internal sealed class ArchivoLocalConfig : IEntityTypeConfiguration<ArchivoLocal>
{
    public void Configure(EntityTypeBuilder<ArchivoLocal> builder)
    {
        builder.ToTable("archivo_local", t =>
        {
            t.HasComment("Archivos generados guardados en la base sin file-manager; solo pruebas automáticas (HU #13846). Solo inserción.");
            t.HasCheckConstraint("ck_archivo_local_tipo", $"tipo IN ({CodigosDeEnum.ListaSql<TipoEvidencia>()})");
        });
        builder.HasKey(x => x.Id).HasName("pk_archivo_local");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación; la referencia es db:{id}.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del archivo.");
        builder.Property(x => x.Tipo).HasMaxLength(8).HasComment("PDF, XLSX o ZIP.");
        builder.Property(x => x.Nombre).HasMaxLength(200).HasComment("Nombre del archivo.");
        builder.Property(x => x.ContentType).HasMaxLength(100).HasComment("Tipo de contenido.");
        builder.Property(x => x.Contenido).HasComment("@pii:medium Bytes del archivo del cliente (documentos enmascarados, sin nombres de personas).");
        builder.Property(x => x.CreadoEn).HasComment("Instante en que se guardó.");
        builder.HasIndex(x => new { x.TenantId, x.CreadoEn }).HasDatabaseName("ix_archivo_local_tenant");
    }
}
