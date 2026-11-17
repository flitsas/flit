"use client";

import { Check } from "lucide-react";
import { useId, useState, type FormEvent } from "react";
import { boton } from "@/components/landing/estilos";
import { Card } from "@/components/tramite/piezas";
import { DETALLE_MEDIO_MAX, MEDIOS_CAJA, PREFACTURA_MAX_BYTES, PREFACTURA_TIPOS, type MedioCaja } from "@/lib/caja/tipos";
import { MensajeCaja } from "./MensajeCaja";

export interface DatosDeCobro {
  medio: MedioCaja;
  medioDetalle?: string;
  prefactura: File | null;
}

type Props = {
  enviando: boolean;
  /** Error del servidor al confirmar (total que cambió, trámite ya pagado, vencido…). */
  mensaje: { texto: string; tono: "error" | "aviso" } | null;
  onConfirmar: (datos: DatosDeCobro) => void;
  onVolver: () => void;
};

/** Validación de la prefactura en el navegador (el servidor la repite por la firma del archivo). Devuelve el motivo o null. */
export function errorDePrefactura(archivo: File): string | null {
  if (!PREFACTURA_TIPOS.includes(archivo.type)) return "La prefactura debe ser PDF, JPG o PNG.";
  if (archivo.size > PREFACTURA_MAX_BYTES) return "La prefactura debe pesar 5 MB o menos.";
  return null;
}

/** Medio de cobro, detalle si es «Otro», prefactura opcional y los botones. Sin campo de valor: lo fija el servidor. */
export function ConfirmarCobroForm({ enviando, mensaje, onConfirmar, onVolver }: Props) {
  const idBase = useId();
  const [medio, setMedio] = useState<MedioCaja | null>(null);
  const [detalle, setDetalle] = useState("");
  const [prefactura, setPrefactura] = useState<File | null>(null);
  const [errores, setErrores] = useState<{ medio?: string; detalle?: string; prefactura?: string }>({});

  function elegirArchivo(archivo: File | null) {
    if (!archivo) {
      setPrefactura(null);
      setErrores((e) => ({ ...e, prefactura: undefined }));
      return;
    }
    const motivo = errorDePrefactura(archivo);
    setPrefactura(motivo ? null : archivo);
    setErrores((e) => ({ ...e, prefactura: motivo ?? undefined }));
  }

  function enviar(e: FormEvent) {
    e.preventDefault();
    const nuevos: typeof errores = { prefactura: errores.prefactura };
    if (!medio) nuevos.medio = "Elija cómo se cobró.";
    else if (medio === "otro" && detalle.trim() === "") nuevos.detalle = "Escriba el detalle del medio.";
    setErrores(nuevos);
    if (nuevos.medio || nuevos.detalle || nuevos.prefactura || !medio) return;
    onConfirmar({ medio, medioDetalle: medio === "otro" ? detalle.trim() : undefined, prefactura });
  }

  return (
    <Card title="Confirmar cobro" id="confirmar-cobro">
    <form onSubmit={enviar} noValidate aria-labelledby="confirmar-cobro-titulo">

      <fieldset aria-describedby={errores.medio ? `${idBase}-medio-error` : undefined}>
        <legend className="mb-2 text-sm font-medium">¿Cómo se cobró?</legend>
        <div className="flex flex-wrap gap-x-6 gap-y-2">
          {MEDIOS_CAJA.map((m) => (
            <label key={m.id} className="flex min-h-11 cursor-pointer items-center gap-2 text-sm">
              <input
                type="radio"
                name={`${idBase}-medio`}
                value={m.id}
                checked={medio === m.id}
                onChange={() => {
                  setMedio(m.id);
                  setErrores((x) => ({ ...x, medio: undefined, detalle: undefined }));
                }}
                className="h-4 w-4 accent-[var(--flit-blue)]"
              />
              {m.etiqueta}
            </label>
          ))}
        </div>
        {errores.medio && (
          <p id={`${idBase}-medio-error`} role="alert" className="mt-1 text-xs font-semibold text-destructive">
            {errores.medio}
          </p>
        )}
      </fieldset>

      {medio === "otro" && (
        <div className="mt-4">
          <label htmlFor={`${idBase}-detalle`} className="mb-1 block text-sm font-medium">
            ¿Cuál?
          </label>
          <input
            id={`${idBase}-detalle`}
            className="field"
            maxLength={DETALLE_MEDIO_MAX}
            autoComplete="off"
            value={detalle}
            aria-invalid={errores.detalle ? true : undefined}
            aria-describedby={errores.detalle ? `${idBase}-detalle-error` : undefined}
            onChange={(e) => {
              setDetalle(e.target.value);
              setErrores((x) => ({ ...x, detalle: undefined }));
            }}
          />
          {errores.detalle && (
            <p id={`${idBase}-detalle-error`} role="alert" className="mt-1 text-xs font-semibold text-destructive">
              {errores.detalle}
            </p>
          )}
        </div>
      )}

      <div className="mt-4">
        <label htmlFor={`${idBase}-prefactura`} className="mb-1 block text-sm font-medium">
          Prefactura <span className="font-normal text-muted-foreground">(opcional · PDF, JPG o PNG, hasta 5 MB)</span>
        </label>
        <input
          id={`${idBase}-prefactura`}
          type="file"
          accept=".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png"
          className="block w-full text-sm file:mr-3 file:min-h-10 file:cursor-pointer file:rounded-full file:border-0 file:bg-secondary file:px-4 file:text-sm file:font-semibold file:text-secondary-foreground"
          aria-invalid={errores.prefactura ? true : undefined}
          aria-describedby={errores.prefactura ? `${idBase}-prefactura-error` : undefined}
          onChange={(e) => {
            elegirArchivo(e.target.files?.[0] ?? null);
            if (!e.target.files?.[0] || errorDePrefactura(e.target.files[0])) e.target.value = "";
          }}
        />
        {errores.prefactura && (
          <p id={`${idBase}-prefactura-error`} role="alert" className="mt-1 text-xs font-semibold text-destructive">
            {errores.prefactura}
          </p>
        )}
      </div>

      {mensaje && (
        <div className="mt-4">
          <MensajeCaja tono={mensaje.tono}>{mensaje.texto}</MensajeCaja>
        </div>
      )}

      <div className="mt-6 flex flex-wrap items-center justify-end gap-3">
        <button type="button" className={boton("back", "min-h-11 px-6", "none")} onClick={onVolver} disabled={enviando}>
          Volver
        </button>
        <button type="submit" className={boton("positive", "min-h-11 px-6 text-secondary!", "none")} disabled={enviando} aria-busy={enviando}>
          <Check aria-hidden />
          {enviando ? "Registrando…" : "Confirmar cobro"}
        </button>
      </div>
    </form>
    </Card>
  );
}
