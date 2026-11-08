using System.Reflection;
using System.Text.Json;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Signatures;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14301 (Feature #14300, Épica #14271) — <c>IdentityValidationOutboxProcessor</c> contra PostgreSQL real cuando la carta de
/// identidad falla: el consumidor de firma/FUR y el lote diferido SÍ corren, el evento queda sin publicar con un intento más, el
/// ciclo siguiente genera la carta y sella <c>published_at</c> sin duplicar, y una validación de Kyverum se sella sin carta.
/// También cubre el riesgo de la transacción abortada: un error de Postgres dentro del guardado de la carta no debe impedir que
/// el procesador cuente el intento (savepoint alrededor de la carta).
/// </summary>
public sealed class CartaIdentidadOutboxTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Company = new("b2000000-0000-7000-8000-0000000000f2");
    private static readonly Guid Person = new("0199c200-0000-7000-8000-0000000000f3");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _consumerRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IDeferredSignatureMarkRepository _marks = Substitute.For<IDeferredSignatureMarkRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IIdentityLetterPdfGenerator _generator = Substitute.For<IIdentityLetterPdfGenerator>();
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private int _cartasGuardadas;
    private Func<StoredFile> _alGuardar = () => new StoredFile("carta/ok", new string('a', 64), 11);

    private async Task<(Guid ValidationId, Guid OutboxId)> SeedAsync(string provider)
    {
        await using (var ctx = NewContext())
        {
            if (!await ctx.Tenants.AnyAsync(t => t.Id == Company, Ct))
            {
                ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-OB", false, null));
                await ctx.SaveChangesAsync(Ct);
            }
        }

        var manual = provider == BiometricProviders.Manual;
        var id = Guid.NewGuid();
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.persons (id, tenant_id, document_type, document_number, full_name, email, created_at)
            VALUES (@p, @t, 'CC', '1000000015', 'Persona de prueba', 'p@flit.test', now())
            ON CONFLICT DO NOTHING;
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, person_id, name, document_type, document_number, email, status, provider, approval_origin,
               token_hash, expires_at, created_at, validated_at, valid_until, face_photo_path, id_front_photo_path,
               id_back_photo_path, signature_image_path, signature_image_sha256, consent_at, consent_ip, consent_text_version)
            VALUES (@v, @t, @p, 'Persona', 'CC', '1000000015', 'p@flit.test', 'aprobado', @prov, @origin,
                    @h, now() + interval '1 hour', now(), now(), now() + interval '30 days', 'r/rostro', 'r/anverso',
                    'r/reverso', 'r/firma', repeat('a', 64), now(), '203.0.113.7', 'manual-ley1581-v2');
            """,
            ("p", Person), ("t", Company), ("v", id), ("prov", provider), ("origin", manual ? "manual" : "automatica"),
            ("h", Guid.NewGuid().ToString("N")));

        var outboxId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.IdentityValidationOutbox.Add(new IdentityValidationOutbox
            {
                Id = outboxId,
                TenantId = Company,
                ValidationId = id,
                EventType = IdentityValidationEventTypes.Completed,
                Payload = JsonSerializer.Serialize(
                    new IdentityValidationCompleted
                    {
                        TenantId = Company,
                        ValidationId = id,
                        Provider = provider,
                        Estado = BiometricEstados.Aprobado,
                    },
                    WebJson),
                OccurredAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        return (id, outboxId);
    }

    private IdentityValidationOutboxProcessor Processor()
    {
        _storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream([1, 2, 3]));
        _storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var stored = _alGuardar();
                _cartasGuardadas++;
                return stored;
            });
        _generator.Generate(Arg.Any<IdentityLetterData>()).Returns([37, 80, 68, 70]);

        // El consumidor de firma/FUR y el lote diferido corren con dobles: lo que se verifica es que el procesador LOS INVOCA.
        _consumerRepo.GetBiometricByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci =>
            new ProcedureInstanceBiometricValidation
            {
                Id = ci.Arg<Guid>(), TenantId = Company, DocumentType = "CC", DocumentNumber = "1000000015",
            });
        _consumerRepo.ListPendientesDeFirmaPorSujetoAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProcedureInstance>>([]));
        _marks.ListPendientesByRepresentativeAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DeferredSignatureMark>>([]));

        var services = new ServiceCollection();
        services.AddScoped(_ => Fixture.CreateDbContext());
        services.AddScoped<ISavepointScope>(sp => new EfSavepointScope(sp.GetRequiredService<FlitDbContext>()));
        services.AddScoped(_ => Substitute.For<IIdentitySignatureCapture>());
        services.AddScoped(_ => new IdentityValidationCompletedConsumer(
            _consumerRepo,
            new SolicitarFirmaHandler(_consumerRepo, Substitute.For<ISignatureProvider>()),
            new GenerarFurHandler(
                _consumerRepo, Substitute.For<IFurDocumentGenerator>(), Substitute.For<IKyverumCertificateClient>(),
                Substitute.For<IRuesCertificateGenerator>(), Substitute.For<IRnmcCertificateGenerator>(),
                Substitute.For<IProcedureInstancePrendaRepository>(), _storage,
                NullLogger<GenerarFurHandler>.Instance)));
        services.AddScoped(_ => new DeferredSignatureBatchConsumer(
            _consumerRepo, _marks, Substitute.For<ITramiteFirmaAplicador>()));
        services.AddScoped<IIdentityLetterService>(sp => new IdentityLetterService(
            new ProcedureInstanceRepository(sp.GetRequiredService<FlitDbContext>()), _storage, _generator));

        return new IdentityValidationOutboxProcessor(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IdentityValidationOutboxProcessor>.Instance);
    }

    /// <summary>Un solo ciclo del procesador (lo que hace <c>ProcessPendingAsync</c> por fila), para ver el estado tras UN intento.</summary>
    private static async Task<bool> UnCicloAsync(IdentityValidationOutboxProcessor p)
    {
        var m = typeof(IdentityValidationOutboxProcessor).GetMethod(
            "ProcessNextClaimedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<bool>)m.Invoke(p, [Ct])!;
    }

    private async Task<(DateTimeOffset? Published, int Attempts)> OutboxAsync(Guid id)
    {
        await using var ctx = NewContext();
        var o = await ctx.IdentityValidationOutbox.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        return (o.PublishedAt, o.Attempts);
    }

    private async Task<ProcedureInstanceBiometricValidation> ValidationAsync(Guid id)
    {
        await using var ctx = NewContext();
        return await ctx.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private void ConsumidoresCorrieron(int veces)
    {
        _consumerRepo.Received(veces).ListPendientesDeFirmaPorSujetoAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _marks.Received(veces).ListPendientesByRepresentativeAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [PostgresFact]
    public async Task AC4_SiLaCartaFallaCorrenFirmaYLoteDiferido_ElEventoQuedaSinPublicarConUnIntento_YElSiguienteCicloLaGenera()
    {
        var (validationId, outboxId) = await SeedAsync(BiometricProviders.Manual);
        _alGuardar = () => throw new IOException("storage caído");
        var p = Processor();

        (await UnCicloAsync(p)).Should().BeTrue();

        ConsumidoresCorrieron(1);
        var tras1 = await OutboxAsync(outboxId);
        tras1.Published.Should().BeNull();
        tras1.Attempts.Should().Be(1);
        (await ValidationAsync(validationId)).CartaPath.Should().BeNull();
        (await ValidationAsync(validationId)).Status.Should().Be(BiometricEstados.Aprobado);

        _alGuardar = () => new StoredFile("carta/ok", new string('a', 64), 11);
        (await UnCicloAsync(p)).Should().BeTrue();

        var tras2 = await OutboxAsync(outboxId);
        tras2.Published.Should().NotBeNull();
        tras2.Attempts.Should().Be(2);
        var v = await ValidationAsync(validationId);
        v.CartaPath.Should().Be("carta/ok");
        v.CartaSerie.Should().StartWith("FL-");
        _cartasGuardadas.Should().Be(1, "el intento fallido no guardó nada: una sola carta");

        (await UnCicloAsync(p)).Should().BeFalse("el evento ya está publicado: no se reclama otra vez");
        _cartasGuardadas.Should().Be(1);
        (await ValidationAsync(validationId)).CartaPath.Should().Be("carta/ok");
    }

    [PostgresFact]
    public async Task AC5_UnaValidacionDeKyverumSellaPublishedAtSinGenerarCarta()
    {
        var (validationId, outboxId) = await SeedAsync(BiometricProviders.Kyverum);
        var p = Processor();

        await p.ProcessPendingAsync(Ct);

        ConsumidoresCorrieron(1);
        var o = await OutboxAsync(outboxId);
        o.Published.Should().NotBeNull();
        o.Attempts.Should().Be(1);
        _cartasGuardadas.Should().Be(0);
        _generator.DidNotReceive().Generate(Arg.Any<IdentityLetterData>());
        (await ValidationAsync(validationId)).TieneCartaIdentidad.Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC4_UnFalloPersistenteSeReintentaHastaElTopeYQuedaEnDeadLetterSinBloquearLosConsumidores()
    {
        var (_, outboxId) = await SeedAsync(BiometricProviders.Manual);
        _alGuardar = () => throw new IOException("storage caído");
        var p = Processor();

        await p.ProcessPendingAsync(Ct);

        var o = await OutboxAsync(outboxId);
        o.Published.Should().BeNull();
        o.Attempts.Should().Be(IdentityValidationOutbox.MaxDeliveryAttempts);
        ConsumidoresCorrieron(IdentityValidationOutbox.MaxDeliveryAttempts);
    }

    [PostgresFact]
    public async Task UnErrorDePostgresAlGuardarLaCartaNoAbortaLaTransaccionDelProcesador_ElIntentoSeCuenta()
    {
        var (validationId, outboxId) = await SeedAsync(BiometricProviders.Manual);
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            // Error REAL de Postgres en el UPDATE de la carta (aborta la transacción si nadie la protege con un savepoint).
            await ExecAsync(cn,
                """
                CREATE OR REPLACE FUNCTION public.it_14301_falla_carta() RETURNS trigger LANGUAGE plpgsql AS
                $$ BEGIN IF NEW.letter_path = 'FALLA' THEN RAISE EXCEPTION 'falla simulada de la carta'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER it_14301_falla_carta BEFORE UPDATE ON tramites.procedure_instance_biometric_validations
                  FOR EACH ROW EXECUTE FUNCTION public.it_14301_falla_carta();
                """);
        }

        try
        {
            _alGuardar = () => new StoredFile("FALLA", new string('a', 64), 11);
            var p = Processor();

            var act = () => UnCicloAsync(p);

            await act.Should().NotThrowAsync("el fallo de la carta no puede tumbar el guardado de attempts");
            ConsumidoresCorrieron(1);
            var tras1 = await OutboxAsync(outboxId);
            tras1.Attempts.Should().Be(1, "el intento se cuenta aunque la transacción haya visto un error de Postgres");
            tras1.Published.Should().BeNull();
            (await ValidationAsync(validationId)).CartaPath.Should().BeNull();

            _alGuardar = () => new StoredFile("carta/ok", new string('a', 64), 11);
            (await UnCicloAsync(p)).Should().BeTrue();
            (await OutboxAsync(outboxId)).Published.Should().NotBeNull();
            (await ValidationAsync(validationId)).CartaPath.Should().Be("carta/ok");
        }
        finally
        {
            await using var cn = await Fixture.OpenConnectionAsync();
            await ExecAsync(cn,
                """
                DROP TRIGGER IF EXISTS it_14301_falla_carta ON tramites.procedure_instance_biometric_validations;
                DROP FUNCTION IF EXISTS public.it_14301_falla_carta();
                """);
        }
    }
}
