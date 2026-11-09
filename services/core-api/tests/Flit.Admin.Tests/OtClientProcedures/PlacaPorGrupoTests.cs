using System.Text.Json;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.OtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU8 [BACKEND] (Caso C 2026-11-09, D1) — la placa que asigna o corrige el OT se valida por el grupo de
/// la clase del vehículo (CARRO / MOTO / REMOLQUE / MAQUINARIA). Remolque y maquinaria se resuelven con
/// el catálogo <c>tramites.vehicle_classification_fur</c> vía <see cref="IFurTemplateResolver"/>; el
/// AUTOMOTOR por defecto del catálogo nunca fabrica un grupo. La bandeja y el detalle exponen
/// <c>grupoPlaca</c>.
/// <para>Uso de ejemplo: <c>repo.AssignPlateAsync(ot, id, "ABC123", ...)</c> sobre un trámite con
/// <c>vehicle_class = "MOTOCICLETA"</c> ⇒ <c>Failure = InvalidPlateFormatForClass</c> con el literal de moto.</para>
/// </summary>
public sealed class PlacaPorGrupoTests
{
    private const string MensajeCarro = "La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.";
    private const string MensajeMoto = "La placa de esta moto debe tener 3 letras y 2 números, con o sin una letra al final, por ejemplo ABC12D o ABC12.";
    private const string MensajeRemolque = "La placa de este remolque debe empezar por R o S seguida de 5 o 6 números, por ejemplo R12345 o S54321.";
    private const string MensajeMaquinaria = "La placa de esta maquinaria debe tener 2 letras y 6 números, por ejemplo MC029554.";
    private const string MensajeMotocarro = "La placa de este motocarro debe tener 3 números y 3 letras, por ejemplo 123ABC.";
    private const string PlacaPrevia = "ZZZ999";

    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa3109");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb3109");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeee3109");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111113109");
    private static readonly Guid Approver = Guid.Parse("44444444-4444-4444-4444-444444443109");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- resolución del grupo (Infrastructure)

    [Theory]
    [InlineData("Camioneta", PlateClassGroup.Carro)]
    [InlineData("Motocicleta", PlateClassGroup.Moto)]
    [InlineData("cuatrimoto", PlateClassGroup.Moto)]
    [InlineData("Remolque", PlateClassGroup.Remolque)]
    [InlineData("SEMIRREMOLQUE", PlateClassGroup.Remolque)]
    [InlineData("SEMIREMOLQUE", PlateClassGroup.Remolque)]
    [InlineData("CAMIÓN GRÚA", PlateClassGroup.Maquinaria)]
    [InlineData("camion grua", PlateClassGroup.Maquinaria)]
    [InlineData("MOTONIVELADORA", PlateClassGroup.Maquinaria)]
    [InlineData("Motocarro", PlateClassGroup.Motocarro)]
    [InlineData("MOTOTRICICLO", PlateClassGroup.Motocarro)]
    [InlineData("tricimoto", PlateClassGroup.Motocarro)]
    [InlineData("MAQ. CONSTRUCCION O MINERA", PlateClassGroup.Remolque)]
    public async Task Grupo_SeResuelveConListasExactasYCatalogo(string clase, PlateClassGroup esperado)
    {
        var grupo = await PlateClassGroupResolution.ResolveAsync(clase, new FakeFurCatalog(), Ct);
        grupo.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SIN CLASE")]
    [InlineData("NAVE ESPACIAL")]
    public async Task Grupo_DesconocidaVaciaOSinClase_NoEsGrupo_PeseAlAutomotorPorDefecto(string? clase)
    {
        var grupo = await PlateClassGroupResolution.ResolveAsync(clase, new FakeFurCatalog(), Ct);
        grupo.Should().BeNull();
    }

    [Fact]
    public async Task Grupo_SinResolver_SoloListasExactas()
    {
        (await PlateClassGroupResolution.ResolveAsync("CAMIÓN GRÚA", null, Ct)).Should().BeNull();
        (await PlateClassGroupResolution.ResolveAsync("MOTOCICLETA", null, Ct)).Should().Be(PlateClassGroup.Moto);
    }

    // ---------------------------------------------------------------- asignación (AssignPlateAsync)

    [Theory]
    [InlineData("MOTOCICLETA", "abc12d", "ABC12D")]
    [InlineData("MOTOCICLETA", "ABC12", "ABC12")]
    [InlineData("REMOLQUE", "r12345", "R12345")]
    [InlineData("SEMIRREMOLQUE", "S654321", "S654321")]
    [InlineData("CAMIÓN GRÚA", "mc029554", "MC029554")]
    [InlineData("AUTOMOVIL", "ABC123", "ABC123")]
    [InlineData("MOTOCARRO", "123abc", "123ABC")]
    [InlineData("TRICIMOTO", "523ABC", "523ABC")]
    [InlineData("NAVE ESPACIAL", "123ABC", "123ABC")]
    [InlineData("NAVE ESPACIAL", "R12345", "R12345")]
    [InlineData("SIN CLASE", "ABC12D", "ABC12D")]
    [InlineData(null, "MC029554", "MC029554")]
    public async Task Assign_PlacaDelGrupo_SeAsigna(string? clase, string placa, string esperada)
    {
        var (db, id) = await SeedAsync(clase, TramiteEstado.Preasignacion);

        var result = await AssignAsync(db, id, placa);

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == id, Ct)).Status.Should().Be(TramiteEstado.Asignado);
        (await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct))
            .ValueText.Should().Be(esperada);
    }

    [Theory]
    [InlineData("MOTOCICLETA", "ABC123", MensajeMoto)]
    [InlineData("Ciclomotor", "R12345", MensajeMoto)]
    [InlineData("REMOLQUE", "ABC123", MensajeRemolque)]
    [InlineData("SEMIREMOLQUE", "MC029554", MensajeRemolque)]
    [InlineData("CAMIÓN GRÚA", "ABC123", MensajeMaquinaria)]
    [InlineData("MOTONIVELADORA", "R12345", MensajeMaquinaria)]
    [InlineData("CAMIONETA", "MC029554", MensajeCarro)]
    [InlineData("MOTOTRICICLO", "ABC12D", MensajeMotocarro)]
    [InlineData("MOTOCARRO", "ABC123", MensajeMotocarro)]
    [InlineData("MOTOCICLETA", "123ABC", MensajeMoto)]
    public async Task Assign_PlacaDeOtroGrupo_FallaSinReservaNiTransicion(string clase, string placa, string literal)
    {
        var (db, id) = await SeedAsync(clase, TramiteEstado.Preasignacion);

        var result = await AssignAsync(db, id, placa);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.InvalidPlateFormatForClass);
        result.Detail.Should().Be(literal);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == id, Ct)).Status.Should().Be(TramiteEstado.Preasignacion);
        (await verify.PlateRangeDetails.AnyAsync(Ct)).Should().BeFalse("el fallo va antes de reservar en el inventario");
        (await verify.PlateRanges.AnyAsync(Ct)).Should().BeFalse();
        (await verify.ProcedureInstanceFieldValues.AnyAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct))
            .Should().BeFalse();
        (await verify.ProcedureInstanceStatusHistories.AnyAsync(h => h.ProcedureInstanceId == id, Ct)).Should().BeFalse();
    }

    // ---------------------------------------------------------------- corrección (UpdatePlateAsync)

    [Theory]
    [InlineData("MOTOCICLETA", "ABC12D")]
    [InlineData("REMOLQUE", "S54321")]
    [InlineData("CAMIÓN GRÚA", "MC029554")]
    [InlineData("MOTOCARRO", "123ABC")]
    public async Task Update_PlacaDelGrupo_SeCorrige(string clase, string placa)
    {
        var (db, id) = await SeedAsync(clase, TramiteEstado.Asignado);

        var result = await UpdateAsync(db, id, placa);

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct))
            .ValueText.Should().Be(placa);
    }

    [Theory]
    [InlineData("MOTOCICLETA", "ABC123", MensajeMoto)]
    [InlineData("REMOLQUE", "X12345", MensajeRemolque)]
    [InlineData("REMOLQUE", "R1234", MensajeRemolque)]
    [InlineData("CAMIÓN GRÚA", "MC02955", MensajeMaquinaria)]
    [InlineData("TRICIMOTO", "ABC12", MensajeMotocarro)]
    public async Task Update_PlacaDeOtroGrupo_FallaYConservaLaPrevia(string clase, string placa, string literal)
    {
        var (db, id) = await SeedAsync(clase, TramiteEstado.Asignado);

        var result = await UpdateAsync(db, id, placa);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.InvalidPlateFormatForClass);
        result.Detail.Should().Be(literal);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct))
            .ValueText.Should().Be(PlacaPrevia);
        var instance = await verify.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
        instance.Plate.Should().Be(PlacaPrevia);
        instance.PlateUpdatedAt.Should().BeNull("la única corrección no se consume");
        (await verify.ProcedureInstanceEvents.AnyAsync(e => e.ProcedureInstanceId == id, Ct)).Should().BeFalse();
    }

    // ---------------------------------------------------------------- contrato grupoPlaca (lista y detalle)

    [Fact]
    public async Task Lista_ProyectaGrupoPlaca_YConsultaElCatalogoUnaVezPorClase()
    {
        var db = NewDbName();
        var carro = Guid.NewGuid();
        var moto = Guid.NewGuid();
        var remolque = Guid.NewGuid();
        var grua1 = Guid.NewGuid();
        var grua2 = Guid.NewGuid();
        var grua3 = Guid.NewGuid();
        var desconocida = Guid.NewGuid();
        var sinClase = Guid.NewGuid();
        var motocarro = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedBase(seed);
            SeedProcedure(seed, carro, "REF-C", "Camioneta", TramiteEstado.Preasignacion);
            SeedProcedure(seed, moto, "REF-M", "MOTOCICLETA", TramiteEstado.Preasignacion);
            SeedProcedure(seed, remolque, "REF-R", "SEMIRREMOLQUE", TramiteEstado.Preasignacion);
            SeedProcedure(seed, grua1, "REF-G1", "CAMIÓN GRÚA", TramiteEstado.Preasignacion);
            SeedProcedure(seed, grua2, "REF-G2", "CAMIÓN GRÚA", TramiteEstado.Preasignacion);
            SeedProcedure(seed, grua3, "REF-G3", "camion grua", TramiteEstado.Preasignacion);
            SeedProcedure(seed, desconocida, "REF-X", "NAVE ESPACIAL", TramiteEstado.Preasignacion);
            SeedProcedure(seed, sinClase, "REF-N", null, TramiteEstado.Preasignacion);
            SeedProcedure(seed, motocarro, "REF-MC", "MOTOTRICICLO", TramiteEstado.Preasignacion);
        }

        var catalog = new FakeFurCatalog();
        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(
            ctx, new NullTramiteTransitionPublisher(), furTemplateResolver: catalog));
        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Status = "preasignacion,asignado,entregado",
        }, Ct);

        result.Data.Single(p => p.Id == carro).GrupoPlaca.Should().Be("CARRO");
        result.Data.Single(p => p.Id == moto).GrupoPlaca.Should().Be("MOTO");
        result.Data.Single(p => p.Id == remolque).GrupoPlaca.Should().Be("REMOLQUE");
        result.Data.Single(p => p.Id == grua1).GrupoPlaca.Should().Be("MAQUINARIA");
        result.Data.Single(p => p.Id == grua2).GrupoPlaca.Should().Be("MAQUINARIA");
        result.Data.Single(p => p.Id == grua3).GrupoPlaca.Should().Be("MAQUINARIA");
        result.Data.Single(p => p.Id == desconocida).GrupoPlaca.Should().BeNull();
        result.Data.Single(p => p.Id == sinClase).GrupoPlaca.Should().BeNull();
        result.Data.Single(p => p.Id == motocarro).GrupoPlaca.Should().Be("MOTOCARRO");
        JsonSerializer.Serialize(result.Data.Single(p => p.Id == grua1), JsonSerializerOptions.Web)
            .Should().Contain("\"grupoPlaca\":\"MAQUINARIA\"");
        catalog.Calls.Should().BeLessThanOrEqualTo(2,
            "el catálogo se consulta una vez por clase normalizada distinta fuera de las listas exactas, no por fila");
    }

    [Fact]
    public async Task Detalle_ProyectaGrupoPlaca()
    {
        var (db, id) = await SeedAsync("CAMIÓN GRÚA", TramiteEstado.Preasignacion);

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), furTemplateResolver: new FakeFurCatalog());
        var detalle = await repo.GetByIdAsync(OtTenant, id, Ct);

        detalle.Should().NotBeNull();
        detalle!.Clase.Should().Be("CAMIÓN GRÚA");
        detalle.GrupoPlaca.Should().Be(PlateClassGroup.Maquinaria);
        var response = await new GetOtClientProcedureHandler(repo)
            .HandleAsync(new GetOtClientProcedureQuery { OtTenantId = OtTenant, ProcedureInstanceId = id }, Ct);
        response.Procedure!.GrupoPlaca.Should().Be("MAQUINARIA");
        JsonSerializer.Serialize(response.Procedure, JsonSerializerOptions.Web).Should().Contain("\"grupoPlaca\":\"MAQUINARIA\"");
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<PlateAssignmentOutcome> AssignAsync(string db, Guid id, string plate)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(
            ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx), furTemplateResolver: new FakeFurCatalog());
        return await repo.AssignPlateAsync(OtTenant, id, plate, Approver, OtTransitionSource.OtAdmin, cancellationToken: Ct);
    }

    private static async Task<PlateAssignmentOutcome> UpdateAsync(string db, Guid id, string plate)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(
            ctx, new NullTramiteTransitionPublisher(), furTemplateResolver: new FakeFurCatalog());
        return await repo.UpdatePlateAsync(OtTenant, id, plate, Approver, "ot_console", Ct);
    }

    private static async Task<(string Db, Guid Id)> SeedAsync(string? vehicleClass, string status)
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await using var seed = NewContext(db);
        SeedBase(seed);
        SeedProcedure(seed, id, "REF-3109", vehicleClass, status);
        return (db, id);
    }

    private static void SeedBase(FlitDbContext ctx)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Users.Add(new User
        {
            Id = Approver,
            Email = "actor-3109@test.local",
            DisplayName = "Actor Test",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = ProcedureType,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
            GateProfile = """{"entryMode":"VIN","requiresBuyer":true,"requiresPlateRequest":true}""",
            IsActive = true,
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedProcedure(FlitDbContext ctx, Guid id, string reference, string? vehicleClass, string status)
    {
        var asignado = status == TramiteEstado.Asignado;
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = reference,
            Status = status,
            Plate = asignado ? PlacaPrevia : null,
            PlateAssignedAt = asignado ? DateTimeOffset.UtcNow.AddMinutes(-30) : null,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Approver,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        if (asignado)
        {
            ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = id,
                TenantId = ClientTenant,
                FieldKey = "plate",
                ValueText = PlacaPrevia,
                Source = "ot",
            });
        }

        if (vehicleClass is not null)
        {
            ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = id,
                TenantId = ClientTenant,
                FieldKey = VehicleFieldKeys.Class,
                ValueText = vehicleClass,
            });
        }

        ctx.SaveChanges();
    }

    private static string NewDbName() => $"flit-3109-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    /// <summary>
    /// Subconjunto del seed <c>39-HU10919-vehicle-classification-fur.sql</c> con la misma resolución pura
    /// (<see cref="FurTemplateResolution.ResolveMatch"/>): sin match ⇒ AUTOMOTOR por defecto.
    /// </summary>
    private sealed class FakeFurCatalog : IFurTemplateResolver
    {
        private static readonly IReadOnlyDictionary<string, FurClassificationMatch> Map =
            new (string Clase, FurTemplateFormat Formato)[]
            {
                ("AUTOMOVIL", FurTemplateFormat.Automotor),
                ("CAMIONETA", FurTemplateFormat.Automotor),
                ("MOTOCICLETA", FurTemplateFormat.Automotor),
                ("SIN CLASE", FurTemplateFormat.Automotor),
                ("CAMIÓN GRÚA", FurTemplateFormat.Maquinaria),
                ("MOTONIVELADORA", FurTemplateFormat.Maquinaria),
                ("MAQ. CONSTRUCCION O MINERA", FurTemplateFormat.Remolques),
                ("REMOLQUE", FurTemplateFormat.Remolques),
                ("REMOLQUE Y SEMIRREMOLQUE", FurTemplateFormat.Remolques),
                ("SEMIREMOLQUE", FurTemplateFormat.Remolques),
            }.ToDictionary(
                r => FurClassificationNormalizer.Normalize(r.Clase),
                r => new FurClassificationMatch(r.Formato, null),
                StringComparer.Ordinal);

        public int Calls { get; private set; }

        public async Task<FurTemplateFormat> ResolveAsync(string? vehicleClass, CancellationToken ct = default) =>
            (await ResolveMatchAsync(vehicleClass, ct)).Format;

        public Task<FurClassificationMatch> ResolveMatchAsync(string? vehicleClass, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(FurTemplateResolution.ResolveMatch(vehicleClass, Map));
        }

        public Task<IReadOnlyList<FurClassificationCatalogItem>> ListCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FurClassificationCatalogItem>>([]);
    }
}
