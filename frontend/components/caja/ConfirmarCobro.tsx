"use client";

// Desglose del trámite y formulario de confirmación del cobro. Sin campo de valor: el servidor cobra el total vigente y,
// si cambió desde que la caja lo vio, responde 409 `total_cambio` con el nuevo total.
import { useId, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PN_FIELD_ERROR_CLS, PN_HINT_CLS, PN_INPUT_CLS, PN_LABEL_CLS } from "@/components/admin/plataforma/persona-natural/pn-ui";
import { formatPesos } from "@/lib/api/admin-pn-tarifas";
import {
  DETALLE_MEDIO_MAX,
  MEDIOS_CAJA,
  PREFACTURA_MAX_BYTES,
  PREFACTURA_TIPOS,
  type MedioCaja,
  type TramiteParaCobrar,
} from "@/lib/api/ot-caja";
import type { MensajeDeCaja } from "@/lib/caja/mensajes";

/** Aviso de la caja: el «ya pagado» y el «total cambió» van en amarillo; el resto, en rojo. */
export function MensajeCaja({ mensaje, id }: { mensaje: MensajeDeCaja; id?: string }) {
  return (
    <InlineAlert id={id} tone={mensaje.tono === "aviso" ? "warning" : "error"}>
      {mensaje.texto}
    </InlineAlert>
  );
}

/** Desglose y total tal como lo calcula el servidor. Solo lectura: la caja no edita el valor. */
export function DesgloseCobro({ tramite, totalCop }: { tramite: TramiteParaCobrar; totalCop: number }) {
  return (
    <section aria-labelledby="caja-desglose-titulo" className="rounded-[9px] border border-border bg-card p-6">
      <h2 id="caja-desglose-titulo" className="text-xl font-bold text-[color:var(--di-heading)]">
        {tramite.tipoTramite.nombre}
      </h2>
      <p className="mt-1 text-sm text-[color:var(--di-text-tertiary)]">
        <span className="font-semibold">{tramite.codigo}</span> · {tramite.organismo.nombre}
      </p>
      <table className="mt-4 w-full text-sm">
        <caption className="sr-only">Desglose del cobro</caption>
        <thead>
          <tr className="text-left text-[0.8125rem] font-bold uppercase tracking-[0.03em]" style={{ background: "var(--table-header-bg)", color: "var(--table-header-fg)" }}>
            <th scope="col" className="px-3.5 py-2.5">
              Concepto
            </th>
            <th scope="col" className="px-3.5 py-2.5 text-right">
              Valor
            </th>
          </tr>
        </thead>
        <tbody>
          {tramite.lineas.map((l, i) => (
            <tr key={`${l.concepto}-${i}`} className="border-b border-border">
              <td className="px-3.5 py-2.5">{l.concepto}</td>
              <td className="px-3.5 py-2.5 text-right tabular-nums">{formatPesos(l.valorCop)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" className="px-3.5 py-3 text-left text-base font-bold">
              Total a cobrar
            </th>
            <td className="px-3.5 py-3 text-right text-lg font-bold tabular-nums" data-testid="total-caja">
              {formatPesos(totalCop)}
            </td>
          </tr>
        </tfoot>
      </table>
    </section>
  );
}

export interface DatosDeCobro {
  medio: MedioCaja;
  medioDetalle?: string;
  prefactura: File | null;
}

/** Validación de la prefactura en el navegador (el servidor la repite por la firma del archivo). Devuelve el motivo o null. */
export function errorDePrefactura(archivo: File): string | null {
  if (!PREFACTURA_TIPOS.includes(archivo.type)) return "La prefactura debe ser PDF, JPG o PNG.";
  if (archivo.size > PREFACTURA_MAX_BYTES) return "La prefactura debe pesar 5 MB o menos.";
  return null;
}

interface Props {
  enviando: boolean;
  /** Error del servidor al confirmar (total que cambió, trámite ya pagado, vencido…). */
  mensaje: MensajeDeCaja | null;
  onConfirmar: (datos: DatosDeCobro) => void;
  onVolver: () => void;
}

/** Medio de cobro, detalle si es «Otro», prefactura opcional y los botones. */
export function ConfirmarCobroForm({ enviando, mensaje, onConfirmar, onVolver }: Props) {
  const idBase = useId();
  const [medio, setMedio] = useState<MedioCaja | null>(null);
  const [detalle, setDetalle] = useState("");
  const [prefactura, setPrefactura] = useState<File | null>(null);
  const [errores, setErrores] = useState<{ medio?: string; detalle?: string; prefactura?: string }>({});

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
    <form onSubmit={enviar} noValidate aria-labelledby={`${idBase}-titulo`} className="rounded-[9px] border border-border bg-card p-6">
      <h2 id={`${idBase}-titulo`} className="mb-4 text-xl font-bold text-[color:var(--di-heading)]">
        Confirmar cobro
      </h2>

      <fieldset aria-describedby={errores.medio ? `${idBase}-medio-error` : undefined}>
        <legend className="mb-2 text-sm font-semibold text-flit-primary">¿Cómo se cobró?</legend>
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
                className="h-4 w-4 accent-[var(--kit-azul-relleno)]"
              />
              {m.etiqueta}
            </label>
          ))}
        </div>
        {errores.medio && (
          <p id={`${idBase}-medio-error`} role="alert" className={`mt-1 ${PN_FIELD_ERROR_CLS}`}>
            {errores.medio}
          </p>
        )}
      </fieldset>

      {medio === "otro" && (
        <div className="mt-4">
          <label htmlFor={`${idBase}-detalle`} className={PN_LABEL_CLS}>
            ¿Cuál?
            <input
              id={`${idBase}-detalle`}
              className={PN_INPUT_CLS}
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
          </label>
          {errores.detalle && (
            <p id={`${idBase}-detalle-error`} role="alert" className={`mt-1 ${PN_FIELD_ERROR_CLS}`}>
              {errores.detalle}
            </p>
          )}
        </div>
      )}

      <div className="mt-4">
        <label htmlFor={`${idBase}-prefactura`} className={PN_LABEL_CLS}>
          <span>
            Prefactura <span className={PN_HINT_CLS}>(opcional · PDF, JPG o PNG, hasta 5 MB)</span>
          </span>
          <input
            id={`${idBase}-prefactura`}
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png"
            className="block w-full text-sm font-normal file:mr-3 file:min-h-10 file:cursor-pointer file:rounded-full file:border-0 file:bg-secondary file:px-4 file:text-sm file:font-semibold file:text-secondary-foreground"
            aria-invalid={errores.prefactura ? true : undefined}
            aria-describedby={errores.prefactura ? `${idBase}-prefactura-error` : undefined}
            onChange={(e) => {
              const archivo = e.target.files?.[0] ?? null;
              const motivo = archivo ? errorDePrefactura(archivo) : null;
              setPrefactura(archivo && !motivo ? archivo : null);
              setErrores((x) => ({ ...x, prefactura: motivo ?? undefined }));
              if (motivo) e.target.value = "";
            }}
          />
        </label>
        {errores.prefactura && (
          <p id={`${idBase}-prefactura-error`} role="alert" className={`mt-1 ${PN_FIELD_ERROR_CLS}`}>
            {errores.prefactura}
          </p>
        )}
      </div>

      {mensaje && (
        <div className="mt-4">
          <MensajeCaja mensaje={mensaje} />
        </div>
      )}

      <div className="mt-6 flex flex-wrap items-center justify-end gap-3">
        <Button variant="secondary" onClick={onVolver} disabled={enviando}>
          Volver
        </Button>
        <Button type="submit" variant="positiva" cargando={enviando}>
          {enviando ? "Registrando…" : "Confirmar cobro"}
        </Button>
      </div>
    </form>
  );
}
