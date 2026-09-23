'use client';

import { useSyncExternalStore } from 'react';
import { ChevronDown } from 'lucide-react';

/**
 * Epic #12686 — acordeón «Búsqueda rápida» debajo de la tira de estados. Presentacional: cada
 * pantalla (listado del gestor, bandeja del OT) decide qué atajos hay y qué filtra cada uno. Los
 * atajos no llevan conteo. Clic en un atajo lo aplica; segundo clic lo quita.
 *
 * Recuerda si quedó abierto o cerrado en este navegador; si el almacenamiento no está disponible
 * (ventana privada, datos bloqueados) simplemente abre desplegado.
 */
export interface AtajoItem {
  key: string;
  label: string;
  hint: string;
}

interface Props {
  items: readonly AtajoItem[];
  /** Atajo activo; vacío = ninguno. */
  selected: string;
  onSelect: (key: string) => void;
  /** Clave de almacenamiento del estado abierto/cerrado, distinta por pantalla. */
  storageKey: string;
  disabled?: boolean;
  /** Fondo en modo oscuro: cada pantalla conserva el de su superficie. */
  darkBgClassName?: string;
}

function leerAbierto(storageKey: string): boolean {
  try {
    return window.localStorage.getItem(storageKey) !== 'cerrado';
  } catch {
    return true;
  }
}

// Estado en memoria para cuando no hay almacenamiento: el acordeón sigue abriendo y cerrando.
const enMemoria = new Map<string, boolean>();
const suscriptores = new Set<() => void>();

function guardarAbierto(storageKey: string, abierto: boolean) {
  enMemoria.set(storageKey, abierto);
  try {
    window.localStorage.setItem(storageKey, abierto ? 'abierto' : 'cerrado');
  } catch {
    /* sin almacenamiento: vale el estado en memoria, solo no se recuerda entre visitas */
  }
  suscriptores.forEach((avisar) => avisar());
}

function suscribir(avisar: () => void) {
  suscriptores.add(avisar);
  return () => {
    suscriptores.delete(avisar);
  };
}

export function BusquedaRapidaAcordeon({
  items,
  selected,
  onSelect,
  storageKey,
  disabled = false,
  darkBgClassName = 'dark:bg-[#162744]',
}: Props) {
  // En el servidor siempre abierto; en el navegador, lo que el usuario dejó. Así el HTML del
  // servidor y el primer render del cliente coinciden y no hay desajuste de hidratación.
  const abierto = useSyncExternalStore(
    suscribir,
    () => enMemoria.get(storageKey) ?? leerAbierto(storageKey),
    () => true,
  );
  const panelId = `${storageKey}-panel`;

  const alternar = () => guardarAbierto(storageKey, !abierto);

  return (
    <section className={`rounded-2xl border border-[#DFE5ED] bg-white dark:border-white/10 ${darkBgClassName}`}>
      <h2 className="m-0">
        <button
          type="button"
          aria-expanded={abierto}
          aria-controls={panelId}
          onClick={alternar}
          className="flex w-full items-center justify-between rounded-2xl px-4 py-2.5 text-left text-xs font-semibold text-[#162744] transition hover:bg-[#557EFF]/[0.04] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF] dark:text-white"
        >
          Búsqueda rápida
          <ChevronDown
            className={`h-4 w-4 opacity-60 transition-transform motion-reduce:transition-none ${abierto ? 'rotate-180' : ''}`}
            aria-hidden="true"
          />
        </button>
      </h2>
      <div id={panelId} hidden={!abierto} className="px-4 pb-3">
        <div role="group" aria-label="Atajos de búsqueda rápida" className="flex flex-wrap gap-2">
          {items.map((item) => {
            const activo = selected === item.key;
            return (
              <button
                key={item.key}
                type="button"
                aria-pressed={activo}
                title={item.hint}
                disabled={disabled}
                onClick={() => onSelect(activo ? '' : item.key)}
                className={`rounded-lg border px-3 py-1.5 text-xs font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:cursor-not-allowed disabled:opacity-60 ${
                  activo
                    ? 'border-[#557EFF] bg-[#557EFF]/10 text-[#3A5FD9] dark:text-[#8FA9FF]'
                    : 'border-[#DFE5ED] text-[#162744] hover:border-[#557EFF] dark:border-white/15 dark:text-white/80'
                }`}
              >
                {item.label}
              </button>
            );
          })}
        </div>
      </div>
    </section>
  );
}
