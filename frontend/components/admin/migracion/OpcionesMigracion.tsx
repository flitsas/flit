"use client";

import {
  DESCRIPCION_INSTANCIA,
  INSTANCIAS,
  type Instancia,
} from "@/lib/migracion/types";

/**
 * Las opciones que comparten la migración de uno y la masiva: qué instancias correr y si es
 * simulación. Un solo componente para que las dos pantallas no puedan ofrecer opciones distintas.
 *
 * Una selección VACÍA significa «las tres», que es exactamente lo que hace el host cuando no se le
 * manda el parámetro. Se dice con todas las letras en la interfaz en vez de dejar que quien opera
 * lo deduzca de que no hay ninguna casilla marcada.
 */
export function OpcionesMigracion({
  instancias,
  onInstancias,
  dryRun,
  onDryRun,
  deshabilitado = false,
}: {
  instancias: Instancia[];
  onInstancias: (valor: Instancia[]) => void;
  dryRun: boolean;
  onDryRun: (valor: boolean) => void;
  deshabilitado?: boolean;
}) {
  const alternar = (instancia: Instancia) => {
    onInstancias(
      instancias.includes(instancia)
        ? instancias.filter((i) => i !== instancia)
        : // Se reordena al orden canónico para que la interfaz muestre lo mismo que va a correr.
          INSTANCIAS.filter((i) => i === instancia || instancias.includes(i)),
    );
  };

  const todas = instancias.length === 0 || instancias.length === INSTANCIAS.length;

  return (
    <fieldset className="flex flex-col gap-3" disabled={deshabilitado}>
      <div>
        <legend className="text-xs font-semibold uppercase tracking-wide opacity-60">
          Qué migrar
        </legend>
        <p className="mt-0.5 text-xs opacity-70">
          {todas
            ? "Se correrán las tres instancias, en su orden obligatorio."
            : "Solo las marcadas. Recuerda que los adjuntos y los documentos necesitan que los datos ya existan."}
        </p>
      </div>

      <div className="flex flex-col gap-2">
        {INSTANCIAS.map((instancia) => (
          <label
            key={instancia}
            className="flex cursor-pointer items-start gap-2 rounded-lg border border-[#DFE5ED] p-2.5 dark:border-white/10"
          >
            <input
              type="checkbox"
              className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF]"
              checked={instancias.length === 0 || instancias.includes(instancia)}
              onChange={() => alternar(instancia)}
            />
            <span className="min-w-0">
              <span className="block text-sm font-medium capitalize">{instancia}</span>
              <span className="block text-xs opacity-70">
                {DESCRIPCION_INSTANCIA[instancia]}
              </span>
            </span>
          </label>
        ))}
      </div>

      <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-dashed border-[#DFE5ED] p-2.5 dark:border-white/10">
        <input
          type="checkbox"
          className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF]"
          checked={dryRun}
          onChange={(e) => onDryRun(e.target.checked)}
        />
        <span className="min-w-0">
          <span className="block text-sm font-medium">Simulación (dry run)</span>
          <span className="block text-xs opacity-70">
            Lee todo y no escribe nada. Sirve para ver qué haría antes de hacerlo.
          </span>
        </span>
      </label>
    </fieldset>
  );
}
