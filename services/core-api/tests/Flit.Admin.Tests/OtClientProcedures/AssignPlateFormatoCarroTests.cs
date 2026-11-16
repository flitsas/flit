using System.Text.Json;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// Caso C 2026-11-09 (registro diferido) — HU 1 [BACKEND]: un carro (vehicle_class normalizado
/// exactamente igual a AUTOMOVIL, CAMIONETA, CAMPERO, BUS, BUSETA, MICROBUS, CAMION, TRACTOCAMION o
/// VOLQUETA) solo admite placa <c>ABC123</c> al asignarla el OT; el resto de clases conserva los
/// formatos actuales. La bandeja del OT trae la clase. HU 3 (parte backend): el field_value
/// <c>plate</c> que escribe el OT queda con <c>Source = "ot"</c>.
/// <para>Uso de ejemplo: <c>repo.AssignPlateAsync(ot, id, "ABC12D", ...)</c> sobre un trámite con
/// <c>vehicle_class = "Camioneta"</c> ⇒ <c>Failure = InvalidPlateFormatForClass</c>, sin reserva ni transición.</para>
/// </summary>
public sealed class AssignPlateFormatoCarroTests
{
    private const string MensajeCarro = "La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.";

    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa1109");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb1109");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeee1109");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111111109");
    private static readonly Guid Approver = Guid.Parse("44444444-4444-4444-4444-444444441109");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // AC1 — carro con placa válida: se asigna normalizada.
    [Fact]
    public async Task AC1_Carro_PlacaValidaEnMinusculas_SeAsignaNormalizada()
    {
        var (db, id) = await SeedAsync("AUTOMOVIL");

        var result = await AssignAsync(db, id, "abc123");

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == id, Ct)).Status.Should().Be(TramiteEstado.Asignado);
        (await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct))
            .ValueText.Should().Be("ABC123");
    }

    // AC2 — carro con placa de otro formato: 422 (falla nombrada), sin reserva y sin transición.
    [Theory]
    [InlineData("Camioneta")]
    [InlineData("AUTOMÓVIL")]
    [InlineData("  tractocamión ")]
    [InlineData("VOLQUETA")]
    public async Task AC2_Carro_PlacaDeOtroFormato_FallaSinReservaNiTransicion(string clase)
    {
        var (db, id) = await SeedAsync(clase);

        var result = await AssignAsync(db, id, "ABC12D");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.InvalidPlateFormatForClass);
        result.Detail.Should().Be(MensajeCarro);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == id, Ct)).Status.Should().Be(TramiteEstado.Preasignacion);
        (await verify.PlateRangeDetails.AnyAsync(Ct)).Should().BeFalse("el fallo va antes de ReserveOutOfRangePlateAsync");
        (await verify.PlateRanges.AnyAsync(Ct)).Should().BeFalse();
        (await verify.ProcedureInstanceFieldValues.AnyAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct))
            .Should().BeFalse();
        (await verify.ProcedureInstanceStatusHistories.AnyAsync(h => h.ProcedureInstanceId == id, Ct)).Should().BeFalse();
    }

    // AC2 (variante) — clase con tilde o minúsculas se trata como carro: ABC123 sí entra.
    [Theory]
    [InlineData("Camioneta")]
    [InlineData("AUTOMÓVIL")]
    public async Task AC2_ClaseConTildeOMinusculas_EsCarroYAceptaABC123(string clase)
    {
        var (db, id) = await SeedAsync(clase);

        var result = await AssignAsync(db, id, "ABC123");

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
    }

    // AC3 / AC4 — moto, maquinaria (CAMION GRUA), vacía, SIN CLASE o sin field conservan los formatos actuales.
    [Theory]
    [InlineData("MOTOCICLETA", "ABC12D")]
    [InlineData("CAMION GRUA", "ABC12D")]
    [InlineData("CAMIÓN GRÚA", "R12345")]
    [InlineData("", "R12345")]
    [InlineData("SIN CLASE", "R12345")]
    [InlineData(null, "R12345")]
    public async Task AC3_AC4_NoCarro_ConservaFormatosActuales(string? clase, string placa)
    {
        var (db, id) = await SeedAsync(clase);

        var result = await AssignAsync(db, id, placa);

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == id, Ct)).Status.Should().Be(TramiteEstado.Asignado);
    }

    // HU 3 AC1 — la placa que escribe el OT queda marcada con Source "ot" (insert).
    [Fact]
    public async Task HU3_AC1_PlacaAsignadaPorElOt_QuedaConSourceOt()
    {
        var (db, id) = await SeedAsync("AUTOMOVIL");

        var result = await AssignAsync(db, id, "ABC125");

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        var fv = await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct);
        fv.Source.Should().Be("ot");
    }

    // HU 3 AC1 (edge) — si ya existía un field plate de otra fuente, la asignación del OT lo reescribe con Source "ot".
    [Fact]
    public async Task HU3_AC1_PlacaExistenteDeOtraFuente_QuedaConSourceOt()
    {
        var (db, id) = await SeedAsync("AUTOMOVIL");
        await using (var seed = NewContext(db))
        {
            seed.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = id,
                TenantId = ClientTenant,
                FieldKey = "plate",
                ValueText = "",
                Source = "user",
            });
            await seed.SaveChangesAsync(Ct);
        }

        var result = await AssignAsync(db, id, "ABC125");

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        var fv = await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct);
        fv.ValueText.Should().Be("ABC125");
        fv.Source.Should().Be("ot");
    }

    // HU 1 AC5 — la LISTA del OT trae la clase cruda del trámite, y el DTO la serializa como "clase".
    [Fact]
    public async Task AC5_Lista_TraeLaClaseCruda()
    {
        var db = NewDbName();
        var conClase = Guid.NewGuid();
        var sinClase = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedBase(seed);
            SeedProcedure(seed, conClase, "REF-CLASE", "Camioneta");
            SeedProcedure(seed, sinClase, "REF-SINCLASE", null);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Status = "preasignacion,asignado,entregado",
        }, Ct);

        var fila = result.Data.Single(p => p.Id == conClase);
        fila.Clase.Should().Be("Camioneta", "la bandeja muestra el valor crudo, sin normalizar");
        result.Data.Single(p => p.Id == sinClase).Clase.Should().BeNull();
        JsonSerializer.Serialize(fila, JsonSerializerOptions.Web).Should().Contain("\"clase\":\"Camioneta\"");
    }

    private static async Task<PlateAssignmentOutcome> AssignAsync(string db, Guid id, string plate)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        return await repo.AssignPlateAsync(OtTenant, id, plate, Approver, OtTransitionSource.OtAdmin, cancellationToken: Ct);
    }

    private static async Task<(string Db, Guid Id)> SeedAsync(string? vehicleClass)
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await using var seed = NewContext(db);
        SeedBase(seed);
        SeedProcedure(seed, id, "REF-1109", vehicleClass);
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
            Email = "actor-1109@test.local",
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

    private static void SeedProcedure(FlitDbContext ctx, Guid id, string reference, string? vehicleClass)
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = reference,
            Status = TramiteEstado.Preasignacion,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Approver,
            CreatedAt = DateTimeOffset.UtcNow,
        });
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

    private static string NewDbName() => $"flit-1109-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
