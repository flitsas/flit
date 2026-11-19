using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.diagnosticos</c> (HU #13738, plan §5, MOD01): cabecera del lote. La base garantiza los códigos de modo y
/// estado, que Con Análisis IA traiga tipo de trámite (<c>ck_tipo_con_analisis</c>) y que el alcance sea un arreglo de
/// fuentes conocidas con RUNT. Modo, tipo de trámite, express, alcance, configuración, creador y creación son inmutables
/// después de guardar (EF lanza si cambian). Las fechas son instantes <c>timestamptz</c>; el corte del día en hora Colombia
/// (D-10) se calcula al leer, no se guarda. El alterno <c>(tenant_id, id)</c> sostiene la FK compuesta del historial, que
/// impide colgar una fila de otra empresa. Desviaciones del checklist de datos: ADR-0072.
/// </summary>
internal sealed class CabeceraDiagnosticoConfig : IEntityTypeConfiguration<CabeceraDiagnostico>
{
    public void Configure(EntityTypeBuilder<CabeceraDiagnostico> builder)
    {
        var runt = CodigosDeEnum.Codigo(Fuente.Runt);
        builder.ToTable("diagnosticos", t =>
        {
            t.HasComment("Cabecera del diagnóstico (MOD01, HU #13738): lote con su alcance y su configuración inmutables.");
            t.HasCheckConstraint("ck_tipo_con_analisis", $"modo = '{CodigosDeEnum.Codigo(ModoDiagnostico.Simple)}' OR tipo_tramite_codigo IS NOT NULL");
            t.HasCheckConstraint("ck_diagnosticos_modo", $"modo IN ({CodigosDeEnum.ListaSql<ModoDiagnostico>()})");
            t.HasCheckConstraint("ck_diagnosticos_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoDiagnostico>()})");
            t.HasCheckConstraint(
                "ck_diagnosticos_alcance_valido",
                $"jsonb_typeof(alcance) = 'array' AND alcance <@ {CodigosDeEnum.ArregloJsonbSql<Fuente>()} AND alcance @> '[\"{runt}\"]'::jsonb");
            t.HasCheckConstraint("ck_diagnosticos_configuracion_objeto", "jsonb_typeof(configuracion) = 'object'");
            t.HasCheckConstraint("ck_diagnosticos_estimado_consultas_objeto", "estimado_consultas IS NULL OR jsonb_typeof(estimado_consultas) = 'object'");
        });
        builder.HasKey(x => x.Id).HasName("pk_diagnosticos");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_diagnosticos_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote.");
        builder.Property(x => x.Nombre).HasMaxLength(150).HasComment("Nombre del lote dado por el usuario.");
        builder.Property(x => x.Modo)
            .HasConversion(CodigosDeEnum.Convertidor<ModoDiagnostico>()).HasMaxLength(16)
            .HasComment("Plan comercial: SIMPLE o CON_ANALISIS (Con Análisis IA, R-2).");
        builder.Property(x => x.TipoTramiteCodigo).HasMaxLength(64).HasComment("Código de tramites.procedure_types; obligatorio en CON_ANALISIS.");
        builder.Property(x => x.EsExpress).HasDefaultValueSql("false").ValueGeneratedNever().HasComment("Creado por la captura express.");
        builder.Property(x => x.Alcance)
            .HasColumnType("jsonb")
            .HasConversion(CodigosDeEnum.ConvertidorDeLista<Fuente>(), CodigosDeEnum.ComparadorDeLista<Fuente>())
            .HasComment("Fuentes a consultar (arreglo de códigos de Fuente); RUNT siempre.");
        builder.Property(x => x.Configuracion).HasColumnType("jsonb").HasComment("Copia inmutable de la configuración de la compañía al guardar.");
        builder.Property(x => x.EstimadoConsultas).HasColumnType("jsonb").HasComment("Estimado de consultas por fuente al confirmar (R-11); null hasta confirmar.");
        builder.Property(x => x.Estado)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoDiagnostico>()).HasMaxLength(16)
            .HasComment("Estado del lote: CREADO, EN_PROCESO, COMPLETADO o INCOMPLETO (plan §3.5).");
        builder.Property(x => x.VersionTablero)
            .HasDefaultValueSql("0").ValueGeneratedNever()
            .HasComment("Sube con cada cambio de celda; polling del tablero (?desde=).");
        builder.Property(x => x.UsuarioCreadorId).HasComment("@pii:low Usuario que creó el lote (claim sub, seudónimo).");
        builder.Property(x => x.CreadoEn).HasComment("Instante de creación (timestamptz).");
        builder.Property(x => x.CerradoEn).HasComment("Instante de cierre (COMPLETADO o INCOMPLETO); null mientras sigue abierto.");

        foreach (var inmutable in new[]
                 {
                     nameof(CabeceraDiagnostico.Modo), nameof(CabeceraDiagnostico.TipoTramiteCodigo), nameof(CabeceraDiagnostico.EsExpress),
                     nameof(CabeceraDiagnostico.Alcance), nameof(CabeceraDiagnostico.Configuracion),
                     nameof(CabeceraDiagnostico.UsuarioCreadorId), nameof(CabeceraDiagnostico.CreadoEn),
                 })
        {
            builder.Property(inmutable).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        // Listado de lotes de la empresa, los más recientes primero.
        builder.HasIndex(x => new { x.TenantId, x.CreadoEn }).IsDescending(false, true).HasDatabaseName("ix_diag_tenant_creado");

        // Concurrencia optimista con la columna de sistema xmin: no agrega columnas a la tabla.
        builder.Property(x => x.Version).IsRowVersion();
    }
}
