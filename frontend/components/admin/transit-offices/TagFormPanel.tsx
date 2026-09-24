"use client";

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import type { CreateOtDocumentTagRequest, OtDocumentTag } from "@/lib/api/types-ot";
import { OtSidePanel } from "./OtSidePanel";
import { OT_INPUT_CLS } from "./ot-form-styles";

/**
 * Paleta CERRADA de colores de marca FLIT (HU #12883 AC3/AC1) — reemplaza el `<input
 * type="color">` de libre elección: un color fuera de la paleta de marca es drift visual y,
 * además, el hex por defecto (#FF0000) no era un token FLIT. Valores desde
 * `flit_design_tokens.json` (`color.brand` + `color.annotation.pdf20Ago.amber`).
 */
export const TAG_COLOR_OPTIONS: { hex: string; name: string }[] = [
  { hex: "#557EFF", name: "Azul" },
  { hex: "#00DBD5", name: "Cian" },
  { hex: "#8CC63F", name: "Verde" },
  { hex: "#FF4E00", name: "Naranja" },
  { hex: "#162744", name: "Navy" },
  { hex: "#F9AC00", name: "Ámbar" },
];

const DEFAULT_TAG_COLOR = TAG_COLOR_OPTIONS[0].hex;

export interface TagFormPanelProps {
  open: boolean;
  onClose: () => void;
  onCreate: (body: CreateOtDocumentTagRequest) => Promise<OtDocumentTag>;
  onSaved: (tag: OtDocumentTag) => void;
}

/** Formulario crear etiqueta documental (HU #10224 AC4; paleta cerrada HU #12883 AC3). */
export function TagFormPanel({ open, onClose, onCreate, onSaved }: TagFormPanelProps) {
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [color, setColor] = useState(DEFAULT_TAG_COLOR);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset de formulario al abrir panel lateral
    setCode("");
    setName("");
    setColor(DEFAULT_TAG_COLOR);
  }, [open]);

  const submit = async () => {
    setSubmitting(true);
    try {
      const created = await onCreate({
        code: code.trim().toUpperCase(),
        name: name.trim(),
        color,
      });
      onSaved(created);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <OtSidePanel
      open={open}
      title="Nueva etiqueta"
      ariaLabel="Nueva etiqueta"
      onClose={onClose}
      disabled={submitting}
      footer={
        <button
          type="button"
          disabled={submitting || !code.trim() || !name.trim()}
          className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: "#557EFF" }}
          onClick={() => void submit()}
        >
          {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
          Guardar
        </button>
      }
    >
      <div className="space-y-3">
        <label className="block text-xs font-semibold text-foreground">
          Código
          <input className={`mt-1 ${OT_INPUT_CLS}`} value={code} onChange={(e) => setCode(e.target.value)} />
        </label>
        <label className="block text-xs font-semibold text-foreground">
          Nombre
          <input className={`mt-1 ${OT_INPUT_CLS}`} value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <fieldset>
          <legend className="text-xs font-semibold text-foreground">Color</legend>
          <div
            className="mt-2 flex flex-wrap gap-3"
            role="radiogroup"
            aria-label="Color de la etiqueta"
          >
            {TAG_COLOR_OPTIONS.map((opt) => (
              <label
                key={opt.hex}
                className="flex cursor-pointer flex-col items-center gap-1 text-xs text-foreground"
              >
                <input
                  type="radio"
                  name="tag-color"
                  value={opt.hex}
                  checked={color === opt.hex}
                  onChange={() => setColor(opt.hex)}
                  className="peer sr-only"
                />
                <span
                  aria-hidden="true"
                  className="h-7 w-7 rounded-full border-2 border-white shadow-sm ring-offset-2 peer-checked:ring-2 peer-checked:ring-[#557EFF] peer-focus-visible:ring-2 peer-focus-visible:ring-[#557EFF] dark:border-[#162744]"
                  style={{ background: opt.hex }}
                />
                {opt.name}
              </label>
            ))}
          </div>
        </fieldset>
      </div>
    </OtSidePanel>
  );
}
