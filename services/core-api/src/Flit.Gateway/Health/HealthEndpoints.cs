namespace Flit.Gateway.Health;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Content("""{"status":"alive"}""", "application/json"))
           .AllowAnonymous();

        app.MapGet("/ready", async (HttpContext ctx) =>
        {
            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var httpClientFactory = ctx.RequestServices.GetRequiredService<IHttpClientFactory>();
            var coreApiBase = config["Health:CoreApiBaseUrl"]
                ?? config["ReverseProxy:Clusters:core-api-cluster:Destinations:core-api-1:Address"]
                ?? "http://localhost:4003/";
            var healthUri = new Uri(new Uri(coreApiBase.TrimEnd('/') + "/"), "health");

            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            try
            {
                var apiHealth = await client.GetAsync(healthUri).ConfigureAwait(false);
                if (!apiHealth.IsSuccessStatusCode) return Results.StatusCode(503);
            }
            catch
            {
                return Results.StatusCode(503);
            }

            return Results.Content("""{"status":"ready"}""", "application/json");
        }).AllowAnonymous();
    }
}
