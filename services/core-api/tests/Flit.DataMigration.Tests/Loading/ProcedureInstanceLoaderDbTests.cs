using System.Globalization;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.DataMigration.Tests.Loading;

/// <summary>
/// Las únicas pruebas del migrador que abren una conexión de verdad — y existen porque su ausencia
/// costó cara.
///
/// <para>
/// Todo lo que rompe este cargador vive en la BASE, no en el C#: el trigger de inmutabilidad que
/// obliga a insertar en borrador, los triggers de denormalización que mueven <c>row_version</c> por
/// debajo de EF, las FK, los CHECK. Con mocks todo eso es invisible.
/// </para>
///
/// <para>
/// El 1 de agosto de 2026 se añadieron dos triggers de denormalización
/// (<c>47-tramites-campos-busqueda.sql</c>, para los listados filtrables) que hacen <c>UPDATE</c>
/// sobre <c>procedure_instances</c> al insertar campos y actores. Eso subió <c>row_version</c> de 0
/// a 4 y el paso final del migrador —el que aplica el estado real— empezó a fallar por concurrencia
/// optimista. Resultado: <b>el 99,4 % de los trámites de V1 se iban a cuarentena</b>, y las 39
/// pruebas del migrador seguían en verde porque ninguna tocaba Postgres.
/// </para>
///
/// <para>
/// El CI ya levanta <c>postgres:16-alpine</c>, aplica las migraciones y expone
/// <c>ConnectionStrings__Core</c> (ver <c>.github/workflows/core-api.yml</c>), así que estas pruebas
/// corren contra el esquema SIEMPRE al día. Si alguien vuelve a añadir un trigger que toque la fila
/// padre, se pone rojo en SU pull request y no cuando intentemos migrar una ola.
/// </para>
///
/// <para>
/// Sin base de datos se saltan en vez de fallar: quien clona el repo y corre <c>dotnet test</c> sin
/// Postgres no debería ver rojo por eso.
/// </para>
/// </summary>
public sealed class ProcedureInstanceLoaderDbTests
{
    /// <summary>
    /// <c>ConnectionStrings__Core</c> es la que ya inyecta el workflow del CI. La segunda es para
    /// correrlas a mano contra una copia local sin tocar la configuración de la API.
    /// </summary>
    private static string? ConexionV2() =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Core")
        ?? Environment.GetEnvironmentVariable("FLITMIG_TEST_V2");

    private static FlitDbContext Abrir(string conexion) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql(conexion)
            // OBLIGATORIO: el modelo está en PascalCase y las tablas en snake_case. Es el mismo
            // registro que hacen la consola y el host HTTP; con otro, nada resuelve.
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>
    /// La regresión que motivó todo esto: un trámite que NO queda en borrador debe migrarse.
    ///
    /// <para>
    /// Solo 336 de los 59.046 trámites de V1 están en borrador; el resto pasa por el paso que aquí
    /// se ejercita. Si esta prueba falla, el migrador no sirve — no importa cuántas otras pasen.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Un_tramite_en_estado_terminal_se_migra()
    {
        var conexion = ConexionV2();
        if (conexion is null)
        {
            Assert.Skip("Sin ConnectionStrings__Core: no hay base contra la que probar.");
        }

        await using var db = Abrir(conexion);
        var escenario = await Escenario.PrepararAsync(db, TestContext.Current.CancellationToken);

        try
        {
            var mapeado = escenario.Mapear(v1Id: 900_001, finalStatus: TramiteEstado.Aprobado);
            var loader = new ProcedureInstanceLoader(db, escenario.Libreta, "test-regresion");

            var resultado = await loader.LoadAsync(
                mapeado, dryRun: false, force: false, TestContext.Current.CancellationToken);

            resultado.Status.Should().Be(
                LoadStatus.Migrated,
                "un trámite en estado terminal debe migrarse; si esto es Quarantined con un error de "
                + "concurrencia, algún trigger nuevo está tocando procedure_instances al insertar "
                + $"sus hijos y el paso 4 se quedó con un row_version viejo. Motivo: {resultado.Reason}");

            var enBase = await db.ProcedureInstances
                .AsNoTracking()
                .FirstAsync(p => p.Id == mapeado.Instance.Id, TestContext.Current.CancellationToken);

            enBase.Status.Should().Be(TramiteEstado.Aprobado);
            enBase.IsMigrated.Should().BeTrue();
        }
        finally
        {
            await escenario.LimpiarAsync(db, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Los triggers de denormalización siguen haciendo su trabajo sobre un trámite migrado.
    ///
    /// <para>
    /// No es un extra: es la prueba de que el arreglo del <c>row_version</c> no se hizo apagando la
    /// denormalización. Un trámite migrado tiene que aparecer en los listados filtrables igual que
    /// uno nativo.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Un_tramite_migrado_queda_indexado_para_los_listados()
    {
        var conexion = ConexionV2();
        if (conexion is null)
        {
            Assert.Skip("Sin ConnectionStrings__Core: no hay base contra la que probar.");
        }

        await using var db = Abrir(conexion);
        var escenario = await Escenario.PrepararAsync(db, TestContext.Current.CancellationToken);

        try
        {
            var mapeado = escenario.Mapear(v1Id: 900_002, finalStatus: TramiteEstado.Aprobado);
            var loader = new ProcedureInstanceLoader(db, escenario.Libreta, "test-denorm");

            await loader.LoadAsync(mapeado, dryRun: false, force: false, TestContext.Current.CancellationToken);

            var enBase = await db.ProcedureInstances
                .AsNoTracking()
                .FirstAsync(p => p.Id == mapeado.Instance.Id, TestContext.Current.CancellationToken);

            enBase.Vin.Should().Be(Escenario.Vin);
            enBase.Plate.Should().Be(Escenario.Placa);
            enBase.VendedorNombre.Should().Be(Escenario.Vendedor);
            enBase.CompradorNombre.Should().Be(Escenario.Comprador);
        }
        finally
        {
            await escenario.LimpiarAsync(db, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Una entrada de libreta que apunta a un trámite inexistente NO cuenta como "ya migrado".
    ///
    /// <para>
    /// Es el caso que dejó ADR-0050 en dev: el reset borró los expedientes y la libreta —que no
    /// tiene FK— sobrevivió entera. Sin esta comprobación el migrador respondía «ya migrado» en
    /// verde sin escribir nada, que es la peor forma de fallar: indistinguible del éxito.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Una_entrada_huerfana_de_la_libreta_no_impide_re_migrar()
    {
        var conexion = ConexionV2();
        if (conexion is null)
        {
            Assert.Skip("Sin ConnectionStrings__Core: no hay base contra la que probar.");
        }

        await using var db = Abrir(conexion);
        var escenario = await Escenario.PrepararAsync(db, TestContext.Current.CancellationToken);

        try
        {
            var mapeado = escenario.Mapear(v1Id: 900_003, finalStatus: TramiteEstado.Aprobado);

            // La libreta dice que ya se migró, pero el trámite nunca se escribió: exactamente lo que
            // queda tras un borrado masivo del esquema `tramites`.
            await escenario.Libreta.RecordAsync(
                mapeado.V1Table, mapeado.V1Id, mapeado.Instance.Id, escenario.TenantId,
                "lote-viejo", TramiteEstado.Aprobado, [], TestContext.Current.CancellationToken);

            var loader = new ProcedureInstanceLoader(db, escenario.Libreta, "test-huerfana");
            var resultado = await loader.LoadAsync(
                mapeado, dryRun: false, force: false, TestContext.Current.CancellationToken);

            resultado.Status.Should().Be(
                LoadStatus.Migrated,
                $"la libreta apuntaba a un trámite que ya no existe, así que hay que volver a "
                + $"migrarlo. Motivo del fallo: {resultado.Reason}");
            resultado.Warnings.Should().Contain(w => w.Contains("ya no existe en V2", StringComparison.Ordinal));
        }
        finally
        {
            await escenario.LimpiarAsync(db, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Un trámite que SÍ sigue en V2 se salta, como siempre. El arreglo de la huérfana no puede
    /// haberse llevado por delante la idempotencia, que es la propiedad que hace seguro reintentar.
    /// </summary>
    [Fact]
    public async Task Un_tramite_que_sigue_en_V2_se_salta()
    {
        var conexion = ConexionV2();
        if (conexion is null)
        {
            Assert.Skip("Sin ConnectionStrings__Core: no hay base contra la que probar.");
        }

        await using var db = Abrir(conexion);
        var escenario = await Escenario.PrepararAsync(db, TestContext.Current.CancellationToken);

        try
        {
            var mapeado = escenario.Mapear(v1Id: 900_004, finalStatus: TramiteEstado.Aprobado);
            var loader = new ProcedureInstanceLoader(db, escenario.Libreta, "test-idempotencia");

            await loader.LoadAsync(mapeado, dryRun: false, force: false, TestContext.Current.CancellationToken);

            // Segunda pasada sobre un mapeo idéntico: el grafo se vuelve a construir, como haría una
            // corrida nueva del migrador.
            var otraVez = escenario.Mapear(v1Id: 900_004, finalStatus: TramiteEstado.Aprobado);
            var resultado = await loader.LoadAsync(
                otraVez, dryRun: false, force: false, TestContext.Current.CancellationToken);

            resultado.Status.Should().Be(LoadStatus.Skipped);
        }
        finally
        {
            await escenario.LimpiarAsync(db, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// El mínimo que V2 exige para poder insertar un trámite: un tenant, un tipo de trámite del
    /// catálogo y un usuario al que atribuir la carga. El tipo y las entidades de actor los siembran
    /// las migraciones; el tenant es propio de cada corrida para que dos ejecuciones no se pisen.
    /// </summary>
    private sealed class Escenario
    {
        internal const string Vin = "TESTVIN0000000001";
        internal const string Placa = "TST001";
        internal const string Vendedor = "VENDEDOR DE PRUEBA";
        internal const string Comprador = "COMPRADOR DE PRUEBA";

        private const string TablaV1 = "vehicle_transfer_master";

        internal required Guid TenantId { get; init; }
        internal required Guid ProcedureTypeId { get; init; }
        internal required Guid OwnerEntityId { get; init; }
        internal required Guid BuyerEntityId { get; init; }
        internal required Guid SystemUserId { get; init; }
        internal required MigrationMapStore Libreta { get; init; }

        internal static async Task<Escenario> PrepararAsync(FlitDbContext db, CancellationToken ct)
        {
            var libreta = new MigrationMapStore(db);
            await libreta.EnsureCreatedAsync(ct);

            var entorno = await TargetEnvironment.ResolveAsync(
                db, "TRASPASO_STANDARD", "migracion.tests@flitsas.io", ct);

            // Código único por corrida: `uq_tenants_code` no perdona, y un test que solo pasa la
            // primera vez es peor que no tenerlo.
            var sufijo = Guid.CreateVersion7().ToString("N")[..8];
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                Code = $"TEST-MIG-{sufijo}",
                LegalName = "Tenant de pruebas del migrador",
                TaxId = $"9{sufijo}",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                RowVersion = 0,
            };

            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            return new Escenario
            {
                TenantId = tenant.Id,
                ProcedureTypeId = entorno.ProcedureTypeId,
                OwnerEntityId = entorno.OwnerEntityId,
                BuyerEntityId = entorno.BuyerEntityId,
                SystemUserId = entorno.SystemUserId,
                Libreta = libreta,
            };
        }

        /// <summary>
        /// Un trámite con lo justo para disparar los cuatro triggers de denormalización: los campos
        /// <c>vin</c> y <c>plate</c>, y los actores <c>vendedor</c> y <c>comprador</c>. Es lo que
        /// mueve <c>row_version</c> de 0 a 4 antes de que el cargador aplique el estado final.
        /// </summary>
        internal MappedProcedure Mapear(long v1Id, string finalStatus)
        {
            var instanceId = DeterministicGuid.ForV1Row(TablaV1, v1Id);
            var ahora = DateTimeOffset.UtcNow;

            var instancia = new ProcedureInstance
            {
                Id = instanceId,
                TenantId = TenantId,
                ProcedureTypeId = ProcedureTypeId,
                ReferenceNumber = $"TEST-MIG-{v1Id.ToString(CultureInfo.InvariantCulture)}",
                Status = TramiteEstado.Borrador,
                ChecklistEstado = "{}",
                CreatedByUserId = SystemUserId,
                CreatedAt = ahora,
                RowVersion = 0,
                IsMigrated = true,
            };

            return new MappedProcedure
            {
                V1Id = v1Id,
                V1Table = TablaV1,
                Instance = instancia,
                Actors =
                [
                    Actor(v1Id, instanceId, "vendedor", OwnerEntityId, Vendedor, ahora),
                    Actor(v1Id, instanceId, "comprador", BuyerEntityId, Comprador, ahora),
                ],
                FieldValues =
                [
                    Campo(v1Id, instanceId, "vin", Vin, ahora),
                    Campo(v1Id, instanceId, "plate", Placa, ahora),
                ],
                StatusHistory =
                [
                    new ProcedureInstanceStatusHistory
                    {
                        Id = DeterministicGuid.ForV1Child(TablaV1, v1Id, "history:0"),
                        TenantId = TenantId,
                        ProcedureInstanceId = instanceId,
                        FromStatus = TramiteEstado.Borrador,
                        ToStatus = finalStatus,
                        ChangedAt = ahora,
                        ChangedBy = SystemUserId,
                        Metadata = "{}",
                    },
                ],
                FinalStatus = finalStatus,
                Warnings = [],
            };
        }

        private ProcedureInstanceActor Actor(
            long v1Id, Guid instanceId, string actorType, Guid entityId, string nombre, DateTimeOffset ahora) =>
            new()
            {
                Id = DeterministicGuid.ForV1Child(TablaV1, v1Id, $"actor:{actorType}"),
                TenantId = TenantId,
                ProcedureInstanceId = instanceId,
                ProcedureEntityId = entityId,
                ActorType = actorType,
                DocumentType = "CC",
                DocumentNumber = "1000000001",
                FullName = nombre,
                PersonType = ActorPersonTypes.Natural,
                EsRepresentanteLegal = false,
                Metadata = "{}",
                CreatedAt = ahora,
            };

        private ProcedureInstanceFieldValue Campo(
            long v1Id, Guid instanceId, string fieldKey, string valor, DateTimeOffset ahora) =>
            new()
            {
                Id = DeterministicGuid.ForV1Child(TablaV1, v1Id, $"field:{fieldKey}"),
                TenantId = TenantId,
                ProcedureInstanceId = instanceId,
                FormFieldId = null,
                FieldKey = fieldKey,
                ValueText = valor,
                Source = "migration",
                CreatedAt = ahora,
            };

        /// <summary>
        /// Deja la base como estaba. Va por SQL crudo y en orden de dependencia: el trigger de
        /// inmutabilidad bloquea incluso el <c>DELETE</c> de campos mientras el padre esté en un
        /// estado final, así que primero hay que devolverlo a borrador.
        /// </summary>
        internal async Task LimpiarAsync(FlitDbContext db, CancellationToken ct)
        {
            db.ChangeTracker.Clear();

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE tramites.procedure_instances SET status = 'borrador' WHERE tenant_id = {0};
                DELETE FROM tramites.procedure_instance_field_values   WHERE tenant_id = {0};
                DELETE FROM tramites.procedure_instance_actors         WHERE tenant_id = {0};
                DELETE FROM tramites.procedure_instance_status_history WHERE tenant_id = {0};
                DELETE FROM tramites.procedure_instances               WHERE tenant_id = {0};
                DELETE FROM migration.migration_map                    WHERE tenant_id = {0};
                DELETE FROM identity.tenants                           WHERE id = {0};
                """,
                [TenantId],
                ct);
        }
    }
}
