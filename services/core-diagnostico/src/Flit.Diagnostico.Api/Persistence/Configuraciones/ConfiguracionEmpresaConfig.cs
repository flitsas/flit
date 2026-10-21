using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.configuracion_empresa</c> (HU #13736, plan §5, D-04 = b): una fila por empresa, con <c>tenant_id</c> como
/// clave. Los valores por defecto y las reglas (subconjunto, límite positivo, códigos válidos) los garantiza la base, para
/// que una escritura por SQL directo tampoco deje la configuración inconsistente. EF siempre envía sus valores (los mismos
/// por defecto, desde la entidad); los DEFAULT de la base cubren la fila insertada solo con <c>tenant_id</c>. Sin retención
/// ni purga (R-18). Desviaciones del checklist de datos: ADR-0072.
/// </summary>
internal sealed class ConfiguracionEmpresaConfig : IEntityTypeConfiguration<ConfiguracionEmpresa>
{
    private const string FuentesPorDefectoSql = "'[\"RUNT\"]'::jsonb";

    public void Configure(EntityTypeBuilder<ConfiguracionEmpresa> builder)
    {
        var fuentesValidas = CodigosDeEnum.ArregloJsonbSql<Fuente>();
        builder.ToTable("configuracion_empresa", t =>
        {
            t.HasComment("Configuración de Diagnóstico por compañía (D-04: nivel contractual y operativo). Sin retención ni purga (R-18).");
            t.HasCheckConstraint("ck_por_defecto_subconjunto", "fuentes_habilitadas @> fuentes_por_defecto");
            t.HasCheckConstraint("ck_configuracion_empresa_limite_positivo", "limite_vehiculos_lote > 0");
            t.HasCheckConstraint(
                "ck_configuracion_empresa_fuentes_habilitadas_validas",
                $"jsonb_typeof(fuentes_habilitadas) = 'array' AND fuentes_habilitadas <@ {fuentesValidas}");
            t.HasCheckConstraint(
                "ck_configuracion_empresa_fuentes_por_defecto_validas",
                $"jsonb_typeof(fuentes_por_defecto) = 'array' AND fuentes_por_defecto <@ {fuentesValidas}");
            t.HasCheckConstraint("ck_configuracion_empresa_visibilidad_usuario", $"visibilidad_usuario IN ({CodigosDeEnum.ListaSql<VisibilidadUsuario>()})");
            t.HasCheckConstraint("ck_configuracion_empresa_estimado_consultas", $"estimado_consultas IN ({CodigosDeEnum.ListaSql<EstimadoConsultas>()})");
        });
        builder.HasKey(x => x.TenantId).HasName("pk_configuracion_empresa");
        builder.Property(x => x.TenantId).ValueGeneratedNever().HasComment("Empresa dueña de la configuración (una fila por empresa).");

        // Nivel contractual: solo backoffice FLIT.
        builder.Property(x => x.FuentesHabilitadas)
            .HasColumnType("jsonb")
            .HasConversion(CodigosDeEnum.ConvertidorDeLista<Fuente>(), CodigosDeEnum.ComparadorDeLista<Fuente>())
            .HasDefaultValueSql(FuentesPorDefectoSql).ValueGeneratedNever()
            .HasComment("Contractual: fuentes contratadas (arreglo de códigos de Fuente).");
        builder.Property(x => x.LimiteVehiculosLote)
            .HasDefaultValueSql("500").ValueGeneratedNever()
            .HasComment("Contractual: máximo de vehículos por lote (> 0).");
        builder.Property(x => x.ContratoActualizadoPor).HasComment("@pii:low Usuario del backoffice del último cambio contractual (seudónimo).");
        builder.Property(x => x.ContratoActualizadoEn).HasComment("Momento del último cambio contractual.");
        builder.Property(x => x.RedaccionIaSuspendida)
            .HasDefaultValueSql("false").ValueGeneratedNever()
            .HasComment("Interruptor de emergencia del backoffice para la redacción IA (R-2). No se muestra al cliente.");

        // Nivel operativo: admin de la compañía.
        builder.Property(x => x.VisibilidadUsuario)
            .HasConversion(CodigosDeEnum.Convertidor<VisibilidadUsuario>()).HasMaxLength(8)
            .HasDefaultValueSql("'EMPRESA'").ValueGeneratedNever()
            .HasComment("Operativa: lotes que ve cada usuario (PROPIOS o EMPRESA, D-01).");
        builder.Property(x => x.FuentesPorDefecto)
            .HasColumnType("jsonb")
            .HasConversion(CodigosDeEnum.ConvertidorDeLista<Fuente>(), CodigosDeEnum.ComparadorDeLista<Fuente>())
            .HasDefaultValueSql(FuentesPorDefectoSql).ValueGeneratedNever()
            .HasComment("Operativa: fuentes premarcadas al crear un diagnóstico (subconjunto de las habilitadas).");
        builder.Property(x => x.EstimadoConsultas)
            .HasConversion(CodigosDeEnum.Convertidor<EstimadoConsultas>()).HasMaxLength(16)
            .HasDefaultValueSql("'NO_MOSTRAR'").ValueGeneratedNever()
            .HasComment("Operativa: cuándo se muestra el estimado de consultas (R-11, R-11b).");
        builder.Property(x => x.OperativaActualizadaPor).HasComment("@pii:low Admin de la compañía del último cambio operativo (seudónimo).");
        builder.Property(x => x.OperativaActualizadaEn).HasComment("Momento del último cambio operativo.");

        // Concurrencia optimista con la columna de sistema xmin: no agrega columnas a la tabla.
        builder.Property(x => x.Version).IsRowVersion();
    }
}
