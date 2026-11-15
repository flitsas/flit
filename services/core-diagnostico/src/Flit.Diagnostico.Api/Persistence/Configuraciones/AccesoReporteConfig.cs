using Flit.Diagnostico.Api.Modulos.Reportes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.accesos_reporte</c> (HU #14579, BE-0, SP-8, ADR-0088): registro de solo inserción de cada visualización y descarga del
/// módulo de reportes. Como <c>lecturas_backoffice</c> (excepción de ADR-0072): sin columnas de edición, borrado lógico ni
/// <c>row_version</c>; la aplicación rechaza modificarla o borrarla (<see cref="AccesoReporte.ValidarSoloInsercion"/>). Sin PII salvo
/// el seudónimo del usuario.
/// </summary>
internal sealed class AccesoReporteConfig : IEntityTypeConfiguration<AccesoReporte>
{
    /// <summary>
    /// CHECK de <c>filtros</c> (CR-BE-1, ADR-0072 §(d) criterio 7, al estilo de <see cref="NotificacionConfig.CheckDatos"/>): objeto plano
    /// de hasta 1 024 bytes, claves <c>^[a-z_]{1,32}$</c>, <c>placa_filtrada</c> solo como <c>true</c> y el resto arrays de códigos
    /// <c>^[A-Z0-9_]{1,32}$</c>. La base impide guardar el valor de una placa, un documento o un texto libre aunque la aplicación
    /// cambie. <c>silent</c> en <c>jsonb_path_exists</c>: con un no-objeto el primer término ya es falso y el CHECK falla.
    /// </summary>
    internal const string CheckFiltros =
        "jsonb_typeof(filtros) = 'object' AND octet_length(filtros::text) <= 1024 AND NOT jsonb_path_exists(filtros, 'strict $.keyvalue() ? (!(@.key like_regex \"^[a-z_]{1,32}$\"))', '{}', true) AND NOT jsonb_path_exists(filtros, 'strict $.keyvalue() ? (@.key == \"placa_filtrada\" && !(@.value.type() == \"boolean\" && @.value == true))', '{}', true) AND NOT jsonb_path_exists(filtros, 'strict $.keyvalue() ? (@.key != \"placa_filtrada\" && @.value.type() != \"array\")', '{}', true) AND NOT jsonb_path_exists(filtros, 'strict $.* ? (@.type() == \"array\")[*] ? (@.type() != \"string\" || !(@ like_regex \"^[A-Z0-9_]{1,32}$\"))', '{}', true)";

    public void Configure(EntityTypeBuilder<AccesoReporte> builder)
    {
        var tipos = string.Join(", ", Enum.GetValues<TipoReporte>().Select(t => $"'{TiposDeReporte.Codigo(t)}'"));
        var empresa = CodigosDeEnum.Codigo(AlcanceReporte.BackofficeEmpresa);
        builder.ToTable("accesos_reporte", t =>
        {
            t.HasComment("Registro de cada visualización y descarga del módulo de reportes (ADR-0088, Ley 1581 art. 17 lit. d). Sin PII. Retención: la de lecturas_backoffice (pendiente LT). Solo inserción.");
            t.HasCheckConstraint("ck_accesos_reporte_alcance", $"alcance IN ({CodigosDeEnum.ListaSql<AlcanceReporte>()})");
            t.HasCheckConstraint("ck_accesos_reporte_formato", $"formato IN ({CodigosDeEnum.ListaSql<FormatoReporte>()})");
            t.HasCheckConstraint("ck_accesos_reporte_tipo", $"tipo_reporte IN ({tipos})");
            t.HasCheckConstraint("ck_accesos_reporte_rango", "desde <= hasta");
            t.HasCheckConstraint("ck_accesos_reporte_filas", "filas >= 0");
            t.HasCheckConstraint("ck_accesos_reporte_empresas", "empresas >= 0");
            t.HasCheckConstraint("ck_accesos_reporte_filtros", CheckFiltros);
            t.HasCheckConstraint("ck_accesos_reporte_empresa_consultada", $"(alcance = '{empresa}') = (empresa_consultada IS NOT NULL)");
        });
        builder.HasKey(x => x.Id).HasName("pk_accesos_reporte");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación; se imprime en el PDF y en la hoja Aviso del Excel.");
        builder.Property(x => x.TenantId).HasComment("Compañía del lector (la del token), también en el backoffice.");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Usuario que vio o descargó (claim sub, seudónimo).");
        builder.Property(x => x.Alcance).HasConversion(CodigosDeEnum.Convertidor<AlcanceReporte>()).HasMaxLength(20)
            .HasComment("COMPANIA, BACKOFFICE_EMPRESA o BACKOFFICE_GLOBAL.");
        builder.Property(x => x.TipoReporte).HasConversion(new ValueConverter<TipoReporte, string>(v => TiposDeReporte.Codigo(v), c => Leer(c)))
            .HasMaxLength(16).HasComment("vehiculos, consultas, saneamientos, derivaciones o consumo.");
        builder.Property(x => x.Formato).HasConversion(CodigosDeEnum.Convertidor<FormatoReporte>()).HasMaxLength(8)
            .HasComment("PANTALLA, XLSX o PDF.");
        builder.Property(x => x.Desde).HasComment("Inicio del rango pedido (día de Colombia, inclusive).");
        builder.Property(x => x.Hasta).HasComment("Fin del rango pedido (día de Colombia, inclusive).");
        builder.Property(x => x.Filtros).HasColumnType("jsonb")
            .HasComment("Filtros de la lista blanca con valores de enum; la placa se guarda como placa_filtrada: true, nunca su valor.");
        builder.Property(x => x.EmpresaConsultada).HasComment("Compañía consultada; solo en BACKOFFICE_EMPRESA.");
        builder.Property(x => x.Filas).HasComment("Filas entregadas (la página o el archivo).");
        builder.Property(x => x.Empresas).HasComment("Compañías distintas entregadas.");
        builder.Property(x => x.Fecha).HasDefaultValueSql("now()").HasComment("Momento del acceso.");

        builder.HasIndex(x => new { x.TenantId, x.Fecha }).IsDescending(false, true).HasDatabaseName("ix_accesos_reporte_tenant_fecha");
    }

    private static TipoReporte Leer(string codigo) =>
        TiposDeReporte.TryLeer(codigo, out var tipo) ? tipo : throw new InvalidOperationException("tipo_reporte fuera del enum.");
}
