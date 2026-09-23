namespace Flit.Infrastructure.Persistence;

/// <summary>
/// HU #12797 (Épica #12760, F2) — acciones que deben esperar al FIN real de una transacción ambiente
/// gestionada (la de <c>OtClientProcedureRepository.ExecuteIn*TenantScopeAsync</c>): los borrados de
/// binarios del reemplazo seguro del consolidado (solo si confirma) y la bitácora de fallos (siempre,
/// fuera de la transacción, para que un rollback no se la lleve).
///
/// <para><b>Por qué.</b> Dentro de esa transacción el <c>SaveChanges</c> del caso de uso no confirma
/// nada: borrar el binario anterior justo después dejaba la BD apuntando a un objeto ya borrado si el
/// commit fallaba; y el INSERT de la bitácora por la misma conexión se perdía con el rollback.</para>
///
/// <para><b>Solo la transacción gestionada.</b> <see cref="TryDiferir"/> devuelve <c>false</c> si no hay
/// transacción o si la actual no es la que abrió <see cref="Abrir"/> (otro dueño que nunca llamaría a
/// <see cref="CerrarAsync"/>): el llamador actúa de inmediato, como antes. Así ninguna acción queda
/// colgada ni se ejecuta tras el commit de OTRA transacción.</para>
///
/// <para>Vive en el <see cref="FlitDbContext"/> (scoped): lo comparten los repositorios de la petición.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// acciones.Abrir(tx.TransactionId);
/// try { …; await tx.CommitAsync(ct); confirmada = true; }
/// finally { await acciones.CerrarAsync(tx.TransactionId, confirmada); }
/// </code>
/// </remarks>
internal sealed class AccionesPostTransaccion
{
    private readonly List<(Func<Task>? AlConfirmar, Func<Task>? AlRevertir)> _acciones = [];
    private Guid? _gestionada;

    /// <summary>Acciones en espera (diagnóstico y tests).</summary>
    internal int Pendientes => _acciones.Count;

    /// <summary>Marca <paramref name="transactionId"/> como la transacción gestionada y descarta restos.</summary>
    public void Abrir(Guid transactionId)
    {
        _acciones.Clear();
        _gestionada = transactionId;
    }

    /// <summary>
    /// Registra las acciones para el fin de la transacción gestionada en curso. <c>false</c> si no hay
    /// una (el llamador debe ejecutar él mismo lo que corresponda al «confirmado»).
    /// </summary>
    /// <param name="transaccionActual">
    /// <c>Database.CurrentTransaction?.TransactionId</c> del contexto: solo se difiere si es la gestionada.
    /// </param>
    public bool TryDiferir(Guid? transaccionActual, Func<Task>? alConfirmar, Func<Task>? alRevertir)
    {
        if (_gestionada is not { } id || transaccionActual != id)
            return false;

        _acciones.Add((alConfirmar, alRevertir));
        return true;
    }

    /// <summary>
    /// Ejecuta las acciones de <paramref name="transactionId"/> tras su fin: las de confirmación si
    /// <paramref name="confirmada"/>, las de reversión si no. Best-effort: una acción que lanza no impide
    /// las demás ni tumba la petición (la transacción ya terminó; las acciones registradas son limpieza
    /// o bitácora y registran su propio fallo).
    /// </summary>
    public async Task CerrarAsync(Guid transactionId, bool confirmada)
    {
        if (_gestionada != transactionId)
            return;

        var pendientes = _acciones.ToList();
        _acciones.Clear();
        _gestionada = null;

        foreach (var (alConfirmar, alRevertir) in pendientes)
        {
            var accion = confirmada ? alConfirmar : alRevertir;
            if (accion is null)
                continue;
            try
            {
                await accion().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Limpieza post-transacción: nunca tumba la petición ya resuelta.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Las acciones registradas (BorrarSinFallar, bitácora) ya registran su propio fallo.
            }
        }
    }
}
