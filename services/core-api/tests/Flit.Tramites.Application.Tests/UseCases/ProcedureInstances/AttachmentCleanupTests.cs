using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// DT-3 (HU #11316, Feature #11309) — <see cref="AttachmentCleanup"/> es la guarda compartida de las
/// ramas «generar-o-limpiar»: solo retira lo GENERADO POR EL SISTEMA. Un adjunto de cualquier otro
/// origen (usuario, organismo, ICT, o personalizado por la compañía) sobrevive intacto, con su archivo.
/// </summary>
public sealed class AttachmentCleanupTests
{
    private static ProcedureInstance Instance() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstanceAttachment Attachment(ProcedureInstance instance, string tipo, string source, string path) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            Source = source,
            StoragePath = path,
            UploadedAt = DateTimeOffset.UtcNow,
        };

    [Theory]
    [InlineData("company")]
    [InlineData("user")]
    [InlineData("ot")]
    [InlineData("ict")]
    public void RetirarGenerados_SoloRetiraElOrigenSistema_ElRestoSobrevive(string otroOrigen)
    {
        var instance = Instance();
        var sistema = Attachment(instance, "mandato", "system", "path/sistema.pdf");
        var otro = Attachment(instance, "mandato", otroOrigen, "path/otro.pdf");
        instance.Attachments.Add(sistema);
        instance.Attachments.Add(otro);

        var repo = Substitute.For<IProcedureInstanceRepository>();
        var storage = Substitute.For<IAttachmentStorage>();

        AttachmentCleanup.RetirarGenerados(instance, repo, storage,
            a => a.Tipo == "mandato");

        instance.Attachments.Should().ContainSingle().Which.Should().BeSameAs(otro);
        storage.Received(1).Delete("path/sistema.pdf");
        storage.DidNotReceive().Delete("path/otro.pdf");
        repo.Received(1).RemoveAttachment(sistema);
        repo.DidNotReceive().RemoveAttachment(otro);
    }

    [Fact]
    public void RetirarGenerados_RespetaElPredicadoDeTipo_NoTocaOtrosTipos()
    {
        var instance = Instance();
        var mandatoSistema = Attachment(instance, "mandato", "system", "path/mandato.pdf");
        var furSistema = Attachment(instance, "fur", "system", "path/fur.pdf");
        instance.Attachments.Add(mandatoSistema);
        instance.Attachments.Add(furSistema);

        var repo = Substitute.For<IProcedureInstanceRepository>();
        var storage = Substitute.For<IAttachmentStorage>();

        AttachmentCleanup.RetirarGenerados(instance, repo, storage,
            a => a.Tipo == "mandato");

        instance.Attachments.Should().ContainSingle().Which.Should().BeSameAs(furSistema);
        storage.DidNotReceive().Delete("path/fur.pdf");
    }

    [Fact]
    public void RetirarGenerados_SinCoincidencias_NoHaceNada()
    {
        var instance = Instance();
        var otro = Attachment(instance, "mandato", "user", "path/otro.pdf");
        instance.Attachments.Add(otro);

        var repo = Substitute.For<IProcedureInstanceRepository>();
        var storage = Substitute.For<IAttachmentStorage>();

        AttachmentCleanup.RetirarGenerados(instance, repo, storage, a => a.Tipo == "mandato");

        instance.Attachments.Should().ContainSingle();
        storage.DidNotReceiveWithAnyArgs().Delete(default!);
        repo.DidNotReceiveWithAnyArgs().RemoveAttachment(default!);
    }

    [Fact]
    public void EsGeneradoPorElSistema_EsCaseInsensitive()
    {
        var instance = Instance();
        var attachment = Attachment(instance, "mandato", "SYSTEM", "path/x.pdf");

        AttachmentCleanup.EsGeneradoPorElSistema(attachment).Should().BeTrue();
    }
}
