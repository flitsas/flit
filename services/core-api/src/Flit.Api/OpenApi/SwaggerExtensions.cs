using Microsoft.OpenApi;

namespace Flit.Api.OpenApi;

/// <summary>
/// Configuración de Swagger/OpenAPI (Swashbuckle) para Flit.Api. Genera el documento
/// OpenAPI a partir de los endpoints minimal API (tags, summaries y respuestas declaradas
/// con <c>.WithTags/.WithSummary/.Produces</c>) y expone Swagger UI SOLO en Development.
///
/// La autenticación es JWT Bearer: el mismo token SuperAdmin que exigen los endpoints
/// <c>/api/v1/admin/*</c>. El botón «Authorize» de la UI permite pegar el JWT y probar.
/// </summary>
public static class SwaggerExtensions
{
    private const string BearerScheme = "Bearer";

    /// <summary>Registra el generador OpenAPI + el esquema de seguridad Bearer.</summary>
    public static IServiceCollection AddFlitSwagger(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "FLIT Core API",
                Version = "v1",
                Description =
                    "Contrato vivo de FLIT Core API (services/core-api). Cubre Seguridad/Autenticación, "
                    + "el Administrador de Compañías (feature #10118) y la Gestión Documental por Trámite "
                    + "(feature #10138). Todos los endpoints /api/v1/admin/* exigen rol SuperAdmin.",
            });

            // Esquema Bearer: token JWT en el header Authorization (rol SuperAdmin).
            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Pega el JWT (sin el prefijo «Bearer»). El claim role debe ser SuperAdmin.",
            });

            // Aplica el requisito Bearer a todas las operaciones (las anónimas como /health
            // siguen siendo accesibles; el candado es solo informativo en esas).
            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference(BearerScheme), new List<string>() },
            });

            // Evita colisiones de schemaId entre records anidados con el mismo nombre simple
            // (varios endpoints declaran un ErrorResponse privado homónimo).
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
        });

        return services;
    }

    /// <summary>Monta el JSON OpenAPI y la UI Swagger. Llamar SOLO en Development.</summary>
    public static WebApplication UseFlitSwagger(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "FLIT Core API v1");
            options.DocumentTitle = "FLIT Core API — Swagger";
        });

        return app;
    }
}
