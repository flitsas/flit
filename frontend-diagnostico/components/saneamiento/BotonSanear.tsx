"use client";

import { useId, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { ACCION_FILA_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";
import { IconoDxBandera } from "@/components/dx/iconos/IconoDxBandera";
import { ApiError } from "@/lib/api";
import {
  desmarcarSaneamiento,
  marcarSaneamiento,
  mensajeErrorSaneamiento,
  type EstadoSaneamientoActivo,
  type SaneamientoFila,
} from "@/lib/saneamiento/api";
import { ROTULO_MOTIVO_NO_ELEGIBLE, deLaPlaca, etiquetaSaneamiento } from "@/lib/saneamiento/estados";

/** Fila sin saneamiento tras desmarcar: vuelve a ser elegible (el servidor lo confirma en el siguiente sondeo). */
export const SIN_SANEAMIENTO: SaneamientoFila = { estado: null, elegible: true, motivo_no_elegible: null };
/** Fila recién marcada: SOLICITADO y ya no elegible para volver a marcar (cuenta fuera de «Sanear (N)»). */
export const SANEAMIENTO_SOLICITADO: SaneamientoFila = { estado: "SOLICITADO", elegible: false, motivo_no_elegible: "YA_SOLICITADO" };

export interface BotonSanearProps {
  diagnosticoId: string;
  /** Vehículo de la fila: el identificador visible es la placa, o el VIN, o el identificador. */
  vehiculo: { id: string; placa?: string | null; vin?: string | null; identificador?: string | null };
  /**
   * Saneamiento de la fila (§6.1). `null` o ausente = la fila no trae el dato (backend previo): no se pinta nada (§2.6).
   * Sin saneamiento es `{ estado: null, elegible, motivo_no_elegible }`.
   */
  saneamiento: SaneamientoFila | null | undefined;
  /** Capacidad `sanear` de la sesión (`puedeSanear(capacidades)`). Sin ella solo hay indicador si hay saneamiento. */
  puedeSanear: boolean;
  /**
   * Cambio del saneamiento de la fila: se llama de inmediato al pulsar (optimista) y otra vez con el valor previo si la API
   * falla. Quien pinta la fila guarda `saneamiento` (el componente es controlado).
   */
  onCambio: (estado: EstadoSaneamientoActivo | null, saneamiento: SaneamientoFila) => void;
  /** «Ver saneamiento»: abre el panel PS-2 del vehículo; recibe el botón para devolverle el foco al cerrar. */
  onAbrirPanel: (disparador: HTMLElement) => void;
  /**
   * La API confirmó el cambio (PUT 200/201 → SOLICITADO; DELETE 204 o 404 SIN_SANEAMIENTO → null). Se llama después de la
   * respuesta, nunca en el optimista: el panel PS-2 recarga aquí su detalle sin correr contra la petición en vuelo.
   */
  onConfirmado?: (estado: EstadoSaneamientoActivo | null, saneamiento: SaneamientoFila) => void;
  /** El estado cambió en el servidor (409 YA_SOLICITADO o SANEAMIENTO_EN_GESTION): conviene volver a leer la fila. */
  onRefrescar?: () => void;
  /** Escribe el resultado en la región viva de la pantalla (§1.6). */
  onAnunciar?: (mensaje: string) => void;
  /** «fila» (por defecto): pastilla de las acciones de fila. «cabecera»: botón secundario (panel, cabeceras). */
  variante?: "fila" | "cabecera";
}

const FILA_CLS = `${ACCION_FILA_CLS} whitespace-nowrap`;
const ENCENDIDA_CLS = "bg-[var(--di-accent-soft)]";
const NO_ELEGIBLE_CLS = "cursor-not-allowed opacity-60";
/** En la fila, el glifo a 14 px como «Crear trámite»; en la cabecera, a su tamaño (16 px). */
const ICONO_CLS = { fila: "h-3.5 w-3.5", cabecera: undefined } as const;

/** Estado de la acción de saneamiento de una fila, sin presentación (lo comparten la bandera y el menú «Acciones»). */
export type AccionSanear =
  | { tipo: "ninguna"; identificador: string; error: string | null }
  | { tipo: "ver"; identificador: string; error: string | null; texto: string | undefined; nombre: string }
  | {
      tipo: "alternar";
      identificador: string;
      error: string | null;
      encendida: boolean;
      noElegible: boolean;
      motivo: string | undefined;
      enVuelo: boolean;
      alternar: () => Promise<void>;
    };

export type UseAccionSanearProps = Omit<BotonSanearProps, "variante" | "onAbrirPanel">;

/**
 * Lógica de la bandera (FE-2: expuesta como hook para el menú «Acciones» del tablero). Optimista con reversión, en vuelo
 * ignora los clics, 403 → /403, 409 YA_SOLICITADO o SANEAMIENTO_EN_GESTION → `onRefrescar`. Ver la doc de `BotonSanear`.
 */
export function useAccionSanear({
  diagnosticoId,
  vehiculo,
  saneamiento,
  puedeSanear,
  onCambio,
  onConfirmado,
  onRefrescar,
  onAnunciar,
}: UseAccionSanearProps): AccionSanear {
  const router = useRouter();
  const [enVuelo, setEnVuelo] = useState(false);
  const enVueloRef = useRef(false);
  const [error, setError] = useState<string | null>(null);

  const identificador = vehiculo.placa || vehiculo.vin || vehiculo.identificador || "";
  if (!saneamiento) return { tipo: "ninguna", identificador, error };
  const estado = saneamiento.estado;

  // Indicador: el saneamiento ya no se puede quitar desde aquí (o el usuario no tiene la capacidad).
  if (estado && (estado !== "SOLICITADO" || !puedeSanear)) {
    const texto = etiquetaSaneamiento(estado)?.texto;
    const nombre = texto ? `Ver saneamiento ${deLaPlaca(identificador)}: ${texto}` : `Ver saneamiento ${deLaPlaca(identificador)}`;
    return { tipo: "ver", identificador, error, texto, nombre };
  }

  if (!puedeSanear) return { tipo: "ninguna", identificador, error };

  const actual = saneamiento;
  const encendida = estado === "SOLICITADO";
  const motivo = !encendida && !actual.elegible && actual.motivo_no_elegible ? ROTULO_MOTIVO_NO_ELEGIBLE[actual.motivo_no_elegible] : undefined;
  const noElegible = !encendida && !actual.elegible;

  async function alternar() {
    if (enVueloRef.current || noElegible) return;
    const previo = actual;
    const siguiente = encendida ? SIN_SANEAMIENTO : SANEAMIENTO_SOLICITADO;
    setError(null);
    enVueloRef.current = true;
    setEnVuelo(true);
    onCambio(siguiente.estado, siguiente);
    try {
      if (encendida) await desmarcarSaneamiento(diagnosticoId, vehiculo.id);
      else await marcarSaneamiento(diagnosticoId, vehiculo.id);
      onAnunciar?.(
        encendida
          ? `Se quitó la solicitud de saneamiento de ${identificador || "el vehículo sin placa"}.`
          : `Saneamiento solicitado para ${identificador || "el vehículo sin placa"}.`,
      );
      onConfirmado?.(siguiente.estado, siguiente);
    } catch (e) {
      onCambio(previo.estado, previo);
      if (e instanceof ApiError && e.status === 403) {
        router.replace(`/403?code=${encodeURIComponent(e.code ?? "SIN_PERMISO")}`);
        return;
      }
      setError(mensajeErrorSaneamiento(e));
      if (e instanceof ApiError && e.status === 409 && (e.code === "YA_SOLICITADO" || e.code === "SANEAMIENTO_EN_GESTION")) onRefrescar?.();
    } finally {
      enVueloRef.current = false;
      setEnVuelo(false);
    }
  }

  return { tipo: "alternar", identificador, error, encendida, noElegible, motivo, enVuelo, alternar };
}

/**
 * Bandera de saneamiento de un vehículo (spec-ui §1.3, HU #14577 AC5–AC8). Patrón del «prioritario» de Trámites: un clic,
 * sin confirmación, optimista y con reversión si la API falla.
 * - Con `puedeSanear` y sin saneamiento: toggle apagado «Sanear» (PUT); en SOLICITADO, encendido «Solicitado» (DELETE). El
 *   nombre accesible no cambia («Solicitar saneamiento de la placa X»): el estado lo da `aria-pressed` (D-02).
 * - No elegible: `aria-disabled` (no `disabled`: sigue en el Tab) con el motivo por `aria-describedby` (D-03).
 * - EN_GESTION, SANEADO, NO_PROCEDE, o SOLICITADO sin la capacidad: «Ver saneamiento», que abre el panel.
 * - Sin la capacidad y sin saneamiento, o sin el dato: no se pinta.
 * En vuelo, `aria-busy` y los clics se ignoran (sin `disabled`, para no perder el foco). El error va en `role="alert"` bajo
 * el botón y se borra en el siguiente clic; el foco se queda en el botón. Un 403 lleva a /403.
 */
export function BotonSanear({ onAbrirPanel, variante = "fila", ...props }: BotonSanearProps) {
  const motivoId = `${useId()}-motivo`;
  const accion = useAccionSanear(props);
  const base = variante === "cabecera" ? BOTON_SECUNDARIO_CLS : FILA_CLS;

  const errorVisible = accion.error && (
    <p role="alert" className="text-xs font-semibold text-[color:var(--di-danger)]">
      {accion.error}
    </p>
  );
  const envolver = (boton: React.ReactNode) => (
    <div className="flex flex-col items-start gap-1">
      {boton}
      {errorVisible}
    </div>
  );

  if (accion.tipo === "ninguna") return null;

  if (accion.tipo === "ver") {
    return envolver(
      <button
        type="button"
        aria-haspopup="dialog"
        aria-label={accion.nombre}
        onClick={(e) => onAbrirPanel(e.currentTarget)}
        className={`${base} ${ENCENDIDA_CLS}`}
      >
        <IconoDxBandera tamano={16} encendida className={ICONO_CLS[variante]} />
        Ver saneamiento
      </button>,
    );
  }

  const { encendida, noElegible, motivo, enVuelo, alternar, identificador } = accion;
  return envolver(
    <>
      <button
        type="button"
        aria-label={`Solicitar saneamiento ${deLaPlaca(identificador)}`}
        aria-pressed={encendida}
        aria-busy={enVuelo || undefined}
        aria-disabled={noElegible || undefined}
        aria-describedby={motivo ? motivoId : undefined}
        title={encendida ? "Clic para quitar la solicitud" : motivo}
        onClick={() => void alternar()}
        className={`${base}${encendida ? ` ${ENCENDIDA_CLS}` : ""}${noElegible ? ` ${NO_ELEGIBLE_CLS}` : ""}`}
      >
        <IconoDxBandera tamano={16} encendida={encendida} className={ICONO_CLS[variante]} />
        {encendida ? "Solicitado" : "Sanear"}
      </button>
      {motivo && (
        <span id={motivoId} hidden>
          {motivo}
        </span>
      )}
    </>,
  );
}
