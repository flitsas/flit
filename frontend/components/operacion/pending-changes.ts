/**
 * Bug #11614 — señal de "captura del gestor sin persistir" de los formularios embebidos en el
 * wizard (`ActorsForm`, `CommercialForm`, `PrendaForm`), que la shell consulta vía
 * `WizardStepFormHandle.hasPendingChanges` antes de cambiar de paso.
 *
 * Por qué no basta un `boolean`: la marca se limpia cuando lo que hay en pantalla ES lo persistido,
 * y eso se sabe al RESOLVERSE una promesa (la carga inicial del formulario, el seed, el PUT de
 * guardado). Entre que esa promesa arranca y resuelve, el gestor puede haber escrito. Un booleano
 * no distingue "limpio porque acabo de rehidratar" de "sucio porque el gestor escribió mientras
 * cargaba", así que la limpieza tardía borraba la marca y devolvía exactamente el modo de fallo que
 * este bug corrige: navegar, no guardar, perder lo capturado.
 *
 * La solución es un contador monótono de ediciones. Antes del `await` se llama `beginSettle()`, que
 * fotografía el contador; el callback que devuelve limpia la marca SOLO si nadie editó desde
 * entonces. Se eligió el contador y no una comparación contra un snapshot de lo cargado porque:
 *  - no exige igualdad profunda ni conocer la forma de los datos de cada formulario (tres formas
 *    distintas, una con actores anidados y representante legal embebido);
 *  - responde a la pregunta correcta —"¿editó el gestor mientras esto viajaba?"— y no a una
 *    aproximación ("¿el estado actual difiere de lo cargado?"), que daría falso negativo cuando el
 *    gestor teclea y vuelve a dejar el mismo valor, y falso positivo con cualquier normalización;
 *  - vale igual para la carga y para el guardado: durante un PUT el payload ya está congelado, así
 *    que lo que se teclee mientras viaja sigue pendiente y debe seguir marcado.
 */
import { useState } from 'react';

export interface PendingChangesTracker {
  /** Edición del gestor: hay captura sin persistir. */
  markDirty: () => void;
  /** ¿Queda captura del gestor sin persistir? Lo que la shell consulta antes de navegar. */
  hasPendingChanges: () => boolean;
  /**
   * Llamar ANTES del `await` (carga, seed o guardado). El callback devuelto limpia la marca solo si
   * el gestor no editó mientras la promesa estaba en vuelo; si editó, la marca sobrevive.
   */
  beginSettle: () => () => void;
}

export function createPendingChangesTracker(): PendingChangesTracker {
  let dirty = false;
  // Monótono: cada edición del gestor lo incrementa. Nunca se reinicia, así que dos ediciones que
  // dejan el mismo valor siguen siendo dos eventos distintos.
  let editSeq = 0;
  return {
    markDirty() {
      dirty = true;
      editSeq += 1;
    },
    hasPendingChanges() {
      return dirty;
    },
    beginSettle() {
      const seqAlIniciar = editSeq;
      return () => {
        if (editSeq === seqAlIniciar) dirty = false;
      };
    },
  };
}

/**
 * Instancia estable por montaje del formulario. Se crea con el inicializador perezoso de `useState`
 * (una sola vez, sin re-render: nunca se llama al setter) porque el tracker no pinta nada — la
 * única lectura la hace la shell del wizard de forma imperativa, vía `hasPendingChanges`.
 */
export function usePendingChanges(): PendingChangesTracker {
  const [tracker] = useState(createPendingChangesTracker);
  return tracker;
}
