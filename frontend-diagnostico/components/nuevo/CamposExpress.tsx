"use client";

import { useId, useState, type Ref } from "react";
import { TIPOS_DOCUMENTO_LOCATARIO } from "@/lib/express/api";
import { limpiarIdentificador } from "@/lib/validation/fieldRules";
import { enmascararDocumento } from "@/components/comun/DocumentoDeclarado";
import { AYUDA_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS, INSIGNIA_CLS } from "@/components/parametrizacion/estilos";
import type { BorradorExpress } from "./borradores";

export interface ErroresLocatario {
  tipo?: string;
  documento?: string;
}

export interface CamposExpressProps {
  borrador: BorradorExpress;
  onCambio: (cambio: Partial<BorradorExpress>) => void;
  errorPlaca?: string | null;
  errores?: ErroresLocatario;
  disabled?: boolean;
  /** Formulario de Parámetros: Enter en estos campos lo envía aunque estén en la otra zona. */
  formId?: string;
  placaRef?: Ref<HTMLInputElement>;
}

/**
 * Vehículo del Express (HU #13755, reubicado en la zona Vehículos de la pantalla única, HU #14085): placa o VIN, que se
 * ve normalizado mientras se escribe (mayúsculas, solo letras y números, sin recortar), y el documento opcional del
 * locatario, rotulado «Declarado por el cliente» y enmascarado tras escribirlo. Placa y locatario van lado a lado en
 * pantallas anchas. El documento solo vive en memoria: no se guarda en el navegador ni se registra en la consola.
 */
export function CamposExpress({ borrador, onCambio, errorPlaca, errores = {}, disabled = false, formId, placaRef }: CamposExpressProps) {
  const id = useId();
  const [documentoEnfocado, setDocumentoEnfocado] = useState(false);
  const placaId = `${id}-placa`;
  const tipoId = `${id}-tipo-doc`;
  const docId = `${id}-doc`;
  const rotuloId = `${id}-rotulo`;
  const valorVisible = borrador.documento && !documentoEnfocado ? enmascararDocumento(borrador.documento) : borrador.documento;

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,15rem)_minmax(0,1fr)]">
      <div className="flex flex-col gap-1.5">
        <label htmlFor={placaId} className={ETIQUETA_CLS}>
          Placa o VIN{" "}
          <span aria-hidden="true" className="text-[var(--badge-danger-fg)]">
            *
          </span>
        </label>
        <input
          ref={placaRef}
          id={placaId}
          form={formId}
          type="text"
          value={borrador.placa}
          onChange={(e) => onCambio({ placa: limpiarIdentificador(e.target.value) })}
          required
          disabled={disabled}
          autoComplete="off"
          spellCheck={false}
          aria-invalid={errorPlaca ? true : undefined}
          aria-describedby={errorPlaca ? `${placaId}-error` : `${placaId}-ayuda`}
          placeholder="ABC123"
          className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} font-semibold uppercase tracking-widest`}
        />
        {errorPlaca ? (
          <p id={`${placaId}-error`} className={ERROR_CLS}>
            {errorPlaca}
          </p>
        ) : (
          <p id={`${placaId}-ayuda`} className={AYUDA_CLS}>
            Escribe la placa o el VIN de un vehículo para diagnosticarlo sin preparar un archivo.
          </p>
        )}
      </div>

      <section aria-labelledby={`${id}-locatario`} className="flex min-w-0 flex-col gap-1.5">
        <div className="flex flex-wrap items-center gap-2">
          <h3 id={`${id}-locatario`} className={ETIQUETA_CLS}>
            Locatario
          </h3>
          <span id={rotuloId} className={INSIGNIA_CLS}>
            Declarado por el cliente
          </span>
        </div>
        <div className="grid gap-3 sm:grid-cols-[minmax(0,11rem)_minmax(0,1fr)]">
          <div className="flex flex-col gap-1.5">
            <label htmlFor={tipoId} className={ETIQUETA_CLS}>
              Tipo de documento <span className="sr-only">del locatario</span>
            </label>
            <select
              id={tipoId}
              form={formId}
              value={borrador.tipoDocumento}
              onChange={(e) => onCambio({ tipoDocumento: e.target.value })}
              disabled={disabled}
              aria-invalid={errores.tipo ? true : undefined}
              aria-describedby={errores.tipo ? `${tipoId}-error` : undefined}
              className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
            >
              <option value="">Sin documento</option>
              {TIPOS_DOCUMENTO_LOCATARIO.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
            {errores.tipo && (
              <p id={`${tipoId}-error`} className={ERROR_CLS}>
                {errores.tipo}
              </p>
            )}
          </div>
          <div className="flex flex-col gap-1.5">
            <label htmlFor={docId} className={ETIQUETA_CLS}>
              Número de documento <span className="sr-only">del locatario</span> (opcional)
            </label>
            <input
              id={docId}
              form={formId}
              type="text"
              value={valorVisible}
              onFocus={() => setDocumentoEnfocado(true)}
              onBlur={() => setDocumentoEnfocado(false)}
              onChange={(e) => onCambio({ documento: e.target.value })}
              disabled={disabled}
              autoComplete="off"
              spellCheck={false}
              aria-invalid={errores.documento ? true : undefined}
              aria-describedby={errores.documento ? `${docId}-error ${rotuloId}` : rotuloId}
              className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} font-mono`}
            />
            {errores.documento && (
              <p id={`${docId}-error`} className={ERROR_CLS}>
                {errores.documento}
              </p>
            )}
          </div>
        </div>
        <p className={AYUDA_CLS}>Opcional. Se guarda cifrado y en pantalla solo se ven los 4 últimos caracteres.</p>
      </section>
    </div>
  );
}
