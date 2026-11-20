using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Solicitudes;

namespace Flit.PersonaNatural.Api.Punto;

public abstract record ResultadoDiagnosticoPunto
{
    public sealed record Ok(DiagnosticoDto Diagnostico) : ResultadoDiagnosticoPunto;

    public sealed record DatosInvalidos(string Mensaje) : ResultadoDiagnosticoPunto;

    public sealed record FachadaCaida(string Mensaje) : ResultadoDiagnosticoPunto;
}

/// <summary>
/// Diagnóstico del punto (HU #14231). Reutiliza <see cref="IFachadaRequisitos"/> (mismo preflight que el portal)
/// con el organismo de la cookie. No persiste solicitud ni procedure instance.
/// </summary>
public sealed class DiagnosticoPuntoService(IFachadaRequisitos requisitos)
{
    public const string MensajeFachadaCaida = "No pudimos armar el diagnóstico. Inténtalo de nuevo.";

    public async Task<ResultadoDiagnosticoPunto> EvaluarAsync(
        OrganismoPunto sesion,
        string? placa,
        string? documento,
        string? tipoCodigo,
        string? tipoDocumento,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sesion);

        var codigo = (tipoCodigo ?? string.Empty).Trim();
        if (codigo.Length == 0)
            return new ResultadoDiagnosticoPunto.DatosInvalidos("Indique el tipo de trámite.");

        var placaNorm = EntradaConsulta.Placa(placa);
        var tipoDoc = string.IsNullOrWhiteSpace(tipoDocumento) ? "CC" : tipoDocumento.Trim();
        var propietario = EntradaConsulta.DocumentoPropietario(tipoDoc, documento);
        if (placaNorm is null || propietario is null)
            return new ResultadoDiagnosticoPunto.DatosInvalidos("Indique la placa y el documento del propietario.");

        try
        {
            var diagnostico = await requisitos.EvaluarAsync(
                codigo,
                sesion.OrganismoId.ToString(),
                placaNorm,
                vin: null,
                propietario.Tipo,
                propietario.Numero,
                ct).ConfigureAwait(false);
            return new ResultadoDiagnosticoPunto.Ok(diagnostico);
        }
        catch (FachadaException)
        {
            return new ResultadoDiagnosticoPunto.FachadaCaida(MensajeFachadaCaida);
        }
    }
}
