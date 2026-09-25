namespace Flit.Infrastructure.Persistence;

/// <summary>
/// HU #12797 (re-review #12760, N2) — cómo terminó la transacción gestionada.
/// </summary>
internal enum FinTransaccion
{
    /// <summary>El commit confirmó.</summary>
    Confirmada = 0,

    /// <summary>Falló ANTES del commit (o se revirtió explícitamente): la BD no cambió.</summary>
    Revertida = 1,

    /// <summary>
    /// La excepción salió de <c>CommitAsync</c>: el servidor pudo haber confirmado o no (p. ej. se cayó la
    /// conexión tras enviar el COMMIT). No se sabe qué versión referencia la BD.
    /// </summary>
    Desconocida = 2,
}

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
/// <para><b>Commit ambiguo</b> (<see cref="FinTransaccion.Desconocida"/>, re-review N2): no se ejecuta ni
/// la acción de confirmación (borrar el anterior) ni la de reversión (borrar el nuevo), porque cualquiera
/// de las dos podría borrar el binario que la BD sí referencia. Queda un huérfano recuperable, que el
/// llamador registra en el log (<see cref="CerrarAsync(Guid, FinTransaccion)"/> devuelve cuántas acciones
/// omitió). Las acciones de <see cref="TryDiferirSiempre"/> (bitácora) se ejecutan igual.</para>
///
/// <para><b>Solo la transacción gestionada.</b> <see cref="TryDiferir"/> devuelve <c>false</c> si no hay
/// transacción o si la actual no es la que abrió <see cref="Abrir"/> (otro dueño que nunca llamaría a
/// <see cref="CerrarAsync(Guid, FinTransaccion)"/>): el llamador actúa de inmediato, como antes. Así ninguna
/// acción queda colgada ni se ejecuta tras el commit de OTRA transacción.</para>
///
/// <para>Vive en el <see cref="FlitDbContext"/> (scoped): lo comparten los repositorios de la petición.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// acciones.Abrir(tx.TransactionId);
/// var fin = FinTransaccion.Revertida;
/// try { …; fin = FinTransaccion.Desconocida; await tx.CommitAsync(ct); fin = FinTransaccion.Confirmada; }
/// finally { await acciones.CerrarAsync(tx.TransactionId, fin); }
/// </code>
/// </remarks>
internal sealed class AccionesPostTransaccion
{
    private readonly List<(Func<Task>? AlConfirmar, Func<Task>? AlRevertir, bool Siempre)> _acciones = [];
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

        _acciones.Add((alConfirmar, alRevertir, false));
        return true;
    }

    /// <summary>
    /// Como <see cref="TryDiferir"/>, pero <paramref name="accion"/> se ejecuta tras el fin de la transacción
    /// en CUALQUIER desenlace, también el ambiguo (la bitácora de fallos: escribirla no puede dañar nada).
    /// </summary>
    public bool TryDiferirSiempre(Guid? transaccionActual, Func<Task> accion)
    {
        ArgumentNullException.ThrowIfNull(accion);
        if (_gestionada is not { } id || transaccionActual != id)
            return false;

        _acciones.Add((accion, accion, true));
        return true;
    }

    /// <summary>Compatibilidad: <c>true</c> = <see cref="FinTransaccion.Confirmada"/>, <c>false</c> = revertida.</summary>
    public Task<int> CerrarAsync(Guid transactionId, bool confirmada) =>
        CerrarAsync(transactionId, confirmada ? FinTransaccion.Confirmada : FinTransaccion.Revertida);

    /// <summary>
    /// Ejecuta las acciones de <paramref name="transactionId"/> tras su fin: las de confirmación si
    /// confirmó, las de reversión si se revirtió; con <see cref="FinTransaccion.Desconocida"/> solo las de
    /// <see cref="TryDiferirSiempre"/>. Best-effort: una acción que lanza no impide las demás ni tumba la
    /// petición (la transacción ya terminó; las acciones registradas son limpieza o bitácora y registran su
    /// propio fallo).
    /// </summary>
    /// <returns>Cuántas acciones de binarios se OMITIERON por commit ambiguo (0 en los demás casos).</returns>
    public async Task<int> CerrarAsync(Guid transactionId, FinTransaccion fin)
    {
        if (_gestionada != transactionId)
            return 0;

        var pendientes = _acciones.ToList();
        _acciones.Clear();
        _gestionada = null;

        var omitidas = 0;
        foreach (var (alConfirmar, alRevertir, siempre) in pendientes)
        {
            Func<Task>? accion;
            if (siempre)
            {
                accion = alConfirmar;
            }
            else if (fin == FinTransaccion.Desconocida)
            {
                if (alConfirmar is not null || alRevertir is not null)
                    omitidas++;
                continue;
            }
            else
            {
                accion = fin == FinTransaccion.Confirmada ? alConfirmar : alRevertir;
            }

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

        return omitidas;
    }
}
