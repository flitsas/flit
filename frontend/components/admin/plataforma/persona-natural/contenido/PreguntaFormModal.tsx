"use client";

// HU #14076 AC1 — Alta y edición de una pregunta frecuente (texto plano, sin HTML). Modal normal; si falla conserva
// lo escrito y muestra el motivo. La pregunta nueva va al final de la lista.
import { useState } from "react";
import { HelpCircle, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { PREGUNTA_MAX, RESPUESTA_MAX, type FaqPn, type GuardarFaq } from "@/lib/api/admin-pn-contenido";
import {
  PN_CANCEL_BTN_CLS,
  PN_FIELD_ERROR_CLS,
  PN_HINT_CLS,
  PN_INPUT_CLS,
  PN_INPUT_ERROR_CLS,
  PN_LABEL_CLS,
  PN_PRIMARY_BTN_CLS,
  PN_PRIMARY_BTN_STYLE,
} from "../pn-ui";
import { pnErrorMessage } from "../pnErrors";
import { validarTextoPlano } from "./contenidoReglas";

export interface PreguntaFormModalProps {
  /** Sin `faq`: alta. */
  faq?: FaqPn;
  onClose: () => void;
  onSave: (body: GuardarFaq) => Promise<void>;
}

export function PreguntaFormModal({ faq, onClose, onSave }: PreguntaFormModalProps) {
  const [pregunta, setPregunta] = useState(faq?.pregunta ?? "");
  const [respuesta, setRespuesta] = useState(faq?.respuesta ?? "");
  const [activa, setActiva] = useState(faq?.activa ?? true);
  const [errores, setErrores] = useState<{ pregunta?: string; respuesta?: string; general?: string }>({});
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const nuevos = {
      pregunta: validarTextoPlano(pregunta, "La pregunta", PREGUNTA_MAX) ?? undefined,
      respuesta: validarTextoPlano(respuesta, "La respuesta", RESPUESTA_MAX) ?? undefined,
    };
    setErrores(nuevos);
    if (nuevos.pregunta || nuevos.respuesta) return;
    setBusy(true);
    try {
      await onSave({ pregunta: pregunta.trim(), respuesta: respuesta.trim(), activa });
    } catch (err) {
      setErrores({ general: pnErrorMessage(err) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={faq ? "Editar pregunta frecuente" : "Nueva pregunta frecuente"}
      description="Texto simple: no se acepta HTML. El cambio queda en el borrador hasta que lo publique."
      icon={HelpCircle}
      size="lg"
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} disabled={busy} className={PN_CANCEL_BTN_CLS}>
            Cancelar
          </button>
          <button type="submit" form="pn-faq-form" disabled={busy} className={PN_PRIMARY_BTN_CLS} style={PN_PRIMARY_BTN_STYLE}>
            {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
            Guardar pregunta
          </button>
        </div>
      }
    >
      <form id="pn-faq-form" onSubmit={handleSubmit} noValidate className="flex flex-col gap-4">
        <label className={PN_LABEL_CLS}>
          Pregunta
          <input
            value={pregunta}
            onChange={(e) => setPregunta(e.target.value)}
            maxLength={PREGUNTA_MAX}
            aria-invalid={Boolean(errores.pregunta)}
            className={`${PN_INPUT_CLS} ${errores.pregunta ? PN_INPUT_ERROR_CLS : ""}`}
          />
          {errores.pregunta && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.pregunta}</span>}
        </label>
        <label className={PN_LABEL_CLS}>
          Respuesta
          <textarea
            value={respuesta}
            onChange={(e) => setRespuesta(e.target.value)}
            maxLength={RESPUESTA_MAX}
            rows={5}
            aria-invalid={Boolean(errores.respuesta)}
            className={`${PN_INPUT_CLS} h-auto py-2 ${errores.respuesta ? PN_INPUT_ERROR_CLS : ""}`}
          />
          <span className={PN_HINT_CLS}>{respuesta.trim().length} de {RESPUESTA_MAX} caracteres.</span>
          {errores.respuesta && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.respuesta}</span>}
        </label>
        <label className="flex cursor-pointer items-center gap-2 text-sm font-semibold text-flit-primary dark:text-white">
          <input
            type="checkbox"
            checked={activa}
            onChange={(e) => setActiva(e.target.checked)}
            className="h-4 w-4 accent-[color:var(--color-flit-brand)]"
          />
          Activa (se publica en el portal)
        </label>
        {errores.general && (
          <p role="alert" className="text-sm font-semibold text-[#C2410C]">
            {errores.general}
          </p>
        )}
      </form>
    </Modal>
  );
}
