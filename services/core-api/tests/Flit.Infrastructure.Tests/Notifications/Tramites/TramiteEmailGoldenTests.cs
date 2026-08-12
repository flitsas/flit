using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tests.Shared;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>
/// HU #11468 — congela ASUNTO y CUERPO de <c>tramites.aprobado</c> y <c>tramites.rechazado</c>
/// en variantes FLIT y Renting, carácter a carácter, ANTES de que el disparador productivo
/// (Features #11459/#11460) toque la composición.
/// <para>
/// Sin handler de envío aún, la captura se hace en el composer vía
/// <see cref="TramiteEmailPreviewSample"/> (mismos datos sintéticos del banco). Cuando exista
/// el worker, estos golden siguen protegiendo el cuerpo que ese worker enviará.
/// </para>
/// <para>
/// <b>Si un refactor obliga a editar un <c>.golden.txt</c>, el refactor está mal.</b> Solo un
/// cambio DELIBERADO del correo justifica regenerarlos con <c>GOLDEN_UPDATE=1</c> en un commit
/// aparte que diga por qué.
/// </para>
/// </summary>
public sealed class TramiteEmailGoldenTests
{
    // URL fija: el golden protege la plantilla, no el valor configurado del CDN.
    private const string AssetsBaseUrl = "https://cdn.flit.test/email-assets";

    [Fact]
    public void Aprobado_flit_conserva_asunto_y_cuerpo()
    {
        var (subject, html) = TramiteEmailPreviewSample.BuildFlitAprobado(AssetsBaseUrl);
        EmailGolden.Assert(ToMessage(TramiteCambioEstadoEmailComposer.TemplateIdAprobado, subject, html),
            "tramites-aprobado-flit");
    }

    [Fact]
    public void Aprobado_renting_conserva_asunto_y_cuerpo()
    {
        var (subject, html) = TramiteEmailPreviewSample.BuildRentingAprobado(AssetsBaseUrl);
        EmailGolden.Assert(ToMessage(TramiteCambioEstadoEmailComposer.TemplateIdAprobado, subject, html),
            "tramites-aprobado-renting");
    }

    [Fact]
    public void Rechazado_flit_conserva_asunto_y_cuerpo()
    {
        var (subject, html) = TramiteEmailPreviewSample.BuildFlitRechazado(AssetsBaseUrl);
        EmailGolden.Assert(ToMessage(TramiteCambioEstadoEmailComposer.TemplateIdRechazado, subject, html),
            "tramites-rechazado-flit");
    }

    [Fact]
    public void Rechazado_renting_conserva_asunto_y_cuerpo()
    {
        var (subject, html) = TramiteEmailPreviewSample.BuildRentingRechazado(AssetsBaseUrl);
        EmailGolden.Assert(ToMessage(TramiteCambioEstadoEmailComposer.TemplateIdRechazado, subject, html),
            "tramites-rechazado-renting");
    }

    private static EmailMessage ToMessage(string templateKey, string subject, string html) =>
        new(TenantId: null, templateKey, "destinatario@flit.test", "Destinatario De Prueba", subject, html);
}
