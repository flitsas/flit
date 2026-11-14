"use client";

import { useId, useState } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { CAMPO_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS } from "@/components/configuracion/estilos";
import { ListaAlertamientosAjuste } from "@/components/backoffice/ListaAlertamientosAjuste";
import { largoTexto, MOTIVO_MAX, validarMotivoAjuste, type FilaAlertamiento } from "@/lib/backoffice/ajusteDictamen";
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
  /** D9 (B7): motivo obligatorio del ajuste. Sin `onMotivo` el campo no se pinta (consumidor previo). */
  motivo?: string;
  onMotivo?: (motivo: string) => void;
  /** Error del API asignado al campo (422 con `campo`, p. ej. el filtro de dígitos P3). */
  errorMotivo?: string | null;
  /** AC45 (B7): alertamientos del ajuste. Sin `onAlertamientos` la lista no se pinta. */
  alertamientos?: FilaAlertamiento[];
  onAlertamientos?: (lista: FilaAlertamiento[]) => void;
  /** Errores del API por campo de alertamiento (`alertamientos[i].texto`). */
  erroresAlertamientos?: Readonly<Record<string, string>>;
}

/**
 * Campos del override del veredicto (corte 7): solo los pinta el `PanelDictamen` en el backoffice y con la capacidad
 * `corregirDictamen`. El panel conserva el estado y el envío; aquí solo van los veredictos y la confirmación explícita.
 * HU #14562 · m-11: el motivo no nace inválido; el error local aparece al salir del campo o en cuanto el usuario escribe
 * (el error del API se muestra siempre). El contador cuenta puntos de código, como el BE (observación 2 del code review).
 * La confirmación sigue siendo una casilla y no el modal «¿Cambiar a Viable?» de la spec F-09: queda a decisión del PO.
 */
export function CamposOverrideDictamen({ elegido, onElegir, confirmado, onConfirmar, aviso, motivo = "", onMotivo, errorMotivo = null, alertamientos = [], onAlertamientos, erroresAlertamientos }: CamposOverrideDictamenProps) {
  const id = useId();
  const [tocado, setTocado] = useState(false);
  const errorLocal = tocado || motivo !== "" ? validarMotivoAjuste(motivo) : null;
  const error = errorMotivo ?? errorLocal;
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
      {onAlertamientos && (
        <div className="flex flex-col gap-1">
          <p className={ETIQUETA_CLS}>Alertamientos</p>
          <ListaAlertamientosAjuste alertamientos={alertamientos} onCambiar={onAlertamientos} errores={erroresAlertamientos} />
        </div>
      )}
      {onMotivo && (
        <div className="flex flex-col gap-1">
          <label htmlFor={`${id}-motivo`} className={ETIQUETA_CLS}>
            Motivo del ajuste
          </label>
          <p id={`${id}-aviso`} className="text-xs text-muted-foreground">
            No escribas documentos ni nombres
          </p>
          <textarea
            id={`${id}-motivo`}
            required
            maxLength={MOTIVO_MAX}
            rows={3}
            value={motivo}
            onChange={(e) => onMotivo(e.target.value)}
            onBlur={() => setTocado(true)}
            aria-invalid={error ? true : undefined}
            aria-describedby={`${id}-aviso ${id}-error`}
            className={CAMPO_CLS.replace("h-11", "min-h-24 py-2")}
          />
          <div className="flex justify-between gap-2 text-xs">
            <span id={`${id}-error`} className="text-[var(--badge-danger-fg)]">
              {error ?? ""}
            </span>
            <span className="tabular-nums text-muted-foreground">{`${largoTexto(motivo.trim())} / ${MOTIVO_MAX}`}</span>
          </div>
        </div>
      )}
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
