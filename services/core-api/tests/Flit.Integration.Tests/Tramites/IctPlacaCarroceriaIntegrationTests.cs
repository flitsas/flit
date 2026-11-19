using Flit.Admin.Domain.PlatePreassign;
using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using PatchFieldValuesRequest = Flit.Tramites.Application.UseCases.ProcedureInstances.PatchFieldValuesRequest;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Hallazgos QA de ICT, contra Postgres REAL: (A) la placa de la matrícula según <c>plate_assignment_type</c>
/// (2 dígito de preferencia, 3 placa entregada con origen «ict» que el preflight no pisa, 4 placa de un rango del
/// OT que se valida y se reserva) y (B) la carrocería de la transformación 17, validada contra el catálogo de la
/// clase RUNT después del preflight.
/// <para>Uso de ejemplo: el servicio se activa del contenedor real con un <see cref="RunPreflightHandler"/> cuya
/// cadena nunca se llama (la consulta RUNT viaja precalculada).</para>
/// </summary>
public sealed class IctPlacaCarroceriaIntegrationTests(PostgresDatabaseFixture fixture, HostsPorClase hosts)
    : PostgresTestBase(fixture), IClassFixture<HostsPorClase>
{
    private static readonly Guid UserId = new("b14600a1-0000-4000-8000-0000000000aa");
    private static readonly Guid OfficeId = new("b14600a1-0000-4000-8000-0000000000b1");
    private static readonly Guid OtTenantId = new("b14600a1-0000-4000-8000-0000000000c1");
    private const string Vin = "9BWZZZ377VT004251";
    private const string PlacaTraspaso = "BCD146";
    private const string PlacaRango = "RNG101";
    private const string ClaseIt = "CLASE IT 14600";

    // ── A. Placa ─────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Tipo3_LaPlacaSeGuardaConOrigenIct_YElPreflightNoLaPisa()
    {
        await SeedTenantAsync();
        var request = Matricula("ict-placa-3", tipo: "3", plate: "abc123",
            new HydratedField("plate", "ZZZ999", null), new HydratedField("vehicle_brand", "MARCA IT", null));

        var reply = await InvokeCreateAsync(request);

        reply.ProcedureInstanceId.Should().NotBeNullOrEmpty("error: {0}", reply.ErrorCode);
        var id = Guid.Parse(reply.ProcedureInstanceId);
        (await FieldAsync(id, "vehicle_brand"))!.Value.Should().Be("MARCA IT", "el preflight sí corrió");
        var plate = await FieldAsync(id, "plate");
        plate!.Value.Should().Be("ABC123", "la placa de ICT vale más que la del RUNT");
        plate.Source.Should().Be("ict");
        (await FieldAsync(id, "plate_assignment_type"))!.Value.Should().Be("3");
    }

    [PostgresFact]
    public async Task Tipo2_ElDigitoNoSeGuardaComoPlaca_SinoComoPreferencia()
    {
        await SeedTenantAsync();
        var request = Matricula("ict-placa-2", tipo: "2", plate: "5");

        var reply = await InvokeCreateAsync(request);

        reply.ProcedureInstanceId.Should().NotBeNullOrEmpty("error: {0}", reply.ErrorCode);
        var id = Guid.Parse(reply.ProcedureInstanceId);
        (await FieldAsync(id, "plate")).Should().BeNull("un dígito de preferencia no es una placa");
        (await FieldAsync(id, "plate_preferred_last_digit"))!.Value.Should().Be("5");
        (await FieldAsync(id, "plate_assignment_type"))!.Value.Should().Be("2");
    }

    [PostgresFact]
    public async Task Tipo4_ConPlacaDelRangoDelOt_CreaElBorrador_YLaReservaPreasignada()
    {
        await SeedTenantAsync();
        await SeedOficinaYRangoAsync();
        var request = Matricula("ict-placa-4-ok", tipo: "4", plate: "rng101", oficina: OfficeId);

        var reply = await InvokeCreateAsync(request);

        reply.ProcedureInstanceId.Should().NotBeNullOrEmpty("error: {0}", reply.ErrorCode);
        var id = Guid.Parse(reply.ProcedureInstanceId);
        var plate = await FieldAsync(id, "plate");
        plate!.Value.Should().Be(PlacaRango);
        plate.Source.Should().Be("ict");
        var detalle = await DetalleAsync(PlacaRango);
        detalle.State.Should().Be(PlateState.Preasignada, "mismo ciclo de vida que la asignación del OT");
        detalle.ProcedureInstanceId.Should().Be(id);
    }

    [PostgresFact]
    public async Task Tipo4_ElReintentoDelMismoPreTramite_EsIdempotente_YNoFallaPorLaPlacaYaReservada()
    {
        await SeedTenantAsync();
        await SeedOficinaYRangoAsync();
        var request = Matricula("ict-placa-4-retry", tipo: "4", plate: PlacaRango, oficina: OfficeId);

        var primero = await InvokeCreateAsync(request);
        var segundo = await InvokeCreateAsync(request);

        segundo.ProcedureInstanceId.Should().Be(primero.ProcedureInstanceId);
        (segundo.ErrorCode ?? string.Empty).Should().NotContain("placa_");
        (await DetalleAsync(PlacaRango)).State.Should().Be(PlateState.Preasignada);
    }

    [PostgresFact]
    public async Task Tipo4_ConPlacaFueraDelRango_EsNovedadFatal_SinBorrador()
    {
        await SeedTenantAsync();
        await SeedOficinaYRangoAsync();
        var request = Matricula("ict-placa-4-fuera", tipo: "4", plate: "ZZZ999", oficina: OfficeId);

        var reply = await InvokeCreateAsync(request);

        reply.ErrorCode.Should().Be("placa_fuera_de_rango");
        reply.ProcedureInstanceId.Should().BeNullOrEmpty();
        (await CountInstancesAsync("ict-placa-4-fuera")).Should().Be(0);
    }

    [PostgresFact]
    public async Task Tipo4_ConPlacaNoDisponible_EsNovedadFatal()
    {
        await SeedTenantAsync();
        await SeedOficinaYRangoAsync(estado: PlateState.Bloqueada);
        var request = Matricula("ict-placa-4-bloqueada", tipo: "4", plate: PlacaRango, oficina: OfficeId);

        var reply = await InvokeCreateAsync(request);

        reply.ErrorCode.Should().Be("placa_fuera_de_rango");
        (await CountInstancesAsync("ict-placa-4-bloqueada")).Should().Be(0);
    }

    [PostgresFact]
    public async Task Tipo4_SinOtConocido_EsNovedadFatal_SinBorrador()
    {
        await SeedTenantAsync();
        var request = Matricula("ict-placa-4-sin-ot", tipo: "4", plate: PlacaRango);

        var reply = await InvokeCreateAsync(request);

        reply.ErrorCode.Should().Be("placa_rango_sin_ot");
        reply.ProcedureInstanceId.Should().BeNullOrEmpty();
        (await CountInstancesAsync("ict-placa-4-sin-ot")).Should().Be(0);
    }

    [PostgresFact]
    public async Task Tipo4_SiLaReservaFallaPorCarrera_ElBorradorSeDescarta_YElReintentoPuedeCrearOtro()
    {
        await SeedTenantAsync();
        await SeedOficinaYRangoAsync();
        var carrera = Substitute.For<IPlateRangeRepository>();
        carrera.IsPlateAvailableAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        carrera.TryReservePlateAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var request = Matricula("ict-placa-4-carrera", tipo: "4", plate: PlacaRango, oficina: OfficeId);

        var reply = await InvokeCreateAsync(request, carrera);

        reply.ErrorCode.Should().Be("placa_fuera_de_rango");
        reply.ProcedureInstanceId.Should().BeNullOrEmpty();
        (await CountInstancesAsync("ict-placa-4-carrera")).Should().Be(0, "no queda un borrador huérfano visible");
        (await CountInstancesAsync("ict-placa-4-carrera", incluirBorrados: true)).Should().Be(1, "borrado lógico");

        var reintento = await InvokeCreateAsync(request);
        reintento.ProcedureInstanceId.Should().NotBeNullOrEmpty("error: {0}", reintento.ErrorCode);
    }

    [PostgresFact]
    public async Task Tipo4_ElOrganismoQuedaFijo_CambiarloSeRechaza_YElMismoSePermite()
    {
        await SeedTenantAsync();
        await SeedOficinaYRangoAsync();
        var reply = await InvokeCreateAsync(Matricula("ict-placa-4-ot-fijo", tipo: "4", plate: PlacaRango, oficina: OfficeId));
        var id = Guid.Parse(reply.ProcedureInstanceId);

        var factory = hosts.Obtener(Fixture); // un host por clase (HostsPorClase)
        await using var scope = factory.Services.CreateAsyncScope();
        var patch = scope.ServiceProvider.GetRequiredService<PatchFieldValuesHandler>();
        var ct = TestContext.Current.CancellationToken;

        var (_, mismo) = await patch.HandleAsync(id, TenantSeed.LoneId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", OfficeId.ToString(), null)]), ct);
        var (_, otro) = await patch.HandleAsync(id, TenantSeed.LoneId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]), ct);

        mismo.Should().BeNull("re-enviar el mismo organismo es idempotente");
        PatchFieldValuesHandler.TryParseOtFijado(otro, out var placa).Should().BeTrue();
        placa.Should().Be(PlacaRango);
    }

    [PostgresFact]
    public async Task Tipo4_LaPlacaYSuTipoQuedanFijos_ElMismoValorSePermite()
    {
        await SeedTenantAsync();
        await SeedOficinaYRangoAsync();
        var reply = await InvokeCreateAsync(Matricula("ict-placa-4-placa-fija", tipo: "4", plate: PlacaRango, oficina: OfficeId));
        var id = Guid.Parse(reply.ProcedureInstanceId);

        var factory = hosts.Obtener(Fixture); // un host por clase (HostsPorClase)
        await using var scope = factory.Services.CreateAsyncScope();
        var patch = scope.ServiceProvider.GetRequiredService<PatchFieldValuesHandler>();
        var ct = TestContext.Current.CancellationToken;

        var (_, otra) = await patch.HandleAsync(id, TenantSeed.LoneId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "plate", "ZZZ999", null)]), ct);
        var (_, tipo) = await patch.HandleAsync(id, TenantSeed.LoneId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "plate_assignment_type", "3", null)]), ct);
        var (_, misma) = await patch.HandleAsync(id, TenantSeed.LoneId,
            new PatchFieldValuesRequest(
            [
                new FieldValueInput(null, "plate", " " + PlacaRango.ToLowerInvariant() + " ", null),
                new FieldValueInput(null, "plate_assignment_type", "4", null),
            ]), ct);

        PatchFieldValuesHandler.TryParsePlacaFijada(otra, out var placa).Should().BeTrue();
        placa.Should().Be(PlacaRango);
        PatchFieldValuesHandler.TryParsePlacaFijada(tipo, out _).Should().BeTrue();
        misma.Should().BeNull();
        (await FieldAsync(id, "plate"))!.Value.Should().Be(PlacaRango);
    }

    [PostgresFact]
    public async Task Tipo3_ElOrganismoSigueSiendoEditable()
    {
        await SeedTenantAsync();
        var reply = await InvokeCreateAsync(Matricula("ict-placa-3-ot-libre", tipo: "3", plate: "abc123"));
        var id = Guid.Parse(reply.ProcedureInstanceId);

        var factory = hosts.Obtener(Fixture); // un host por clase (HostsPorClase)
        await using var scope = factory.Services.CreateAsyncScope();
        var patch = scope.ServiceProvider.GetRequiredService<PatchFieldValuesHandler>();

        var (_, error) = await patch.HandleAsync(id, TenantSeed.LoneId,
            new PatchFieldValuesRequest([new FieldValueInput(null, "transit_office_id", Guid.NewGuid().ToString(), null)]),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
    }

    // ── B. Carrocería ────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Carroceria_QueCoincideConElCatalogoDeLaClase_SeGuardaConElNombreDelCatalogo()
    {
        await SeedTenantAsync();
        await SeedCarroceriasAsync();
        var request = Traspaso("ict-carroceria-ok", "  volqueta  IT ",
            new HydratedField("vehicle_class", ClaseIt, null), new HydratedField("vehicle_body_type", "FURGON", null));

        var reply = await InvokeCreateAsync(request);

        var id = Guid.Parse(reply.ProcedureInstanceId);
        (reply.ErrorCode ?? string.Empty).Should().NotContain("carroceria_warning");
        (await FieldAsync(id, "vehicle_body_type"))!.Value.Should().Be("VOLQUETA IT");
        (await FieldAsync(id, "cambio_carroceria"))!.Value.Should().Be("true");
    }

    [PostgresFact]
    public async Task Carroceria_QueNoCoincide_VuelveAlRunt_ConservaLaBandera_YAvisa()
    {
        await SeedTenantAsync();
        await SeedCarroceriasAsync();
        var request = Traspaso("ict-carroceria-no", "Cambio carroceria Hatchback",
            new HydratedField("vehicle_class", ClaseIt, null), new HydratedField("vehicle_body_type", "FURGON", null));

        var reply = await InvokeCreateAsync(request);

        var id = Guid.Parse(reply.ProcedureInstanceId);
        reply.ErrorCode.Should().Contain("carroceria_warning:fuera_de_catalogo");
        (await FieldAsync(id, "vehicle_body_type"))!.Value.Should().Be("FURGON", "queda el valor del RUNT: el gestor elige");
        (await FieldAsync(id, "cambio_carroceria"))!.Value.Should().Be("true");
    }

    [PostgresFact]
    public async Task Carroceria_ConRuntSinCarroceria_NoSeActivaElCambio_YAvisa()
    {
        await SeedTenantAsync();
        await SeedCarroceriasAsync();
        // La consulta respondió (hay clase) y no trae carrocería: no hay nada que cambiar ni que comparar.
        var request = Traspaso("ict-carroceria-vacia", "VOLQUETA IT",
            new HydratedField("vehicle_class", ClaseIt, null));

        var reply = await InvokeCreateAsync(request);

        var id = Guid.Parse(reply.ProcedureInstanceId);
        reply.ErrorCode.Should().Contain("carroceria_warning:vehiculo_sin_carroceria")
            .And.NotContain("fuera_de_catalogo", "si no hay carrocería no se compara con el catálogo");
        (await FieldAsync(id, "cambio_carroceria"))!.Value.Should().Be("false");
        (await FieldAsync(id, "vehicle_body_type"))!.Value.Should().BeNull();
    }

    [PostgresFact]
    public async Task Carroceria_ConRuntSinCarroceriaExplicita_NoSeActivaElCambio_YConservaElValorRunt()
    {
        await SeedTenantAsync();
        await SeedCarroceriasAsync();
        var request = Traspaso("ict-carroceria-sin-carroceria", "VOLQUETA IT",
            new HydratedField("vehicle_class", ClaseIt, null),
            new HydratedField("vehicle_body_type", "SIN CARROCERIA", null));

        var reply = await InvokeCreateAsync(request);

        var id = Guid.Parse(reply.ProcedureInstanceId);
        reply.ErrorCode.Should().Contain("carroceria_warning:vehiculo_sin_carroceria");
        (await FieldAsync(id, "cambio_carroceria"))!.Value.Should().Be("false");
        (await FieldAsync(id, "vehicle_body_type"))!.Value.Should().Be("SIN CARROCERIA");
    }

    [PostgresFact]
    public async Task Carroceria_SinRespuestaDelRunt_NoSeDeduceQueNoTieneCarroceria()
    {
        await SeedTenantAsync();
        await SeedCarroceriasAsync();
        // Sin clase ni carrocería hidratadas el preflight no obtuvo datos: sigue la validación de catálogo.
        var request = Traspaso("ict-carroceria-sin-respuesta", "VOLQUETA IT");

        var reply = await InvokeCreateAsync(request);

        (reply.ErrorCode ?? string.Empty).Should().NotContain("vehiculo_sin_carroceria");
        (await FieldAsync(Guid.Parse(reply.ProcedureInstanceId), "cambio_carroceria"))!.Value.Should().Be("true");
    }

    [PostgresFact]
    public async Task Carroceria_SinClaseRunt_UsaSoloLasFilasDeRespaldo()
    {
        await SeedTenantAsync();
        await SeedCarroceriasAsync();

        var respaldo = await InvokeCreateAsync(Traspaso("ict-carroceria-respaldo", "respaldo it"));
        var deClase = await InvokeCreateAsync(Traspaso("ict-carroceria-sin-clase", "VOLQUETA IT"));

        (await FieldAsync(Guid.Parse(respaldo.ProcedureInstanceId), "vehicle_body_type"))!.Value
            .Should().Be("RESPALDO IT");
        (respaldo.ErrorCode ?? string.Empty).Should().NotContain("carroceria_warning");
        deClase.ErrorCode.Should().Contain("carroceria_warning:fuera_de_catalogo",
            "sin clase solo cuentan las carrocerías de respaldo");
    }

    // ── invocación y armado ──────────────────────────────────────────────────

    private async Task<DraftReply> InvokeCreateAsync(
        CreateDraftFromIctRequest request, IPlateRangeRepository? plateRepo = null)
    {
        var factory = hosts.Obtener(Fixture); // un host por clase (HostsPorClase)
        await using var scope = factory.Services.CreateAsyncScope();
        var preflight = ActivatorUtilities.CreateInstance<RunPreflightHandler>(
            scope.ServiceProvider, (IConsultationProviderChainResolver)new CadenaProhibida());
        var service = plateRepo is null
            ? ActivatorUtilities.CreateInstance<IctOrchestrationService>(scope.ServiceProvider, preflight)
            : ActivatorUtilities.CreateInstance<IctOrchestrationService>(scope.ServiceProvider, preflight, plateRepo);
        return await service.CreateDraftFromIct(request, new TestCallContext(TestContext.Current.CancellationToken));
    }

    private static PrecomputedVehicleConsultation Precomputed(string kind, string identificador, HydratedField[] campos)
    {
        var hidratados = new List<HydratedField>(campos) { new("vehicle_brand", "MARCA IT", null) };
        return new PrecomputedVehicleConsultation
        {
            SnapshotJson = PreflightVehicleSnapshotJson.Serialize(new PreflightVehicleSnapshot(
                [new PreflightCheckDto("soat", "SOAT", "ok", "kyverum_runt", null)],
                hidratados,
                ["kyverum_runt"])),
            ConsultedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-1)),
            Provider = "kyverum_runt",
            Kind = kind,
            QueriedPlate = kind == "VehiclePlate" ? identificador : string.Empty,
            QueriedVin = kind == "VehicleVin" ? identificador : string.Empty,
        };
    }

    private static CreateDraftFromIctRequest Matricula(
        string externalRef, string tipo, string plate, params HydratedField[] campos) =>
        Matricula(externalRef, tipo, plate, oficina: null, campos);

    private static CreateDraftFromIctRequest Matricula(
        string externalRef, string tipo, string plate, Guid? oficina, params HydratedField[] campos)
    {
        var request = new CreateDraftFromIctRequest
        {
            TenantId = TenantSeed.LoneId.ToString(),
            ProcedureTypeCode = "MATRICULA_NUEVA",
            Origin = "ict",
            ExternalRef = externalRef,
            PrecomputedVehicle = Precomputed("VehicleVin", Vin, campos),
        };
        request.FieldValues.Add(new FieldValue { FieldKey = "vin", ValueText = Vin });
        request.FieldValues.Add(new FieldValue { FieldKey = "plate", ValueText = plate });
        request.FieldValues.Add(new FieldValue { FieldKey = "plate_assignment_type", ValueText = tipo });
        request.FieldValues.Add(new FieldValue { FieldKey = "vehicle_service", ValueText = "PARTICULAR" });
        if (oficina is { } office)
        {
            request.TransitOfficeId = office.ToString();
            request.TransitOfficeCode = "9914600";
            request.TransitOfficeName = "OT IT 14600";
            request.TransitOfficeCity = "99146";
        }

        return request;
    }

    private static CreateDraftFromIctRequest Traspaso(
        string externalRef, string carroceria, params HydratedField[] campos)
    {
        var request = new CreateDraftFromIctRequest
        {
            TenantId = TenantSeed.LoneId.ToString(),
            ProcedureTypeCode = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            Origin = "ict",
            ExternalRef = externalRef,
            PrecomputedVehicle = Precomputed("VehiclePlate", PlacaTraspaso, campos),
        };
        request.FieldValues.Add(new FieldValue { FieldKey = "plate", ValueText = PlacaTraspaso });
        request.FieldValues.Add(new FieldValue { FieldKey = "cambio_carroceria", ValueText = "true" });
        request.FieldValues.Add(new FieldValue { FieldKey = "vehicle_body_type", ValueText = carroceria });
        return request;
    }

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task SeedTenantAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = UserId,
            Email = "it-14600@flit.test",
            DisplayName = "ICT 14600",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        // Sin política la familia MATRICULAS nace bloqueada (procedure_family_blocked).
        ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = TenantSeed.LoneId,
            AllowInitialRegistration = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>OT activo y operativo + grant a la compañía + un rango de una placa (<see cref="PlacaRango"/>).</summary>
    private async Task SeedOficinaYRangoAsync(string estado = PlateState.Disponible)
    {
        var ahora = DateTimeOffset.UtcNow;
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(OtTenantId, "IT-OT-14600", isGroupParent: false, parentId: null));
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = OfficeId,
            Code = "9914600",
            Name = "OT IT 14600",
            DepartmentCode = "99",
            CityCode = "99146",
            IsActive = true,
        });
        await ctx.SaveChangesAsync();
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenantId,
            TransitOfficeId = OfficeId,
            OperationMode = "dashboard",
            CreatedAt = ahora,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = TenantSeed.LoneId,
            TransitOfficeId = OfficeId,
            IsEnabled = true,
            CreatedAt = ahora,
        });
        var rango = new PlateRangeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantSeed.LoneId,
            TransitOfficeId = OfficeId,
            Prefix = "RNG",
            RangeFrom = 101,
            RangeTo = 101,
            EditableUntil = ahora,
            CreatedAt = ahora,
        };
        ctx.PlateRanges.Add(rango);
        await ctx.SaveChangesAsync();
        ctx.PlateRangeDetails.Add(new PlateRangeDetailEntity
        {
            Id = Guid.NewGuid(),
            PlateRangeId = rango.Id,
            TenantId = TenantSeed.LoneId,
            TransitOfficeId = OfficeId,
            Plate = PlacaRango,
            State = estado,
            ProcedureInstanceId = null,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedCarroceriasAsync()
    {
        await using var ctx = NewContext();
        var ahora = DateTimeOffset.UtcNow;
        // El catálogo no se limpia entre pruebas: la siembra es idempotente por código.
        if (await ctx.VehicleBodyworks.AnyAsync(b => b.Code == "IT14600-1"))
        {
            return;
        }

        ctx.VehicleBodyworks.AddRange(
            new VehicleBodywork { Id = Guid.NewGuid(), Code = "IT14600-1", Name = "VOLQUETA IT", ClassVehicle = ClaseIt, IsActive = true, CreatedAt = ahora },
            new VehicleBodywork { Id = Guid.NewGuid(), Code = "IT14600-2", Name = "FURGON", ClassVehicle = ClaseIt, IsActive = true, CreatedAt = ahora },
            new VehicleBodywork { Id = Guid.NewGuid(), Code = "IT14600-3", Name = "RESPALDO IT", ClassVehicle = null, IsActive = true, CreatedAt = ahora });
        await ctx.SaveChangesAsync();
    }

    private sealed record Campo(string? Value, string Source);

    private async Task<Campo?> FieldAsync(Guid instanceId, string key)
    {
        await using var ctx = NewContext();
        var f = await ctx.Set<ProcedureInstanceFieldValue>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.ProcedureInstanceId == instanceId && x.FieldKey == key)
            .Select(x => new Campo(x.ValueText, x.Source))
            .FirstOrDefaultAsync();
        return f;
    }

    private async Task<PlateRangeDetailEntity> DetalleAsync(string placa)
    {
        await using var ctx = NewContext();
        return await ctx.PlateRangeDetails.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Plate == placa);
    }

    private async Task<int> CountInstancesAsync(string externalRef, bool incluirBorrados = false)
    {
        await using var ctx = NewContext();
        return await ctx.Set<ProcedureInstance>()
            .IgnoreQueryFilters()
            .CountAsync(p => p.ExternalRef == externalRef && (incluirBorrados || p.DeletedAt == null));
    }

    /// <summary>Cadena de vehículo que nunca debe llamarse: la consulta RUNT viaja precalculada.</summary>
    private sealed class CadenaProhibida : IConsultationProviderChainResolver
    {
        public IReadOnlyList<string> ResolveChain(ConsultationKind kind, ConsultationTenantOverride? tenantOverride = null) =>
            ["prohibida"];

        public Task<ConsultationResult> ConsultAsync(
            ConsultationKind kind, ConsultationContext ctx, ConsultationTenantOverride? tenantOverride, CancellationToken ct) =>
            Task.FromResult(new ConsultationResult("prohibida", "green", [], []));
    }

    /// <summary><see cref="ServerCallContext"/> mínimo: el servicio solo lee el token de cancelación.</summary>
    private sealed class TestCallContext(CancellationToken ct) : ServerCallContext
    {
        protected override string MethodCore => "/flit.ict.IctOrchestration/CreateDraftFromIct";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => ct;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
