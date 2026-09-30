using System.Linq.Expressions;
using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Domain.Abstractions;

/// <summary>Resultado de dar de alta un pre-trámite con control de duplicado (Bug #13109, punto 7).</summary>
public enum PreTramiteAlta
{
    /// <summary>La fila se insertó.</summary>
    Registrado = 0,

    /// <summary>Ya había otro pre-trámite EN PROCESO del mismo tenant con la misma clave; no se insertó.</summary>
    DuplicadoActivo = 1,
}

/// <summary>
/// Clave de deduplicación de un pre-trámite ICT: VIN en matrícula (1, 2), placa en traspaso (3, 4) y
/// placa + tipo en los demás. Es la misma clave para el duplicado dentro del lote y para el duplicado
/// contra lo ya guardado, para que las dos barreras no diverjan.
/// </summary>
public static class PreTramiteClave
{
    /// <summary>Estados internos que bloquean un nuevo register: 1 (registrado) y 2 (en validación).</summary>
    public const short EstadoRegistrado = 1;

    /// <summary>Estado interno 2: en validación (negocio o externa).</summary>
    public const short EstadoEnValidacion = 2;

    /// <summary>Texto de la clave. <paramref name="plate"/> y <paramref name="vin"/> se normalizan (trim + mayúsculas).</summary>
    public static string Texto(int transactionType, string? plate, string? vin)
    {
        var p = Normalizar(plate);
        var v = Normalizar(vin);
        return transactionType switch
        {
            1 or 2 => $"vin:{v}",
            3 or 4 => $"plate:{p}",
            _ => $"plate+type:{p}:{transactionType}",
        };
    }

    /// <summary>
    /// Predicado de «otro pre-trámite en proceso con la misma clave» para <paramref name="nuevo"/>. Solo
    /// cuentan los estados internos 1 y 2 sin borrado lógico: novedad (4), borrador creado (5) y anulado en
    /// ICT (6) no bloquean. Se expresa como árbol de expresión para que EF lo traduzca a SQL y los tests
    /// lo evalúen en memoria con la misma regla.
    /// </summary>
    public static Expression<Func<ExternalIntegrationMaster, bool>> EnProcesoConLaMismaClave(
        ExternalIntegrationMaster nuevo)
    {
        ArgumentNullException.ThrowIfNull(nuevo);

        // Variables locales: EF las envía como parámetros (no se concatena nada en el SQL).
        var tenant = nuevo.TenantId;
        var id = nuevo.Id;
        var tipo = nuevo.TransactionType;
        var plate = Normalizar(nuevo.Plate);
        var vin = Normalizar(nuevo.Vin);

        return tipo switch
        {
            1 or 2 => m => m.TenantId == tenant && m.Id != id && m.DeletedAt == null
                && (m.ProcessStatusId == EstadoRegistrado || m.ProcessStatusId == EstadoEnValidacion)
                && (m.TransactionType == 1 || m.TransactionType == 2)
                && m.Vin == vin,
            3 or 4 => m => m.TenantId == tenant && m.Id != id && m.DeletedAt == null
                && (m.ProcessStatusId == EstadoRegistrado || m.ProcessStatusId == EstadoEnValidacion)
                && (m.TransactionType == 3 || m.TransactionType == 4)
                && m.Plate == plate,
            _ => m => m.TenantId == tenant && m.Id != id && m.DeletedAt == null
                && (m.ProcessStatusId == EstadoRegistrado || m.ProcessStatusId == EstadoEnValidacion)
                && m.TransactionType == tipo
                && m.Plate == plate,
        };
    }

    private static string Normalizar(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
}
