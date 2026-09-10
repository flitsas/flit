import { describe, it, expect } from 'vitest';

import { createPendingChangesTracker } from '@/components/operacion/pending-changes';

/**
 * Bug #11614 (observación de code review) — CARRERA entre la rehidratación y la captura.
 *
 * Los formularios embebidos limpiaban su marca de "hay cambios sin guardar" dentro de una promesa
 * (la carga inicial o el PUT de guardado). Si esa promesa resolvía DESPUÉS de que el gestor
 * empezara a escribir, la limpieza tardía borraba la marca y el wizard volvía a cambiar de paso sin
 * guardar: exactamente el modo de fallo que el bug corrige.
 *
 * Uso de ejemplo:
 *   const pending = createPendingChangesTracker();
 *   const settle = pending.beginSettle();   // ANTES del await
 *   await cargar();
 *   settle();                               // limpia solo si nadie editó mientras tanto
 */
describe('createPendingChangesTracker — Bug #11614', () => {
  it('nace limpio y una edición del gestor lo marca pendiente', () => {
    const pending = createPendingChangesTracker();
    expect(pending.hasPendingChanges()).toBe(false);
    pending.markDirty();
    expect(pending.hasPendingChanges()).toBe(true);
  });

  it('la carga que resuelve sin captura de por medio sí limpia la marca', () => {
    const pending = createPendingChangesTracker();
    const settle = pending.beginSettle();
    settle();
    expect(pending.hasPendingChanges()).toBe(false);

    // Y lo que se capture DESPUÉS de esa liquidación sigue contando como pendiente.
    pending.markDirty();
    expect(pending.hasPendingChanges()).toBe(true);
  });

  it('CARRERA — la carga que resuelve DESPUÉS de la captura NO borra la marca', () => {
    const pending = createPendingChangesTracker();
    const settle = pending.beginSettle(); // arranca la carga
    pending.markDirty(); // el gestor escribe mientras la carga viaja
    settle(); // la carga aterriza tarde

    expect(pending.hasPendingChanges()).toBe(true);
  });

  it('CARRERA — lo tecleado mientras el guardado viaja sigue pendiente (el payload ya iba congelado)', () => {
    const pending = createPendingChangesTracker();
    pending.markDirty();
    const settle = pending.beginSettle(); // arranca el PUT con el payload de ese momento
    pending.markDirty(); // el gestor sigue escribiendo
    settle(); // el PUT responde OK

    expect(pending.hasPendingChanges()).toBe(true);
  });

  it('no confunde dos ediciones que dejan el mismo valor con "no hubo edición"', () => {
    const pending = createPendingChangesTracker();
    const settle = pending.beginSettle();
    pending.markDirty(); // escribe "5"
    pending.markDirty(); // borra y vuelve a escribir "5"
    settle();

    // Un snapshot por igualdad de valores diría "no cambió nada"; el contador de ediciones no.
    expect(pending.hasPendingChanges()).toBe(true);
  });

  it('liquidaciones solapadas: la más vieja no puede limpiar lo capturado tras la más nueva', () => {
    const pending = createPendingChangesTracker();
    const settleCarga = pending.beginSettle();
    const settleGuardado = pending.beginSettle();
    pending.markDirty();
    settleGuardado();
    settleCarga();

    expect(pending.hasPendingChanges()).toBe(true);
  });

  it('cada formulario lleva su propio contador (instancias independientes)', () => {
    const a = createPendingChangesTracker();
    const b = createPendingChangesTracker();
    a.markDirty();
    expect(a.hasPendingChanges()).toBe(true);
    expect(b.hasPendingChanges()).toBe(false);
  });
});
