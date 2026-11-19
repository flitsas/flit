using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// Ayudas del impuesto departamental en el detalle y los reportes (HU #13864): payloads con la forma que deja
/// <c>EjecutorCeldaImpuesto</c> (#13862) más campos que el payload nunca debería proyectar (un proveedor y una transacción
/// inventados, para probar la fuga R-20) y la celda en N/A con un motivo concreto. Valores ficticios.
/// </summary>
/// <example>
/// <code>
/// await Tablero.PayloadAsync(empresa, lote.Celda("TST481", Fuente.ImpuestoDepartamental), ImpuestoDePrueba.ConVigencias, ct);
/// await ImpuestoDePrueba.NoAplicaAsync(empresa, celda, MotivoNa.DepartamentoNoSoportado, ct);
/// </code>
/// </example>
internal static class ImpuestoDePrueba
{
    /// <summary>Proveedor inventado: no existe en ningún catálogo, así que si aparece en una salida es fuga del payload.</summary>
    public const string ProveedorFicticio = "PROVEEDOR-FICTICIO-ZQX";

    public const string TransaccionFicticia = "TX-IMPUESTO-ZQX-481";

    /// <summary>Texto del motivo DEPARTAMENTO_NO_SOPORTADO (el de <c>LectorContenidoReporte.TextoMotivoNa</c> y del frontend).</summary>
    public const string TextoDepartamentoNoSoportado = "El departamento del vehículo todavía no está soportado.";

    /// <summary>350.000 pendientes de 2026 y 2025 pagada (180.000); las vigencias vienen desordenadas a propósito.</summary>
    public const string ConVigencias =
        """{"monto_total":350000,"vigencias":[{"anio":2026,"valor":350000,"pagada":false},{"anio":2025,"valor":180000,"pagada":true}],"departamento_codigo":"05","proveedor":"PROVEEDOR-FICTICIO-ZQX","servicio_que_resolvio":"PROVEEDOR-FICTICIO-ZQX","id_transaccion":"TX-IMPUESTO-ZQX-481","origen":"API"}""";

    /// <summary>Sin vigencias registradas: total adeudado 0.</summary>
    public const string SinVigencias = """{"monto_total":0,"vigencias":[],"departamento_codigo":"05","proveedor":"PROVEEDOR-FICTICIO-ZQX"}""";

    /// <summary>Deja la celda en N/A con <paramref name="motivo"/> (sin payload, origen ni fecha del dato).</summary>
    public static async Task NoAplicaAsync(EmpresaDePrueba empresa, CeldaConsulta celda, MotivoNa motivo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        ArgumentNullException.ThrowIfNull(celda);
        await using var db = empresa.Db();
        var guardada = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == celda.Id, ct);
        guardada.Estado = EstadoCelda.Na;
        guardada.MotivoNa = motivo;
        guardada.Payload = null;
        guardada.Origen = null;
        guardada.UsuarioManualId = null;
        guardada.TipoError = null;
        guardada.DatoObtenidoEn = null;
        await db.SaveChangesAsync(ct);
    }
}
