using Flit.Ict.Application.Attachments;
using Flit.Ict.Application.Auth.Login;
using Flit.Ict.Application.Edit;
using Flit.Ict.Application.Register;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Ict.Application;

/// <summary>Registro de los casos de uso (handlers POCO) de la capa Application de ICT.</summary>
public static class IctApplicationExtensions
{
    public static IServiceCollection AddIctApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginIntegrationClientHandler>();
        services.AddScoped<RegisterIctBatchHandler>();
        services.AddScoped<EditPreTramiteHandler>();
        services.AddScoped<PresignAttachmentHandler>();
        services.AddScoped<RegisterAttachmentHandler>();
        services.AddScoped<ListAttachmentsHandler>();
        return services;
    }
}
