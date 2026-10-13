using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Comparendos.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>comparendos</c>), del que es dueño su usuario de base (HU #13330). Modelo de
/// la especificación GDC §3: nombres en snake_case (convención), enumeraciones como texto con su nombre salvo
/// <see cref="TipoIngreso"/> (smallint, como en V1) y toda tabla de empresa con <c>tenant_id</c>.
/// </summary>
public sealed class ComparendosDb(DbContextOptions<ComparendosDb> options) : DbContext(options)
{
    public const string Schema = ServicioSettings.Codigo;

    public DbSet<ArchivoLocal> ArchivosLocales => Set<ArchivoLocal>();
    public DbSet<Archivo> Archivos => Set<Archivo>();
    public DbSet<AuditoriaConfiguracion> AuditoriasConfiguracion => Set<AuditoriaConfiguracion>();
    public DbSet<CalendarioDia> CalendarioDias => Set<CalendarioDia>();
    public DbSet<InfraccionCatalogo> InfraccionesCatalogo => Set<InfraccionCatalogo>();
    public DbSet<ConfiguracionEmpresa> ConfiguracionesEmpresa => Set<ConfiguracionEmpresa>();
    public DbSet<IdentificacionMonitoreada> IdentificacionesMonitoreadas => Set<IdentificacionMonitoreada>();
    public DbSet<PlacaMonitoreada> PlacasMonitoreadas => Set<PlacaMonitoreada>();
    public DbSet<RepresentanteLegal> RepresentantesLegales => Set<RepresentanteLegal>();
    public DbSet<OrganismoTransito> OrganismosTransito => Set<OrganismoTransito>();
    public DbSet<OrganismoTransitoAlias> OrganismosTransitoAlias => Set<OrganismoTransitoAlias>();
    public DbSet<PermisoOtEmpresa> PermisosOtEmpresa => Set<PermisoOtEmpresa>();
    public DbSet<MunicipioFuente> MunicipiosFuente => Set<MunicipioFuente>();
    public DbSet<TipologiaDp> Tipologias => Set<TipologiaDp>();
    public DbSet<TipologiaEmpresa> TipologiasEmpresa => Set<TipologiaEmpresa>();
    public DbSet<Plantilla> Plantillas => Set<Plantilla>();
    public DbSet<PlantillaVersion> PlantillaVersiones => Set<PlantillaVersion>();
    public DbSet<TrabajoProgramado> TrabajosProgramados => Set<TrabajoProgramado>();
    public DbSet<SchedulerAviso> SchedulerAvisos => Set<SchedulerAviso>();
    public DbSet<AlertaConfiguracion> AlertaConfiguraciones => Set<AlertaConfiguracion>();
    public DbSet<AlertaDestinatario> AlertaDestinatarios => Set<AlertaDestinatario>();
    public DbSet<PromptOcrVersion> PromptsOcr => Set<PromptOcrVersion>();
    public DbSet<ConfiguracionConsultas> ConfiguracionesConsultas => Set<ConfiguracionConsultas>();
    public DbSet<ConfiguracionConsultaCanal> ConfiguracionConsultaCanales => Set<ConfiguracionConsultaCanal>();
    public DbSet<Comparendo> Comparendos => Set<Comparendo>();
    public DbSet<ComparendoFuente> ComparendoFuentes => Set<ComparendoFuente>();
    public DbSet<ComparendoCambio> ComparendoCambios => Set<ComparendoCambio>();
    public DbSet<TimelineEvento> Timeline => Set<TimelineEvento>();
    public DbSet<CicloIngesta> CiclosIngesta => Set<CicloIngesta>();
    public DbSet<ConsultaFuente> ConsultasFuente => Set<ConsultaFuente>();
    public DbSet<LogMigracion> LogMigracion => Set<LogMigracion>();
    public DbSet<Contraventor> Contraventores => Set<Contraventor>();
    public DbSet<ContraventorHistorial> ContraventorHistorial => Set<ContraventorHistorial>();
    public DbSet<DocumentoSoporte> DocumentosSoporte => Set<DocumentoSoporte>();
    public DbSet<AvisoConductor> AvisosConductor => Set<AvisoConductor>();
    public DbSet<CambioContraventorSolicitud> SolicitudesCambio => Set<CambioContraventorSolicitud>();
    public DbSet<PortalEnlace> PortalEnlaces => Set<PortalEnlace>();
    public DbSet<ComunicacionSaliente> Comunicaciones => Set<ComunicacionSaliente>();
    public DbSet<ComunicacionAdjunto> ComunicacionAdjuntos => Set<ComunicacionAdjunto>();
    public DbSet<ComunicacionIntento> ComunicacionIntentos => Set<ComunicacionIntento>();
    public DbSet<DerechoPeticion> DerechosPeticion => Set<DerechoPeticion>();
    public DbSet<ConsecutivoRadicado> ConsecutivosRadicado => Set<ConsecutivoRadicado>();
    public DbSet<LoteDp> LotesDp => Set<LoteDp>();
    public DbSet<LoteDpItem> LoteDpItems => Set<LoteDpItem>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<CargaMasivaPagos> CargasMasivasPagos => Set<CargaMasivaPagos>();
    public DbSet<CargaMasivaPagosFila> CargaMasivaPagosFilas => Set<CargaMasivaPagosFila>();
    public DbSet<PaqueteOcr> PaquetesOcr => Set<PaqueteOcr>();
    public DbSet<DocumentoOcr> DocumentosOcr => Set<DocumentoOcr>();
    public DbSet<DocumentoOcrCampo> DocumentoOcrCampos => Set<DocumentoOcrCampo>();
    public DbSet<DocumentoOcrEdicion> DocumentoOcrEdiciones => Set<DocumentoOcrEdicion>();
    public DbSet<DocumentoOcrIntento> DocumentoOcrIntentos => Set<DocumentoOcrIntento>();
    public DbSet<Exportacion> Exportaciones => Set<Exportacion>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        foreach (var tipo in typeof(EstadoGestion).Assembly.GetTypes()
                     .Where(t => t.IsEnum && t.Namespace == typeof(EstadoGestion).Namespace && t != typeof(TipoIngreso)))
        {
            configurationBuilder.Properties(tipo).HaveConversion<string>().HaveMaxLength(40);
        }

        configurationBuilder.Properties<decimal>().HavePrecision(14, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddFlitOutbox(Schema);
        modelBuilder.AddFlitInbox(Schema);

        Base(modelBuilder);
        Configuracion(modelBuilder);
        ComparendosEIngesta(modelBuilder);
        Gestion(modelBuilder);
        Ocr(modelBuilder);
    }

    private static void Base(ModelBuilder m)
    {
        m.Entity<ArchivoLocal>(e =>
        {
            e.ToTable("archivo_local");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(60);
            e.Property(x => x.Nombre).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(100);
        });
        m.Entity<Archivo>(e =>
        {
            e.ToTable("archivo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Referencia).HasMaxLength(200);
            e.Property(x => x.Dominio).HasMaxLength(60);
            e.Property(x => x.NombreOriginal).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => new { x.TenantId, x.Sha256 });
        });
        m.Entity<AuditoriaConfiguracion>(e =>
        {
            e.ToTable("auditoria_configuracion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Entidad).HasMaxLength(60);
            e.Property(x => x.EntidadId).HasMaxLength(100);
            e.Property(x => x.Accion).HasMaxLength(30);
            e.Property(x => x.Cambios).HasColumnType("jsonb");
            e.Property(x => x.UsuarioNombre).HasMaxLength(254);
            e.HasIndex(x => new { x.Entidad, x.EntidadId, x.RegistradoEn });
            e.HasIndex(x => new { x.TenantId, x.RegistradoEn });
        });
        m.Entity<CalendarioDia>(e =>
        {
            e.ToTable("calendario_dia");
            e.HasKey(x => x.Fecha);
            e.Property(x => x.DescripcionFestivo).HasMaxLength(120);
            e.Property(x => x.Origen).HasMaxLength(20);
        });
        m.Entity<InfraccionCatalogo>(e =>
        {
            e.ToTable("infraccion_catalogo");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasMaxLength(10);
        });
    }

    private static void Configuracion(ModelBuilder m)
    {
        m.Entity<ConfiguracionEmpresa>(e =>
        {
            e.ToTable("configuracion_empresa");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.EmpresaNombre).HasMaxLength(200);
            e.Property(x => x.RemitenteNombre).HasMaxLength(120);
            e.Property(x => x.CorreoRespuesta).HasMaxLength(254);
            e.Property(x => x.Cc).HasColumnType("jsonb");
            e.Property(x => x.Bcc).HasColumnType("jsonb");
        });
        m.Entity<IdentificacionMonitoreada>(e =>
        {
            e.ToTable("identificacion_monitoreada");
            e.HasKey(x => x.Id);
            e.Property(x => x.NumeroBase).HasMaxLength(15);
            e.Property(x => x.NombreTitular).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.TipoDocumento, x.NumeroBase }).IsUnique();
            e.HasIndex(x => new { x.TipoDocumento, x.NumeroBase }).HasFilter("activo");
        });
        m.Entity<PlacaMonitoreada>(e =>
        {
            e.ToTable("placa_monitoreada");
            e.HasKey(x => x.Id);
            e.Property(x => x.Placa).HasMaxLength(7);
            e.Property(x => x.OrigenRegistro).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Placa }).IsUnique();
            e.HasIndex(x => x.Placa).HasFilter("activo");
        });
        m.Entity<RepresentanteLegal>(e =>
        {
            e.ToTable("representante_legal");
            e.HasKey(x => x.Id);
            e.Property(x => x.NumeroDocumento).HasMaxLength(20);
            e.Property(x => x.Nombres).HasMaxLength(200);
            e.Property(x => x.Cargo).HasMaxLength(120);
            e.Property(x => x.Correo).HasMaxLength(254);
            e.Property(x => x.Telefono).HasMaxLength(30);
            e.Property(x => x.DireccionNotificacion).HasMaxLength(250);
            e.HasIndex(x => x.TenantId).IsUnique().HasFilter("alcance = 'Empresa' AND activo").HasDatabaseName("ux_representante_legal_empresa_activo");
            e.HasIndex(x => new { x.TenantId, x.IdentificacionMonitoreadaId }).IsUnique()
                .HasFilter("alcance = 'Identificacion' AND activo").HasDatabaseName("ux_representante_legal_nit_activo");
        });
        m.Entity<OrganismoTransito>(e =>
        {
            e.ToTable("organismo_transito");
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.CodigoDivipola).HasMaxLength(10);
            e.Property(x => x.Departamento).HasMaxLength(100);
            e.Property(x => x.Municipio).HasMaxLength(100);
            e.HasIndex(x => x.Codigo).IsUnique();
        });
        m.Entity<OrganismoTransitoAlias>(e =>
        {
            e.ToTable("organismo_transito_alias");
            e.HasKey(x => x.Id);
            e.Property(x => x.TextoNormalizado).HasMaxLength(250);
            e.Property(x => x.Origen).HasMaxLength(20);
            e.HasIndex(x => x.TextoNormalizado).IsUnique();
            e.HasOne<OrganismoTransito>().WithMany().HasForeignKey(x => x.OrganismoTransitoId);
        });
        m.Entity<PermisoOtEmpresa>(e =>
        {
            e.ToTable("permiso_ot_empresa");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.OrganismoTransitoId, x.RepresentanteLegalId }).IsUnique().AreNullsDistinct(false);
            e.HasOne<OrganismoTransito>().WithMany().HasForeignKey(x => x.OrganismoTransitoId);
        });
        m.Entity<MunicipioFuente>(e =>
        {
            e.ToTable("municipio_fuente");
            e.HasKey(x => x.Id);
            e.Property(x => x.CodigoFuente).HasMaxLength(60);
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.HasIndex(x => x.CodigoFuente).IsUnique();
        });
        m.Entity<TipologiaDp>(e =>
        {
            e.ToTable("tipologia_dp");
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.Property(x => x.Nombre).HasMaxLength(150);
            e.Property(x => x.PrefijoRadicado).HasMaxLength(6);
            e.Property(x => x.LegacyV1Codigo).HasMaxLength(10);
            e.HasIndex(x => x.Codigo).IsUnique();
        });
        m.Entity<TipologiaEmpresa>(e =>
        {
            e.ToTable("tipologia_empresa");
            e.HasKey(x => new { x.TenantId, x.TipologiaId });
            e.HasOne<TipologiaDp>().WithMany().HasForeignKey(x => x.TipologiaId);
        });
        m.Entity<Plantilla>(e =>
        {
            e.ToTable("plantilla");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(150);
            e.HasIndex(x => new { x.Conjunto, x.Tipo, x.TipologiaId }).IsUnique().AreNullsDistinct(false);
        });
        m.Entity<PlantillaVersion>(e =>
        {
            e.ToTable("plantilla_version");
            e.HasKey(x => x.Id);
            e.Property(x => x.Asunto).HasMaxLength(300);
            e.HasIndex(x => new { x.PlantillaId, x.NumeroVersion }).IsUnique();
            e.HasOne<Plantilla>().WithMany().HasForeignKey(x => x.PlantillaId);
        });
        m.Entity<ConfiguracionConsultas>(e =>
        {
            e.ToTable("configuracion_consultas");
            e.HasKey(x => x.TenantId);
            e.HasIndex(x => x.Estado);
        });
        m.Entity<ConfiguracionConsultaCanal>(e =>
        {
            e.ToTable("configuracion_consulta_canal");
            e.HasKey(x => x.Id);
            e.Property(x => x.Proveedor).HasMaxLength(40);
            e.Property(x => x.Modo).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Canal }).IsUnique();
            e.HasOne<ConfiguracionConsultas>().WithMany().HasForeignKey(x => x.TenantId);
        });
        m.Entity<TrabajoProgramado>(e =>
        {
            e.ToTable("trabajo_programado");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExpresionCron).HasMaxLength(120);
            e.HasIndex(x => x.Tipo).IsUnique();
        });
        m.Entity<SchedulerAviso>(e =>
        {
            e.ToTable("scheduler_aviso");
            e.HasKey(x => x.TipoAviso);
        });
        m.Entity<AlertaConfiguracion>(e =>
        {
            e.ToTable("alerta_configuracion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.PlantillaAsunto).HasMaxLength(200);
            e.Property(x => x.RemitenteNombre).HasMaxLength(120);
        });
        m.Entity<AlertaDestinatario>(e =>
        {
            e.ToTable("alerta_destinatario");
            e.HasKey(x => x.Id);
            e.Property(x => x.Correo).HasMaxLength(254);
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.HasIndex(x => x.Correo).IsUnique();
        });
        m.Entity<PromptOcrVersion>(e =>
        {
            e.ToTable("prompt_ocr_version");
            e.HasKey(x => x.Id);
            e.Property(x => x.Proveedor).HasMaxLength(40);
            e.Property(x => x.Modelo).HasMaxLength(80);
            e.HasIndex(x => x.NumeroVersion).IsUnique();
            e.HasIndex(x => x.Publicada).IsUnique().HasFilter("publicada");
        });
    }

    private static void ComparendosEIngesta(ModelBuilder m)
    {
        m.Entity<Comparendo>(e =>
        {
            e.ToTable("comparendo", t =>
            {
                t.HasCheckConstraint("ck_comparendo_inactivo_con_motivo", "activo OR motivo_inactivacion IS NOT NULL");
                t.HasCheckConstraint("ck_comparendo_ocr_con_evidencia", "tipo_ingreso NOT IN (2, 3) OR archivo_evidencia_id IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.TipoIngreso).HasConversion<short>();
            e.Property(x => x.Numero).HasMaxLength(40);
            e.Property(x => x.NumeroNormalizado).HasMaxLength(40);
            e.Property(x => x.Placa).HasMaxLength(7);
            e.Property(x => x.NitConsultado).HasMaxLength(20);
            e.Property(x => x.NumeroDocumentoInfractor).HasMaxLength(20);
            e.Property(x => x.NombreInfractor).HasMaxLength(200);
            e.Property(x => x.CodigoInfraccion).HasMaxLength(10);
            e.Property(x => x.DireccionInfraccion).HasMaxLength(250);
            e.Property(x => x.SecretariaTexto).HasMaxLength(250);
            e.Property(x => x.TipoVehiculo).HasMaxLength(60);
            e.Property(x => x.ServicioVehiculo).HasMaxLength(60);
            e.Property(x => x.EstadoFuente).HasMaxLength(120);
            e.Property(x => x.LegacyV1Id).HasMaxLength(60);
            e.HasIndex(x => new { x.TenantId, x.NumeroNormalizado }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Activo, x.FechaLimiteGestion });
            e.HasIndex(x => new { x.TenantId, x.Placa });
            e.HasIndex(x => new { x.TenantId, x.IdentificacionMonitoreadaId });
            e.HasIndex(x => new { x.TenantId, x.OrganismoTransitoId });
            e.HasIndex(x => new { x.TenantId, x.EstadoGestion });
            e.HasIndex(x => new { x.TenantId, x.EstadoCambioContraventor });
            e.HasIndex(x => new { x.TenantId, x.EstadoNotificacionConductor });
            e.HasIndex(x => new { x.TenantId, x.FechaImposicion });
            e.HasIndex(x => new { x.TenantId, x.FechaNotificacion });
            e.HasIndex(x => new { x.TenantId, x.CreadoEn });
            e.HasIndex(x => new { x.TenantId, x.UltimoEventoEn });
            e.HasIndex(x => x.TenantId).HasFilter("activo AND NOT pagado AND NOT notificado_fisicamente").HasDatabaseName("ix_comparendo_avisos");
            e.HasOne(x => x.Contraventor).WithOne().HasForeignKey<Contraventor>(c => c.ComparendoId);
        });
        m.Entity<ComparendoFuente>(e =>
        {
            e.ToTable("comparendo_fuente");
            e.HasKey(x => x.Id);
            e.Property(x => x.MunicipioCodigo).HasMaxLength(60);
            e.Property(x => x.DatosNormalizados).HasColumnType("jsonb");
            e.Property(x => x.HashDatos).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => new { x.ComparendoId, x.Canal, x.IdentificacionMonitoreadaId, x.PlacaMonitoreadaId, x.MunicipioCodigo })
                .IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => new { x.TenantId, x.Canal, x.IdentificacionMonitoreadaId, x.MunicipioCodigo }).HasFilter("presente")
                .HasDatabaseName("ix_comparendo_fuente_presentes_nit");
            e.HasIndex(x => new { x.TenantId, x.Canal, x.PlacaMonitoreadaId, x.MunicipioCodigo }).HasFilter("presente")
                .HasDatabaseName("ix_comparendo_fuente_presentes_placa");
            e.HasOne<Comparendo>().WithMany().HasForeignKey(x => x.ComparendoId);
        });
        m.Entity<ComparendoCambio>(e =>
        {
            e.ToTable("comparendo_cambio");
            e.HasKey(x => x.Id);
            e.Property(x => x.Campo).HasMaxLength(60);
            e.HasIndex(x => new { x.ComparendoId, x.RegistradoEn });
            e.HasOne<Comparendo>().WithMany().HasForeignKey(x => x.ComparendoId);
        });
        m.Entity<TimelineEvento>(e =>
        {
            e.ToTable("timeline_evento");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorNombre).HasMaxLength(200);
            e.Property(x => x.Datos).HasColumnType("jsonb");
            e.Property(x => x.RelacionadoTipo).HasMaxLength(40);
            e.HasIndex(x => new { x.ComparendoId, x.OcurridoEn });
            e.HasIndex(x => new { x.TenantId, x.Tipo, x.OcurridoEn });
            e.HasOne<Comparendo>().WithMany().HasForeignKey(x => x.ComparendoId);
        });
        m.Entity<CicloIngesta>(e =>
        {
            e.ToTable("ciclo_ingesta");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Canal, x.InicioEn });
            // Un ciclo en curso por canal y empresa (los programados corren por empresa, HU #13521).
            e.HasIndex(x => new { x.Canal, x.TenantIdManual }).IsUnique().AreNullsDistinct(false).HasFilter("resultado = 'EnCurso'").HasDatabaseName("ux_ciclo_ingesta_en_curso");
        });
        m.Entity<ConsultaFuente>(e =>
        {
            e.ToTable("consulta_fuente");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClaveConsultada).HasMaxLength(20);
            e.Property(x => x.MunicipioCodigo).HasMaxLength(60);
            e.HasIndex(x => new { x.CicloId, x.Canal, x.ClaveConsultada, x.MunicipioCodigo });
            e.HasOne<CicloIngesta>().WithMany().HasForeignKey(x => x.CicloId);
        });
        m.Entity<LogMigracion>(e =>
        {
            e.ToTable("log_migracion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Clave).HasMaxLength(20);
            e.Property(x => x.MunicipioCodigo).HasMaxLength(60);
            e.HasIndex(x => new { x.TenantId, x.InicioEn });
            e.HasIndex(x => new { x.Canal, x.Resultado, x.InicioEn });
            e.HasOne<CicloIngesta>().WithMany().HasForeignKey(x => x.CicloId);
        });
    }

    private static void Gestion(ModelBuilder m)
    {
        m.Entity<Contraventor>(e =>
        {
            e.ToTable("contraventor");
            e.HasKey(x => x.ComparendoId);
            e.Property(x => x.NumeroDocumento).HasMaxLength(20);
            e.Property(x => x.Nombres).HasMaxLength(200);
            e.Property(x => x.Correo).HasMaxLength(254);
            e.Property(x => x.Telefono).HasMaxLength(30);
            e.Property(x => x.Direccion).HasMaxLength(250);
            e.Property(x => x.Ciudad).HasMaxLength(100);
            e.Property(x => x.Sucursal).HasMaxLength(120);
            e.Property(x => x.TipoContrato).HasMaxLength(120);
            e.HasIndex(x => new { x.TenantId, x.NumeroDocumento });
        });
        m.Entity<ContraventorHistorial>(e =>
        {
            e.ToTable("contraventor_historial");
            e.HasKey(x => x.Id);
            e.Property(x => x.DatosAnteriores).HasColumnType("jsonb");
            e.Property(x => x.DatosNuevos).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ComparendoId, x.RegistradoEn });
        });
        m.Entity<DocumentoSoporte>(e =>
        {
            e.ToTable("documento_soporte");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ComparendoId, x.Tipo });
        });
        m.Entity<AvisoConductor>(e =>
        {
            e.ToTable("aviso_conductor");
            e.HasKey(x => x.Id);
            e.Property(x => x.CorreoDestino).HasMaxLength(254);
            e.HasIndex(x => new { x.ComparendoId, x.TipoAviso, x.CorreoDestino }).IsUnique().HasFilter("disparo = 'Programado'")
                .HasDatabaseName("ux_aviso_conductor_programado");
        });
        m.Entity<CambioContraventorSolicitud>(e =>
        {
            e.ToTable("cambio_contraventor_solicitud");
            e.HasKey(x => x.Id);
            e.Property(x => x.ContraventorSnapshot).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ComparendoId, x.NumeroIntento }).IsUnique();
            e.HasIndex(x => x.ComparendoId).IsUnique().HasFilter("estado = 'Solicitado'").HasDatabaseName("ux_cambio_contraventor_abierta");
        });
        m.Entity<PortalEnlace>(e =>
        {
            e.ToTable("portal_enlace");
            e.HasKey(x => x.Id);
            e.Property(x => x.ContraventorDocumento).HasMaxLength(20);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.MotivoRevocacion).HasMaxLength(40);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.ComparendoId);
        });
        m.Entity<ComunicacionSaliente>(e =>
        {
            e.ToTable("comunicacion_saliente");
            e.HasKey(x => x.Id);
            e.Property(x => x.Remitente).HasMaxLength(254);
            e.Property(x => x.RemitenteNombre).HasMaxLength(120);
            e.Property(x => x.Destinatarios).HasColumnType("jsonb");
            e.Property(x => x.Asunto).HasMaxLength(300);
            e.Property(x => x.PlantillaClave).HasMaxLength(80);
            e.Property(x => x.EntregaIdPlataforma).HasMaxLength(64);
            e.Property(x => x.ResultadoPlataforma).HasMaxLength(60);
            e.HasIndex(x => new { x.TenantId, x.CreadoEn });
            e.HasIndex(x => new { x.TenantId, x.Tipo, x.Estado });
            e.HasIndex(x => x.ComparendoId);
        });
        m.Entity<ComunicacionAdjunto>(e =>
        {
            e.ToTable("comunicacion_adjunto");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(255);
            e.HasIndex(x => new { x.ComunicacionId, x.ArchivoId }).IsUnique();
        });
        m.Entity<ComunicacionIntento>(e =>
        {
            e.ToTable("comunicacion_intento");
            e.HasKey(x => x.Id);
            e.Property(x => x.ResultadoPlataforma).HasMaxLength(60);
            e.HasIndex(x => x.ComunicacionId);
        });
        m.Entity<DerechoPeticion>(e =>
        {
            e.ToTable("derecho_peticion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Radicado).HasMaxLength(40);
            e.Property(x => x.PeticionarioSnapshot).HasColumnType("jsonb");
            e.Property(x => x.EstadoLegalAlRadicar).HasMaxLength(20);
            e.Property(x => x.RespuestaRadicadoOt).HasMaxLength(60);
            e.Property(x => x.LegacyV1Id).HasMaxLength(60);
            e.HasIndex(x => x.ComparendoId).IsUnique();
            e.HasIndex(x => x.Radicado).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Estado });
            e.HasIndex(x => new { x.TenantId, x.FechaRadicacion });
            e.HasIndex(x => new { x.TenantId, x.RespuestaSentido });
        });
        m.Entity<ConsecutivoRadicado>(e =>
        {
            e.ToTable("consecutivo_radicado");
            e.HasKey(x => new { x.Prefijo, x.Anio });
            e.Property(x => x.Prefijo).HasMaxLength(6);
        });
        m.Entity<LoteDp>(e =>
        {
            e.ToTable("lote_dp");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.CreadoEn });
        });
        m.Entity<LoteDpItem>(e =>
        {
            e.ToTable("lote_dp_item");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LoteId, x.ComparendoId }).IsUnique();
        });
        m.Entity<Pago>(e =>
        {
            e.ToTable("pago", t => t.HasCheckConstraint("ck_pago_valor_positivo", "valor_pagado > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyV1Id).HasMaxLength(60);
            e.HasIndex(x => x.ComparendoId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.FechaPago });
            e.HasIndex(x => new { x.TenantId, x.RegistradoPor, x.RegistradoEn });
        });
        m.Entity<CargaMasivaPagos>(e =>
        {
            e.ToTable("carga_masiva_pagos");
            e.HasKey(x => x.Id);
            e.Property(x => x.NombreArchivo).HasMaxLength(255);
            e.Property(x => x.CargadoPorNombre).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.CargadoEn });
        });
        m.Entity<CargaMasivaPagosFila>(e =>
        {
            e.ToTable("carga_masiva_pagos_fila");
            e.HasKey(x => x.Id);
            e.Property(x => x.Datos).HasColumnType("jsonb");
            e.HasIndex(x => new { x.CargaId, x.NumeroFila });
        });
        m.Entity<Exportacion>(e =>
        {
            e.ToTable("exportacion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Filtros).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.CreadoEn });
        });
    }

    private static void Ocr(ModelBuilder m)
    {
        m.Entity<PaqueteOcr>(e =>
        {
            e.ToTable("paquete_ocr");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(150);
            e.Property(x => x.CreadoPorNombre).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.CreadoEn });
            e.HasIndex(x => new { x.TenantId, x.Estado });
        });
        m.Entity<DocumentoOcr>(e =>
        {
            e.ToTable("documento_ocr");
            e.HasKey(x => x.Id);
            e.Property(x => x.NombreArchivo).HasMaxLength(255);
            e.Property(x => x.TipoArchivo).HasMaxLength(10);
            e.Property(x => x.ProveedorModelo).HasMaxLength(120);
            e.Property(x => x.RespuestaLlm).HasColumnType("jsonb");
            e.Property(x => x.NumeroComparendoExtraido).HasMaxLength(40);
            e.Property(x => x.NumeroNormalizado).HasMaxLength(40);
            e.Property(x => x.FuenteDuplicado).HasMaxLength(20);
            e.HasIndex(x => new { x.PaqueteId, x.Estado });
            e.HasIndex(x => new { x.TenantId, x.NumeroNormalizado });
            e.HasIndex(x => new { x.Estado, x.ProximoIntentoEn });
            e.HasOne<PaqueteOcr>().WithMany().HasForeignKey(x => x.PaqueteId);
        });
        m.Entity<DocumentoOcrCampo>(e =>
        {
            e.ToTable("documento_ocr_campo");
            e.HasKey(x => x.Id);
            e.Property(x => x.NombreCampo).HasMaxLength(60);
            e.Property(x => x.Confianza).HasPrecision(3, 2);
            e.HasIndex(x => new { x.DocumentoOcrId, x.NombreCampo }).IsUnique();
            e.HasOne<DocumentoOcr>().WithMany().HasForeignKey(x => x.DocumentoOcrId);
        });
        m.Entity<DocumentoOcrEdicion>(e =>
        {
            e.ToTable("documento_ocr_edicion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Campo).HasMaxLength(60);
            e.Property(x => x.UsuarioNombre).HasMaxLength(200);
            e.HasIndex(x => x.DocumentoOcrId);
        });
        m.Entity<DocumentoOcrIntento>(e =>
        {
            e.ToTable("documento_ocr_intento");
            e.HasKey(x => x.Id);
            e.Property(x => x.Resultado).HasMaxLength(10);
            e.HasIndex(x => x.DocumentoOcrId);
        });
    }

    /// <summary>Opciones comunes: Npgsql con la tabla de migraciones dentro del propio esquema y nombres en snake_case.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
                npgsql.EnableRetryOnFailure(3);
            })
            .UseSnakeCaseNamingConvention();
}

/// <summary>Para <c>dotnet ef migrations add</c>: no necesita una base de verdad.</summary>
internal sealed class ComparendosDbDesignFactory : IDesignTimeDbContextFactory<ComparendosDb>
{
    public ComparendosDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ComparendosDb>();
        ComparendosDb.Configure(options, "Host=localhost;Database=diseno");
        return new ComparendosDb(options.Options);
    }
}
