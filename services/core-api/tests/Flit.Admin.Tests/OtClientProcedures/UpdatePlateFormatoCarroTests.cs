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
/// Caso C 2026-11-09 (tanda 2, decisión del usuario) — la corrección de placa del OT
/// (<c>UpdatePlateAsync</c>, ventana de 1 h) aplica la misma regla ABC123 para carros que la asignación,
/// y la placa corregida queda con <c>Source = "ot"</c>.
/// <para>Uso de ejemplo: <c>repo.UpdatePlateAsync(ot, id, "ABC12D", ...)</c> sobre un trámite con
/// <c>vehicle_class = "AUTOMOVIL"</c> ⇒ <c>Failure = InvalidPlateFormatForClass</c> y la placa previa intacta.</para>
/// </summary>
public sealed class UpdatePlateFormatoCarroTests
{
    private const string MensajeCarro = "La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.";
    private const string PlacaPrevia = "ABC123";

    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa2109");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb2109");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeee2109");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111112109");
    private static readonly Guid Approver = Guid.Parse("44444444-4444-4444-4444-444444442109");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // T1 — carro corrigiendo a ABC12D: falla con el literal, placa previa intacta, sin efectos.
    [Theory]
    [InlineData("AUTOMOVIL")]
    [InlineData("Camioneta")]
    [InlineData("AUTOMÓVIL")]
    public async Task T1_Carro_CorrigeAOtroFormato_FallaSinEfectos(string clase)
    {
        var (db, id) = await SeedAsync(clase);

        var result = await UpdateAsync(db, id, "ABC12D");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.InvalidPlateFormatForClass);
        result.Detail.Should().Be(MensajeCarro);
        await using var verify = NewContext(db);
        var fv = await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct);
        fv.ValueText.Should().Be(PlacaPrevia);
        fv.Source.Should().Be("ot");
        var instance = await verify.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
        instance.Plate.Should().Be(PlacaPrevia);
        instance.PlateUpdatedAt.Should().BeNull("la única corrección permitida no se consume");
        instance.Status.Should().Be(TramiteEstado.Asignado);
        (await verify.ProcedureInstanceEvents.AnyAsync(e => e.ProcedureInstanceId == id, Ct)).Should().BeFalse();
        (await verify.PlateRangeDetails.AnyAsync(Ct)).Should().BeFalse();
        (await verify.PlateRanges.AnyAsync(Ct)).Should().BeFalse();
    }

    // T2 — carro corrigiendo a XYZ987: OK y Source "ot" (field existente → update).
    [Fact]
    public async Task T2_Carro_CorrigeAXYZ987_OkYSourceOt()
    {
        var (db, id) = await SeedAsync("AUTOMOVIL", sourcePrevio: "user");

        var result = await UpdateAsync(db, id, "xyz987");

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        var fv = await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct);
        fv.ValueText.Should().Be("XYZ987");
        fv.Source.Should().Be("ot");
    }

    // T2 (insert) — sin field plate previo: el que se inserta también queda con Source "ot".
    [Fact]
    public async Task T2_SinFieldPrevio_InsertaConSourceOt()
    {
        var (db, id) = await SeedAsync("AUTOMOVIL", conFieldPlate: false);

        var result = await UpdateAsync(db, id, "XYZ987");

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        var fv = await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct);
        fv.ValueText.Should().Be("XYZ987");
        fv.Source.Should().Be("ot");
    }

    // T3 / T4 — moto con ABC12D y clase vacía / sin clase / SIN CLASE: formatos actuales.
    [Theory]
    [InlineData("MOTOCICLETA", "ABC12D")]
    [InlineData("", "R12345")]
    [InlineData(null, "R12345")]
    [InlineData("SIN CLASE", "R12345")]
    [InlineData("CAMIÓN GRÚA", "ABC12D")]
    public async Task T3_T4_NoCarro_CorrigeConFormatosActuales(string? clase, string placa)
    {
        var (db, id) = await SeedAsync(clase);

        var result = await UpdateAsync(db, id, placa);

        result.Succeeded.Should().BeTrue(result.Failure + " " + result.Detail);
        await using var verify = NewContext(db);
        (await verify.ProcedureInstanceFieldValues.SingleAsync(f => f.ProcedureInstanceId == id && f.FieldKey == "plate", Ct))
            .ValueText.Should().Be(placa);
    }

    private static async Task<PlateAssignmentOutcome> UpdateAsync(string db, Guid id, string plate)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        return await repo.UpdatePlateAsync(OtTenant, id, plate, Approver, "ot_console", Ct);
    }

    private static async Task<(string Db, Guid Id)> SeedAsync(
        string? vehicleClass, bool conFieldPlate = true, string sourcePrevio = "ot")
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await using var ctx = NewContext(db);
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
            Email = "actor-2109@test.local",
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
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = "REF-2109",
            Status = TramiteEstado.Asignado,
            Plate = PlacaPrevia,
            PlateAssignedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Approver,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        if (conFieldPlate)
        {
            ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = id,
                TenantId = ClientTenant,
                FieldKey = "plate",
                ValueText = PlacaPrevia,
                Source = sourcePrevio,
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

        await ctx.SaveChangesAsync(Ct);
        return (db, id);
    }

    private static string NewDbName() => $"flit-2109-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
