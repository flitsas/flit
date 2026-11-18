using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Puntos de intervención de las pruebas del envío automático de DP: se arman en la prueba y se limpian al terminar.</summary>
public sealed class GanchosPlantillas
{
    /// <summary>Se ejecuta justo antes de armar el PDF del DP (con el candado del comparendo ya puesto).</summary>
    public Func<Task>? AlArmarDp { get; set; }

    /// <summary>El PDF se arma con una versión de texto SIN revisión jurídica (como si se hubiera publicado una nueva a medias).</summary>
    public bool ArmarSinRevision { get; set; }

    public void Limpiar() => (AlArmarDp, ArmarSinRevision) = (null, false);
}

public sealed class PlantillasConGancho(ComparendosDb db, IConfiguration configuration, GanchosPlantillas ganchos) : ServicioPlantillas(db, configuration)
{
    public override async Task<PlantillaVersion?> PublicadaAsync(ConjuntoPlantillas conjunto, TipoPlantilla tipo, Guid? tipologiaId, CancellationToken ct)
    {
        return await base.PublicadaAsync(conjunto, tipo, tipologiaId, ct).ConfigureAwait(false);
    }

    public override async Task<Armado> ArmarAsync(ConjuntoPlantillas conjunto, TipoPlantilla tipo, Guid? tipologiaId, IReadOnlyDictionary<string, string?> valores, CancellationToken ct)
    {
        if (tipo == TipoPlantilla.DpDocumento)
        {
            if (ganchos.AlArmarDp is { } gancho)
                await gancho().ConfigureAwait(false);
            if (ganchos.ArmarSinRevision)
            {
                var v = await PublicadaAsync(conjunto, tipo, tipologiaId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Sin plantilla.");
                var sinRevisar = new PlantillaVersion
                {
                    Id = Guid.NewGuid(), PlantillaId = v.PlantillaId, NumeroVersion = v.NumeroVersion + 1, Estado = EstadoPlantillaVersion.Publicada,
                    Asunto = v.Asunto, CuerpoHtml = v.CuerpoHtml, VariablesUsadas = v.VariablesUsadas, RevisadaJuridicamenteEn = null,
                };
                return Armar(sinRevisar, tipo, valores, conjunto, Recursos);
            }
        }

        return await base.ArmarAsync(conjunto, tipo, tipologiaId, valores, ct).ConfigureAwait(false);
    }
}

public sealed class ApiConGanchosDp : ApiConProductos
{
    public GanchosPlantillas Ganchos { get; } = new();

    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
        services.AddSingleton(Ganchos);
        services.AddScoped<ServicioPlantillas>(sp => new PlantillasConGancho(sp.GetRequiredService<ComparendosDb>(), sp.GetRequiredService<IConfiguration>(), Ganchos));
    }
}
