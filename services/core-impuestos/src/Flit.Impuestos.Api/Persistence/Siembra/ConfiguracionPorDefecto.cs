using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Persistence.Siembra;

/// <summary>
/// Valores por defecto de la configuración de una compañía (F5a-CF4, CO-03, D-CONFIG): tope contractual de
/// <see cref="MaxRegistrosLoteContractual"/> placas por lote, todos los departamentos activos de <c>/fuentes</c> (null), sin tope
/// operativo (vale el contractual) y sin correos de aviso. Así el primer lote funciona sin configuración previa: sin fila se leen
/// estos valores sin escribir nada, y la primera escritura (operativa o contractual) parte de ellos con <see cref="SembrarAsync"/>.
/// No hay parámetro de contacto del propietario (CO-04, DA-9a).
/// </summary>
/// <example>
/// <code>
/// var fila = await db.Set&lt;ConfiguracionEmpresa&gt;().FirstOrDefaultAsync(c => c.TenantId == empresa, ct)
///     ?? ConfiguracionPorDefecto.Nueva(empresa, reloj.GetUtcNow());
/// </code>
/// </example>
public static class ConfiguracionPorDefecto
{
    /// <summary>Tope contractual sembrado (el <c>DEFAULT</c> de <c>max_registros_lote_contractual</c> y del contrato).</summary>
    public const int MaxRegistrosLoteContractual = 500;

    /// <summary>Límites del máximo por lote (CHECK de la base y contrato).</summary>
    public const int MaxRegistrosLoteMinimo = 1;

    public const int MaxRegistrosLoteMaximo = 5000;

    /// <summary>Una fila nueva con los valores por defecto (sin agregarla al contexto).</summary>
    public static ConfiguracionEmpresa Nueva(Guid empresa, DateTimeOffset ahora)
    {
        if (empresa == Guid.Empty)
            throw new ArgumentException("La configuración exige una compañía.", nameof(empresa));
        return new ConfiguracionEmpresa
        {
            TenantId = empresa,
            MaxRegistrosLoteContractual = MaxRegistrosLoteContractual,
            DepartamentosHabilitados = null,
            MaxRegistrosLoteOperativo = null,
            CorreosAviso = [],
            Version = 0,
            ActualizadoEn = ahora,
        };
    }

    /// <summary>
    /// La fila de la compañía con seguimiento, para actualizarla; si no existe, la siembra con los valores por defecto (agregada
    /// al contexto, sin guardar). La versión de una fila sembrada empieza en 0: quien la guarda la sube.
    /// </summary>
    public static async Task<ConfiguracionEmpresa> SembrarAsync(ImpuestosDb db, Guid empresa, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var fila = await db.Set<ConfiguracionEmpresa>().FirstOrDefaultAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false);
        if (fila is not null)
            return fila;
        fila = Nueva(empresa, ahora);
        db.Add(fila);
        return fila;
    }
}
