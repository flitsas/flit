using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14302 (Feature #14300, Épica #14271) AC1.1 — con copropietarios (ADR-0053) cada actor recibe SU carta de identidad en el
/// expediente y no se cruzan (Bug #13194). Pipeline real sobre PostgreSQL: <see cref="GenerarFurHandler"/> +
/// <see cref="ProcedureInstanceRepository"/> + <see cref="IdentityLetterService"/>. Una de las dos cartas ya existe y la otra falta
/// (la outbox se rindió): se genera en el momento, sin duplicar y sin tocar la del otro. Solo se sustituyen el storage (memoria),
/// el generador del PDF de la carta y el del FUR.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var (r, error) = await handler.HandleAsync(instanciaId, tenantId, ct);
/// // r.Documents incluye certificado_identidad (copropietario 1) y certificado_identidad_2 (copropietario 2), cada uno con su carta.
/// </code>
/// Datos ficticios: documentos 9000014xxx.
/// </remarks>
public sealed class CartaIdentidadExpedienteIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = HierarchyScenario.C1;
    private static readonly Guid Instancia = new("5a5a5a5a-00e1-4000-8000-000000014302");
    private const string DocA = "9000014311";
    private const string DocB = "9000014312";

    private sealed class MemoryStorage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> SavedByTipo { get; } = new(StringComparer.Ordinal);

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"it14302/{procedureInstanceId:D}/{tipo}";
            Blobs[path] = ms.ToArray();
            SavedByTipo[tipo] = ms.ToArray();
            return new StoredFile(path, new string('c', 64), ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Blobs.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private sealed class FakeFur : IFurDocumentGenerator
    {
        public GeneratedDocument GenerateFur(FurDocumentData data) =>
            new("fur", "fur.pdf", "application/pdf", [1, 2, 3]);

        public GeneratedDocument GenerateFurFillAll(FurTemplateFormat format = FurTemplateFormat.Automotor) =>
            new("fur", "fur_FILLALL.pdf", "application/pdf", [1, 2, 3]);

        public GeneratedDocument GenerateCompraventa(FurDocumentData data) =>
            new("compraventa", "cv.pdf", "application/pdf", [1, 2, 3]);
    }

    /// <summary>Genera un PDF marcado con el documento del titular: permite ver de quién es cada carta.</summary>
    private sealed class MarkerLetterPdf : IIdentityLetterPdfGenerator
    {
        public byte[] Generate(IdentityLetterData data) => Encoding.UTF8.GetBytes($"CARTA-GENERADA-{data.DocumentNumber}");
    }

    [PostgresFact]
    public async Task AC1_1_Copropietarios_CadaActorRecibeSuCarta_ConUnaYaGeneradaYOtraBajoDemanda_SinCruzarse()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new MemoryStorage();
        var (validacionA, validacionB) = await SembrarAsync(storage, ct);

        await using (var ctx = NewContext())
        {
            var repo = new ProcedureInstanceRepository(ctx);
            var handler = new GenerarFurHandler(
                repo, new FakeFur(), Substitute.For<IKyverumCertificateClient>(), Substitute.For<IRuesCertificateGenerator>(),
                Substitute.For<IRnmcCertificateGenerator>(), Substitute.For<IProcedureInstancePrendaRepository>(), storage,
                NullLogger<GenerarFurHandler>.Instance,
                identityLetterService: new IdentityLetterService(repo, storage, new MarkerLetterPdf()));

            var (result, error) = await handler.HandleAsync(Instancia, Tenant, ct);

            error.Should().BeNull();
            result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(
                ["fur", "certificado_identidad", "certificado_identidad_2"]);
        }

        // Cada copropietario, SU carta: el ordinal 1 la que ya existía; el ordinal 2 la generada en el momento.
        Encoding.UTF8.GetString(storage.SavedByTipo["certificado_identidad"]).Should().Be("CARTA-YA-EXISTENTE-A");
        Encoding.UTF8.GetString(storage.SavedByTipo["certificado_identidad_2"]).Should().Be($"CARTA-GENERADA-{DocB}");

        await using var verify = NewContext();
        var a = await verify.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(v => v.Id == validacionA, ct);
        var b = await verify.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(v => v.Id == validacionB, ct);
        a.CartaSerie.Should().Be("FL-AAAAAAAA-AAAAAA", "la carta existente no se regenera");
        b.TieneCartaIdentidad.Should().BeTrue("la carta faltante se generó y se registró");
        b.CartaSerie.Should().StartWith("FL-").And.NotBe(a.CartaSerie);
        b.CartaPath.Should().NotBe(a.CartaPath);

        var adjuntos = await verify.Set<ProcedureInstanceAttachment>().AsNoTracking()
            .Where(x => x.ProcedureInstanceId == Instancia && x.Tipo.StartsWith("certificado_identidad"))
            .Select(x => x.Tipo).ToListAsync(ct);
        adjuntos.Should().BeEquivalentTo(["certificado_identidad", "certificado_identidad_2"]);
    }

    private async Task<(Guid A, Guid B)> SembrarAsync(MemoryStorage storage, CancellationToken ct)
    {
        var tipo = await HierarchyScenario.SeedAsync(Fixture);
        var validacionA = Guid.NewGuid();
        var validacionB = Guid.NewGuid();

        foreach (var ruta in new[] { "r/rostro", "r/anverso", "r/reverso", "r/firma" })
            storage.Blobs[ruta] = [1, 2, 3, 4];
        storage.Blobs["it14302/carta-a.pdf"] = Encoding.UTF8.GetBytes("CARTA-YA-EXISTENTE-A");

        await using var ctx = NewContext();
        var entidades = await ctx.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM tramites.procedure_entities ORDER BY id LIMIT 2")
            .ToListAsync(ct);

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = Instancia,
            TenantId = Tenant,
            ProcedureTypeId = tipo.Id,
            ReferenceNumber = "IT14302E",
            Status = TramiteEstado.Borrador,
            Plate = "ITE302",
            CreatedByUserId = HierarchyScenario.UserOf(Tenant),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Set<ProcedureInstanceFieldValue>().Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = Instancia,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });

        var copropietarios = new[] { (DocA, 1, validacionA, entidades[0]), (DocB, 2, validacionB, entidades[^1]) };
        foreach (var (doc, ordinal, validacion, entidad) in copropietarios)
        {
            ctx.Set<ProcedureInstanceActor>().Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                ProcedureInstanceId = Instancia,
                ProcedureEntityId = entidad,
                ActorType = "comprador",
                PersonType = "natural",
                DocumentType = "CC",
                DocumentNumber = doc,
                FullName = $"Copropietario {ordinal}",
                Email = $"c{ordinal}@example.test",
                Ordinal = ordinal,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            var v = new ProcedureInstanceBiometricValidation
            {
                Id = validacion,
                TenantId = Tenant,
                ProcedureInstanceId = Instancia,
                PartyRole = "comprador",
                Status = BiometricEstados.Aprobado,
                Provider = BiometricProviders.Manual,
                ApprovalOrigin = BiometricApprovalOrigins.Manual,
                Name = $"Copropietario {ordinal}",
                DocumentType = "CC",
                DocumentNumber = doc,
                Email = $"c{ordinal}@example.test",
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                ValidatedAt = DateTimeOffset.UtcNow,
                ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
                CreatedAt = DateTimeOffset.UtcNow,
                FacePhotoPath = "r/rostro",
                IdFrontPhotoPath = "r/anverso",
                IdBackPhotoPath = "r/reverso",
                SignatureImagePath = "r/firma",
                SignatureImageSha256 = new string('a', 64),
                ConsentAt = DateTimeOffset.UtcNow,
                ConsentIp = "203.0.113.7",
                ConsentTextVersion = "manual-ley1581-v2",
            };
            if (ordinal == 1)
                v.RegistrarCartaIdentidad("FL-AAAAAAAA-AAAAAA", new string('a', 64), new string('b', 64), "it14302/carta-a.pdf",
                    DateTimeOffset.UtcNow);
            ctx.Set<ProcedureInstanceBiometricValidation>().Add(v);
        }

        await ctx.SaveChangesAsync(ct);
        return (validacionA, validacionB);
    }
}
