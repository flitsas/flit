using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Reporte 2026-09-03 — <b>un rechazo/vencimiento de identidad no puede sobrevivir al reemplazo de la
/// persona que ocupa la parte.</b>
///
/// <para><b>Qué se corrige.</b> <c>DeriveFirmaParte</c> marcaba «Rechazado» con <c>Any(...)</c> sobre TODO
/// el historial de <c>BiometricValidations</c> que compartiera <c>PartyRole</c>, sin mirar a qué persona
/// pertenecía la fila. Al cambiar el representante legal de una parte jurídica (correo distinto → HU
/// #10880 expira la validación previa y reenvía una nueva al RL nuevo), la fila vieja del RL ANTERIOR
/// seguía teniendo <c>PartyRole = "comprador"</c> y <c>Status = Expirado</c>, así que la columna del
/// listado acusaba «Rechazado» al RL NUEVO aunque a este nunca se le hubiera enviado nada — mientras el
/// wizard (que correlaciona por actor/documento) mostraba «Pendiente de validación» para la misma fila.
/// Reproduce exactamente la discrepancia reportada entre el wizard y el dashboard para el mismo trámite.</para>
/// </summary>
public sealed class ColumnaFirmadoPersonaAnteriorTests
{
    private const string DocumentoRlAnterior = "1000000001";
    private const string DocumentoRlActual = "1090123456";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    [Fact]
    public async Task RepresentanteLegalReemplazado_LaFilaVencidaDelAnteriorNoRechazaAlNuevo()
    {
        var instance = Tramite();
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = "comprador",
            DocumentType = "CC",
            DocumentNumber = DocumentoRlAnterior, // el RL de ANTES del reemplazo, no el actual.
            Name = "Representante Anterior",
            Email = "anterior@x.com",
            Status = BiometricEstados.Expirado, // HU #10880: expirada al cambiar el correo del sujeto.
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        });
        Preparar(instance);

        var filas = await new ListProcedureInstancesHandler(_repo)
            .HandleAsync(instance.TenantId, TestContext.Current.CancellationToken);

        // El RL actual nunca recibió ni rechazo ni vencimiento propio: la columna no puede acusarlo de
        // algo que le pasó a la persona que ocupaba el rol antes que él.
        filas.Should().ContainSingle().Which.FirmaCompradorEstado
            .Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task RepresentanteLegalReemplazado_UnRechazoPropioSiSigueRechazando()
    {
        // Control: si la fila rechazada SÍ es del documento del RL actual, la columna debe seguir
        // acusándolo — el fix filtra por persona, no desactiva el rechazo.
        var instance = Tramite();
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = "comprador",
            DocumentType = "CC",
            DocumentNumber = DocumentoRlActual, // el RL ACTUAL.
            Name = "Ana Representante",
            Email = "ana@x.com",
            Status = BiometricEstados.Rechazado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        Preparar(instance);

        var filas = await new ListProcedureInstancesHandler(_repo)
            .HandleAsync(instance.TenantId, TestContext.Current.CancellationToken);

        filas.Should().ContainSingle().Which.FirmaCompradorEstado
            .Should().Be(FirmaParteEstados.Rechazado);
    }

    [Fact]
    public async Task UnRechazoPropioViejoNoPesaSiElReintentoMasRecienteSigueEnCurso()
    {
        // La MISMA persona, dos filas: una rechazada vieja y un reintento nuevo en curso. Manda la más
        // reciente — igual que "lo positivo gana" para la aprobación, "lo más reciente manda" para el
        // rechazo: mientras el reintento sigue vivo, la columna no puede seguir clavada en «rechazado».
        var instance = Tramite();
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = "comprador",
            DocumentType = "CC",
            DocumentNumber = DocumentoRlActual,
            Name = "Ana Representante",
            Email = "ana@x.com",
            Status = BiometricEstados.Rechazado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = "comprador",
            DocumentType = "CC",
            DocumentNumber = DocumentoRlActual,
            Name = "Ana Representante",
            Email = "ana@x.com",
            Status = BiometricEstados.Enviado,
            CreatedAt = DateTimeOffset.UtcNow, // más reciente que el rechazo.
        });
        Preparar(instance);

        var filas = await new ListProcedureInstancesHandler(_repo)
            .HandleAsync(instance.TenantId, TestContext.Current.CancellationToken);

        filas.Should().ContainSingle().Which.FirmaCompradorEstado
            .Should().Be(FirmaParteEstados.Pendiente);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Preparar(ProcedureInstance instance)
    {
        _repo.ListWithSummaryGraphAsync(
                instance.TenantId, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([instance]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        // Sin firma del baúl registrada para nadie: aísla el bug de la fila biométrica vieja del
        // representante anterior, sin que el baúl entre a decidir por su cuenta.
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>(StringComparer.Ordinal));
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
    }

    /// <summary>
    /// Matrícula inicial en borrador con un comprador jurídico cuyo representante legal ACTUAL es
    /// <see cref="DocumentoRlActual"/> (CC) — el mismo escenario del reporte: comprador tipo S.A.
    /// </summary>
    private static ProcedureInstance Tramite()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000083",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "vin",
            ValueText = "1HGCM82633A004352",
            Source = "user",
        });
        instance.PreflightSnapshots.Add(new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(),
            Overall = "green",
            Checks = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            PersonType = ActorPersonTypes.Juridical,
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "Bancolombia S.A",
            Email = "contacto@bancolombia.com",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize(
                "Bogotá",
                "Calle 1 # 2-3",
                new ActorRepresentanteLegal(
                    "CC", DocumentoRlActual, "Ana Representante", "ana@x.com", null, null)),
        });
        foreach (var tipo in new[] { "factura", "aduana", "impronta" })
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
            });

        return instance;
    }
}
