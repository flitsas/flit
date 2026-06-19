using Flit.Tramites.Application.UseCases.Catalogs;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Tramites.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTramitesApplication(this IServiceCollection services)
    {
        services.AddScoped<IProcedureTypeValidator, ProcedureTypeValidator>();

        services.AddScoped<CreateProcedureTypeHandler>();
        services.AddScoped<ListProcedureTypesHandler>();
        services.AddScoped<GetProcedureTypeHandler>();
        services.AddScoped<UpdateProcedureTypeHandler>();
        services.AddScoped<DeleteProcedureTypeHandler>();
        services.AddScoped<PublishProcedureTypeHandler>();
        services.AddScoped<ArchiveProcedureTypeHandler>();
        services.AddScoped<ValidateProcedureTypeHandler>();
        services.AddScoped<GetConformationRulesHandler>();
        services.AddScoped<UpsertConformationRulesHandler>();
        services.AddScoped<GetProcedureStepsHandler>();
        services.AddScoped<UpsertProcedureStepsHandler>();
        services.AddScoped<GetProcedureTypeConfigurationHandler>();

        services.AddScoped<CreateProcedureInstanceHandler>();
        services.AddScoped<GetProcedureInstanceHandler>();
        services.AddScoped<PatchFieldValuesHandler>();
        services.AddScoped<SubmitProcedureInstanceHandler>();

        services.AddScoped<ListProcedureEntitiesHandler>();
        services.AddScoped<ListExternalDataSourcesHandler>();
        services.AddScoped<ListConsultationTemplatesHandler>();
        services.AddScoped<ApplyConsultationTemplateFieldsHandler>();

        return services;
    }
}
