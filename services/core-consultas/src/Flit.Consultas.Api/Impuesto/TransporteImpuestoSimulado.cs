using System.Collections.Concurrent;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Simulación asíncrona con fixtures <c>impuesto-v2</c>. La marca es el penúltimo carácter de la placa (en el id externo).
/// Marcas del POST (rechazo inmediato, sin sondeo): <c>D</c> → 422 <c>department_unsupported</c> (sin fuente para el
/// departamento), <c>F</c> → 409 <c>source_inactive</c> (fuente inactiva). Marca del sondeo: <c>G</c> → <c>failed</c> con
/// <c>source_inactive</c> (la fuente se desactivó entre el POST y el worker). Dígitos 0-9: ver <see cref="ConsultarAsync"/>.
/// Datos siempre sintéticos (placas <c>TST…</c>).
/// </summary>
internal sealed class TransporteImpuestoSimulado : ITransporteImpuesto
{
    private readonly ConcurrentDictionary<string, int> _consultas = new(StringComparer.Ordinal);

    public bool Listo => true;

    public string NombreProveedor => "impuesto_simulado";

    public Task<EnvioImpuesto> EnviarAsync(string cuerpo, string apiKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = apiKey;
        using var documento = JsonDocument.Parse(cuerpo);
        var placa = documento.RootElement.TryGetProperty("plateNumber", out var valor) ? valor.GetString() ?? "" : "";
        var marca = placa.Length >= 2 ? placa[^2] : '2';
        switch (marca)
        {
            case 'D':
                return Task.FromResult(new EnvioImpuesto(null, new FalloProveedor(ClaseFalloProveedor.Rechazo, 422), "department_unsupported"));
            case 'F':
                return Task.FromResult(new EnvioImpuesto(null, new FalloProveedor(ClaseFalloProveedor.Rechazo, 409), "source_inactive"));
        }

        var id = "sim-" + marca + "-" + Guid.CreateVersion7().ToString("N");
        return Task.FromResult(new EnvioImpuesto(id, null, null));
    }

    public Task<EstadoImpuesto> ConsultarAsync(string idExterno, string apiKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = apiKey;
        var marca = Marca(idExterno);
        var n = _consultas.AddOrUpdate(idExterno, 1, (_, v) => v + 1);
        var (estado, archivo) = marca switch
        {
            '0' => (EstadoTrabajoExterno.Exitoso, "resultado-con-deuda.json"),
            '1' => (EstadoTrabajoExterno.Exitoso, "resultado-con-deuda-sin-total.json"),
            '2' => (EstadoTrabajoExterno.Exitoso, "resultado-paz-y-salvo.json"),
            '3' => (EstadoTrabajoExterno.Parcial, "resultado-parcial-con-deuda.json"),
            '4' => (EstadoTrabajoExterno.Ejecutando, "get-ejecutando.json"),
            '5' => (EstadoTrabajoExterno.Parcial, "resultado-parcial-sin-deuda.json"),
            '6' => (EstadoTrabajoExterno.Exitoso, "resultado-mismatch-ownername.json"),
            '7' => (EstadoTrabajoExterno.Fallido, "resultado-fallido.json"),
            '8' => (EstadoTrabajoExterno.Exitoso, "resultado-mismatch-servicio-con-avisos.json"),
            '9' when n == 1 => (EstadoTrabajoExterno.EnCola, "get-en-cola.json"),
            '9' => (EstadoTrabajoExterno.Exitoso, "resultado-paz-y-salvo.json"),
            'G' => (EstadoTrabajoExterno.Fallido, "resultado-fuente-inactiva.json"),
            _ => (EstadoTrabajoExterno.Exitoso, "resultado-paz-y-salvo.json"),
        };

        if (estado is EstadoTrabajoExterno.EnCola or EstadoTrabajoExterno.Ejecutando)
            return Task.FromResult(new EstadoImpuesto(estado, null, null, null));

        var cuerpo = Simulacion.FixturesImpuesto.Texto(archivo);
        return Task.FromResult(new EstadoImpuesto(estado, cuerpo, null, null));
    }

    public Task<ArchivoImpuesto> DescargarAsync(string archivoId, string apiKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = archivoId;
        _ = apiKey;
        return Task.FromResult(new ArchivoImpuesto(
            Simulacion.FixturesImpuesto.Bytes("liquidacion-sintetica.pdf"),
            "application/pdf",
            null,
            false));
    }

    private static char Marca(string idExterno)
    {
        // sim-{marca}-{guid}
        var partes = idExterno.Split('-', 3, StringSplitOptions.None);
        return partes.Length >= 2 && partes[1].Length == 1 ? partes[1][0] : '2';
    }
}
