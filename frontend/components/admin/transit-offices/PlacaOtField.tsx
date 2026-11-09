"use client";

import {
  PLACA_CARRO_MAX_LENGTH,
  estadoPlacaOt,
  normalizarPlacaCarro,
} from "@/lib/tramites/plate-format";
import { OT_INPUT_CLS } from "./ot-form-styles";

/** Texto bajo el input cuando el vehículo es carro (registro diferido 2026-11-09, HU 2). */
export const AYUDA_PLACA_CARRO =
  "Formato ABC123. Se validará que no esté registrada y quedará en el inventario de la compañía.";
/** Aviso ámbar cuando la placa de un carro no cumple ABC123. */
export const AVISO_PLACA_CARRO = "La placa debe tener 3 letras y 3 números, por ejemplo ABC123.";

/**
 * Campo de placa de los modales del OT «Asignar placa» y «Corregir placa». Para carros normaliza
 * mientras se escribe (mayúsculas, solo A-Z/0-9, máx. 6), cambia la ayuda a «Formato ABC123…» y
 * avisa en ámbar (`role="alert"`, `aria-invalid`, `aria-describedby`) mientras no cumpla. Para lo
 * demás deja el input libre y muestra `ayudaNoCarro` tal cual (o nada, si no se pasa).
 *
 * `idPrefix` hace únicos los ids de ayuda y aviso por modal. El botón de confirmar se habilita con
 * `estadoPlacaOt(value, esCarro).lista`, la misma función que usa este campo.
 */
export function PlacaOtField({
  idPrefix,
  label,
  value,
  onChange,
  esCarro,
  ayudaNoCarro,
  className = "",
}: {
  idPrefix: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  esCarro: boolean;
  ayudaNoCarro?: string;
  className?: string;
}) {
  const { invalida } = estadoPlacaOt(value, esCarro);
  const ayuda = esCarro ? AYUDA_PLACA_CARRO : ayudaNoCarro;
  const ayudaId = `${idPrefix}-ayuda`;
  const avisoId = `${idPrefix}-aviso`;
  const describedBy = [ayuda ? ayudaId : null, invalida ? avisoId : null].filter(Boolean).join(" ");

  return (
    <label className={`block text-xs font-semibold ${className}`} style={{ color: "var(--color-flit-primary)" }}>
      {label}
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(esCarro ? normalizarPlacaCarro(e.target.value) : e.target.value)}
        placeholder="ABC123"
        aria-label={label}
        maxLength={esCarro ? PLACA_CARRO_MAX_LENGTH : undefined}
        aria-invalid={invalida || undefined}
        aria-describedby={describedBy || undefined}
        className={`mt-1 uppercase ${OT_INPUT_CLS}`}
      />
      {ayuda && (
        <span id={ayudaId} className="mt-1 block text-[11px] font-normal opacity-70">
          {ayuda}
        </span>
      )}
      {invalida && (
        <span id={avisoId} role="alert" className="mt-1 block text-[11px] font-medium" style={{ color: "#B45309" }}>
          {AVISO_PLACA_CARRO}
        </span>
      )}
    </label>
  );
}
