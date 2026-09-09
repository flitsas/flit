"use client";

import { useId, useState } from "react";
import Link from "next/link";
import { FileCheck2, Search } from "lucide-react";
import {
  generateRuesDocument,
  previewRuesCompany,
} from "@/lib/api/admin-generacion-documental";
import { ApiError, ApiValidationError, type ValidationError } from "@/lib/api/types";
import type {
  StandaloneDocumentGenerateResult,
  StandaloneRuesPreviewResult,
} from "@/lib/api/types-generacion-documental";

/**
 * Captura del NIT, revisión previa y generación del Certificado RUES (CF-04).
 *
 * Este formulario cierra un hueco de la descomposición del Feature #12201: la HU #12203 es
 * `[BACKEND]` y solo especificó `POST /rues/preview` y `POST /rues/generate`; la HU #12202 dejó
 * el cascarón de la pestaña con sus cuatro estados. Ninguna de las diez HU pidió la captura del
 * NIT, así que la pestaña mostraba el encabezado y ningún control. La contraparte de
 * transferencia sí la tenía (`TransferenciaFormPanel`, HU #12207/#12208/#12209).
 *
 * Se monta como hijo de `RuesFormPanel`, igual que `TransferenciaFormPanel` lo hace dentro de
 * `TransferenciaPanel`: el panel decide cómo se ve la pestaña vacía, cargando o en error; este
 * componente decide qué campos existen.
 *
 * Dos decisiones que conviene no deshacer:
 *
 * - **La revisión previa no es un paso obligatorio.** El AC de #12203 la define como consulta que
 *   «no persiste ninguna fila», pensada para revisar antes de gastar una generación. Obligarla
 *   añadiría una llamada al proveedor por cada documento, que es justo lo que la idempotencia
 *   (CF-16) intenta evitar.
 * - **Nunca se pinta el `value` de un error de validación.** `ValidationError` lo trae opcional,
 *   pero el NIT es dato de un tercero: se muestran `field` y `message`, jamás lo capturado.
 */
export function RuesGeneracionForm() {
  const nitInputId = useId();
  const ayudaId = useId();

  const [nit, setNit] = useState("");
  const [revisando, setRevisando] = useState(false);
  const [generando, setGenerando] = useState(false);
  const [preview, setPreview] = useState<StandaloneRuesPreviewResult | null>(null);
  const [resultado, setResultado] = useState<StandaloneDocumentGenerateResult | null>(null);
  const [errores, setErrores] = useState<ValidationError[]>([]);
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);

  const nitNormalizado = nit.trim();
  const enVuelo = revisando || generando;
  const puedeConsultar = nitNormalizado.length > 0 && !enVuelo;

  function limpiarResultados() {
    setErrores([]);
    setErrorGeneral(null);
    setResultado(null);
  }

  /** Traduce un fallo del cliente a estado de UI sin filtrar el dato capturado. */
  function registrarFallo(error: unknown, mensajePorDefecto: string) {
    if (error instanceof ApiValidationError) {
      setErrores(error.errors);
    } else if (error instanceof ApiError) {
      setErrorGeneral(error.message);
    } else {
      setErrorGeneral(mensajePorDefecto);
    }
  }

  async function onRevisar() {
    setRevisando(true);
    limpiarResultados();
    setPreview(null);

    try {
      setPreview(await previewRuesCompany(nitNormalizado));
    } catch (error) {
      registrarFallo(error, "No se pudo consultar el RUES. Intenta nuevamente en unos minutos.");
    } finally {
      setRevisando(false);
    }
  }

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setGenerando(true);
    limpiarResultados();

    try {
      setResultado(await generateRuesDocument(nitNormalizado));
    } catch (error) {
      registrarFallo(error, "No se pudo generar el certificado. Intenta nuevamente en unos minutos.");
    } finally {
      setGenerando(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-4" aria-labelledby="rues-form-titulo">
      <h3 id="rues-form-titulo" className="text-sm font-semibold" style={{ color: "#162744" }}>
        Emitir Certificado RUES por NIT
      </h3>

      <div className="flex flex-col gap-1.5">
        <label htmlFor={nitInputId} className="text-xs font-semibold" style={{ color: "#162744" }}>
          NIT de la compañía
        </label>
        <input
          id={nitInputId}
          name="nit"
          value={nit}
          onChange={(event) => {
            setNit(event.target.value);
            // La vista previa deja de corresponder al NIT en pantalla en cuanto se edita.
            setPreview(null);
            limpiarResultados();
          }}
          inputMode="numeric"
          autoComplete="off"
          required
          aria-describedby={ayudaId}
          className="w-full max-w-xs rounded-xl border px-3 py-2 text-xs"
          placeholder="900123456"
        />
        <p id={ayudaId} className="text-[11px] opacity-70">
          Sin dígito de verificación ni puntos. El servidor calcula el DV y consulta el RUES en vivo.
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={onRevisar}
          disabled={!puedeConsultar}
          className="flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-60"
          style={{ color: "#557EFF", borderColor: "#557EFF" }}
        >
          <Search className="h-3.5 w-3.5" aria-hidden="true" />
          {revisando ? "Consultando…" : "Revisar antes de generar"}
        </button>

        <button
          type="submit"
          disabled={!puedeConsultar}
          aria-describedby="rues-generar-ayuda"
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ backgroundColor: "#557EFF" }}
        >
          {generando ? "Generando…" : "Generar certificado"}
        </button>

        <p id="rues-generar-ayuda" className="text-[11px] opacity-70">
          La revisión previa no persiste nada. El certificado se genera en el servidor y se descarga
          desde el historial.
        </p>
      </div>

      {preview && !preview.found && (
        <p role="status" className="text-[11px] font-semibold text-amber-700">
          El NIT consultado no tiene coincidencia en el RUES. Verifica el número antes de generar.
        </p>
      )}

      {preview?.found && (
        <section aria-labelledby="rues-preview-titulo" className="rounded-xl border p-3">
          <h4 id="rues-preview-titulo" className="text-xs font-semibold" style={{ color: "#162744" }}>
            Revisión previa — datos que quedarán congelados en el certificado
          </h4>
          <dl className="mt-2 grid gap-x-6 gap-y-1.5 sm:grid-cols-2">
            {preview.fields.map((campo) => (
              <div key={campo.key} className="flex flex-col">
                <dt className="text-[11px] opacity-70">{campo.label}</dt>
                <dd className="text-xs font-medium">{campo.value ?? "—"}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}

      {errores.length > 0 && (
        <ul role="alert" className="flex flex-col gap-1">
          {errores.map((issue) => (
            <li key={`${issue.field}-${issue.message}`} className="text-[11px] text-red-700">
              <span className="font-semibold">{issue.field}</span> — {issue.message}
            </li>
          ))}
        </ul>
      )}

      {errorGeneral && (
        <p role="alert" className="text-[11px] text-red-700">
          {errorGeneral}
        </p>
      )}

      {resultado && (
        <div role="status" className="flex flex-wrap items-center gap-2 rounded-xl border p-3">
          <FileCheck2 className="h-4 w-4 shrink-0" style={{ color: "#557EFF" }} aria-hidden="true" />
          <p className="text-xs">
            Certificado generado. La descarga se hace desde el historial: la generación devuelve
            siempre JSON, nunca el PDF.
          </p>
          <Link
            href="/admin/generacion-documental/historial"
            className="text-xs font-semibold underline"
            style={{ color: "#557EFF" }}
          >
            Ir al historial
          </Link>
        </div>
      )}
    </form>
  );
}
