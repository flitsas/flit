"use client";

import { Ban } from "lucide-react";
import {
  REGIMEN_ESPECIAL_CONDICIONES,
  REGIMEN_NINGUNA_APLICA,
  condicionPorCodigo,
  type RegimenSeleccion,
} from "./transferencia-regimen";

const OPCION_CLASS =
  "flex items-start gap-2 rounded-lg px-2 py-1.5 text-[11px] focus-within:ring-2 focus-within:ring-[#557EFF]";

export interface RegimenAplicableControlProps {
  seleccion: RegimenSeleccion;
  onChange: (seleccion: RegimenSeleccion) => void;
}

/**
 * Control «Régimen aplicable a la operación» — CF-24 / `VB-07`.
 *
 * <p>Va <b>antes</b> del selector de escenario A/B/C porque el anexo normativo §4.0 lo pone antes:
 * es un gate de exclusión, no un campo más del formulario. Si la operación encuadra en cualquiera
 * de las once condiciones especiales de traspaso de los arts. 5.3.2.3 a 5.3.2.13, el trámite exige
 * soportes que este módulo no captura ni acredita y el documento no debe existir.</p>
 *
 * <p><b>Las once, una por una, con su artículo.</b> No se resumen en «trámites especiales» ni se
 * agrupan: el usuario tiene que poder reconocer su caso, y el mensaje de bloqueo cita el artículo
 * concreto para que sepa por qué vía debe adelantarlo.</p>
 *
 * <p><b>El art. 5.3.2.14 no aparece</b> (expedición de la nueva licencia de tránsito): es el paso
 * final común a todo traspaso, no una condición especial. Ofrecerlo bloquearía trámites ordinarios.</p>
 *
 * <p><b>Accesibilidad (CF-22):</b> es un `radiogroup` con leyenda, cada opción con su `label`; el
 * bloqueo se comunica con un `role="alert"` que lleva icono y texto —no solo color— y el botón de
 * generar queda deshabilitado con una explicación enlazada por `aria-describedby`.</p>
 *
 * <p><b>Este control no aparece en el flujo de Certificado RUES:</b> allí no hay traspaso que
 * clasificar. Vive en el formulario de transferencia y en ningún otro.</p>
 */
export function RegimenAplicableControl({ seleccion, onChange }: RegimenAplicableControlProps) {
  const condicionDeclarada = condicionPorCodigo(seleccion);

  return (
    <fieldset
      className="rounded-2xl border p-4"
      data-testid="transferencia-regimen-aplicable"
      aria-describedby="tf-regimen-ayuda"
    >
      <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">
        Régimen aplicable a la operación
      </legend>

      <p id="tf-regimen-ayuda" className="mb-2 text-[11px] opacity-80">
        Antes de elegir el escenario, declara si la operación corresponde a alguno de los traspasos
        especiales de los artículos 5.3.2.3 a 5.3.2.13 de la Resolución 20233040017145 de 2023. Esos
        trámites exigen soportes que este módulo no produce ni acredita.
      </p>

      <div role="radiogroup" aria-label="Régimen aplicable a la operación" className="flex flex-col">
        <label htmlFor="tf-regimen-ninguna" className={OPCION_CLASS}>
          <input
            id="tf-regimen-ninguna"
            type="radio"
            name="tf-regimen"
            className="mt-0.5"
            value={REGIMEN_NINGUNA_APLICA}
            checked={seleccion === REGIMEN_NINGUNA_APLICA}
            onChange={() => onChange(REGIMEN_NINGUNA_APLICA)}
          />
          <span className="font-semibold">Ninguna de las anteriores aplica</span>
        </label>

        {REGIMEN_ESPECIAL_CONDICIONES.map((condicion) => (
          <label key={condicion.codigo} htmlFor={`tf-regimen-${condicion.codigo}`} className={OPCION_CLASS}>
            <input
              id={`tf-regimen-${condicion.codigo}`}
              type="radio"
              name="tf-regimen"
              className="mt-0.5"
              value={condicion.codigo}
              checked={seleccion === condicion.codigo}
              onChange={() => onChange(condicion.codigo)}
            />
            <span>
              {condicion.titulo} <span className="opacity-70">({condicion.articulo})</span>
            </span>
          </label>
        ))}
      </div>

      {condicionDeclarada ? (
        <div
          role="alert"
          data-testid="transferencia-regimen-bloqueo"
          className="mt-3 flex gap-2 rounded-xl border border-red-300 bg-red-50 p-3 text-[11px] text-red-800"
        >
          {/* El icono acompaña al texto: el bloqueo no puede comunicarse solo por color (CF-22). */}
          <Ban className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <div>
            <p className="font-semibold">
              No se puede generar el documento: traspaso especial del {condicionDeclarada.articulo}
            </p>
            <p className="mt-1">
              {condicionDeclarada.titulo}. Ese trámite exige requisitos y soportes adicionales que
              este módulo no produce ni acredita ({condicionDeclarada.soporte}) Adelanta el trámite
              por la vía especial de ese artículo, con los soportes propios de la norma.
            </p>
          </div>
        </div>
      ) : null}
    </fieldset>
  );
}
