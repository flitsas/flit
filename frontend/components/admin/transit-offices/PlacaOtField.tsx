"use client";

import {
  FORMATO_PLACA_POR_GRUPO,
  estadoPlacaOt,
  normalizarPlacaOt,
  type GrupoPlaca,
} from "@/lib/tramites/plate-format";
import { OT_INPUT_CLS } from "./ot-form-styles";

const SUFIJO_AYUDA = " Se validará que no esté registrada y quedará en el inventario de la compañía.";

/** Texto bajo el input cuando el vehículo es carro (registro diferido 2026-11-09, HU 2). */
export const AYUDA_PLACA_CARRO = `Formato ABC123.${SUFIJO_AYUDA}`;
/** Aviso ámbar cuando la placa de un carro no cumple ABC123. */
export const AVISO_PLACA_CARRO = "La placa debe tener 3 letras y 3 números, por ejemplo ABC123.";

/** HU6 (registro diferido 2026-11-09) — texto de formato bajo el input, por grupo. */
export const AYUDA_PLACA_POR_GRUPO: Readonly<Record<GrupoPlaca, string>> = {
  CARRO: AYUDA_PLACA_CARRO,
  MOTO: `Formato permitido: ABC12D (3 letras, 2 números y 1 letra) o ABC12 (3 letras y 2 números).${SUFIJO_AYUDA}`,
  REMOLQUE: `Formato permitido: R (remolque) o S (semirremolque) seguida de 5 o 6 números, por ejemplo R12345 o S54321.${SUFIJO_AYUDA}`,
  MAQUINARIA: `Formato permitido: serie identificatoria del Registro Especial de Maquinaria, 2 letras y 6 números, por ejemplo MC029554.${SUFIJO_AYUDA}`,
};

/** HU6 — aviso ámbar mientras la placa no cumple el formato del grupo. */
export const AVISO_PLACA_POR_GRUPO: Readonly<Record<GrupoPlaca, string>> = {
  CARRO: AVISO_PLACA_CARRO,
  MOTO: "La placa debe tener 3 letras y 2 números, con o sin una letra al final, por ejemplo ABC12D o ABC12.",
  REMOLQUE: "La placa debe empezar por R o S seguida de 5 o 6 números, por ejemplo R12345 o S54321.",
  MAQUINARIA: "La placa debe tener 2 letras y 6 números, por ejemplo MC029554.",
};

const EJEMPLO_POR_GRUPO: Readonly<Record<GrupoPlaca, string>> = {
  CARRO: "ABC123",
  MOTO: "ABC12D",
  REMOLQUE: "R12345",
  MAQUINARIA: "MC029554",
};

/**
 * Campo de placa de los modales del OT «Asignar placa» y «Corregir placa». Con grupo conocido
 * (CARRO / MOTO / REMOLQUE / MAQUINARIA) normaliza mientras se escribe (mayúsculas, solo A-Z/0-9,
 * maxLength del grupo), muestra la ayuda de formato del grupo y avisa en ámbar (`role="alert"`,
 * `aria-invalid`, `aria-describedby`) mientras no cumpla. Sin grupo deja el input libre y muestra
 * `ayudaSinGrupo` tal cual (o nada, si no se pasa).
 *
 * `idPrefix` hace únicos los ids de ayuda y aviso por modal. El botón de confirmar se habilita con
 * `estadoPlacaOt(value, grupo).lista`, la misma función que usa este campo.
 */
export function PlacaOtField({
  idPrefix,
  label,
  value,
  onChange,
  grupo,
  ayudaSinGrupo,
  className = "",
}: {
  idPrefix: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  grupo: GrupoPlaca | null;
  ayudaSinGrupo?: string;
  className?: string;
}) {
  const { invalida } = estadoPlacaOt(value, grupo);
  const ayuda = grupo ? AYUDA_PLACA_POR_GRUPO[grupo] : ayudaSinGrupo;
  const ayudaId = `${idPrefix}-ayuda`;
  const avisoId = `${idPrefix}-aviso`;
  const describedBy = [ayuda ? ayudaId : null, invalida ? avisoId : null].filter(Boolean).join(" ");

  return (
    <label className={`block text-xs font-semibold ${className}`} style={{ color: "var(--color-flit-primary)" }}>
      {label}
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(normalizarPlacaOt(e.target.value, grupo))}
        placeholder={grupo ? EJEMPLO_POR_GRUPO[grupo] : "ABC123"}
        aria-label={label}
        maxLength={grupo ? FORMATO_PLACA_POR_GRUPO[grupo].maxLength : undefined}
        aria-invalid={invalida || undefined}
        aria-describedby={describedBy || undefined}
        className={`mt-1 uppercase ${OT_INPUT_CLS}`}
      />
      {ayuda && (
        <span id={ayudaId} className="mt-1 block text-[11px] font-normal opacity-70">
          {ayuda}
        </span>
      )}
      {invalida && grupo && (
        <span id={avisoId} role="alert" className="mt-1 block text-[11px] font-medium" style={{ color: "#B45309" }}>
          {AVISO_PLACA_POR_GRUPO[grupo]}
        </span>
      )}
    </label>
  );
}
