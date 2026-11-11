"use client";

import { InlineAlert } from "@flit/ui/InlineAlert";
import { MARCA_CLS, OPCION_CLS } from "@/components/configuracion/estilos";
import type { Dictamen } from "@/lib/dictamen/api";

const VEREDICTOS = [
  { valor: "VIABLE", nombre: "Viable" },
  { valor: "BLOQUEADO", nombre: "Bloqueado" },
] as const;

export interface CamposOverrideDictamenProps {
  elegido: Dictamen["veredicto"] | null;
  onElegir: (veredicto: Dictamen["veredicto"]) => void;
  confirmado: boolean;
  onConfirmar: (confirmado: boolean) => void;
  /** Error del último reemplazo, o null. */
  aviso: string | null;
}

/**
 * Campos del override del veredicto (corte 7): solo los pinta el `PanelDictamen` en el backoffice y con la capacidad
 * `corregirDictamen`. El panel conserva el estado y el envío; aquí solo van los veredictos y la confirmación explícita.
 */
export function CamposOverrideDictamen({ elegido, onElegir, confirmado, onConfirmar, aviso }: CamposOverrideDictamenProps) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="text-sm font-semibold text-flit-primary">Nuevo veredicto</legend>
      {VEREDICTOS.map((opcion) => (
        <label key={opcion.valor} className={OPCION_CLS}>
          <input
            type="radio"
            name="veredicto-dictamen"
            value={opcion.valor}
            className={MARCA_CLS}
            checked={elegido === opcion.valor}
            onChange={() => onElegir(opcion.valor)}
          />
          {opcion.nombre}
        </label>
      ))}
      <label className={OPCION_CLS}>
        <input type="checkbox" className={MARCA_CLS} checked={confirmado} onChange={(e) => onConfirmar(e.target.checked)} />
        Confirmo el cambio de veredicto
      </label>
      {aviso && (
        <InlineAlert tone="error" title="No se reemplazó el veredicto">
          {aviso}
        </InlineAlert>
      )}
    </fieldset>
  );
}
