"use client";

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import type { CreateOtDocumentTagRequest, OtDocumentTag } from "@/lib/api/types-ot";
import { OtSidePanel } from "./OtSidePanel";
import { OT_INPUT_CLS } from "./ot-form-styles";

const HEX_RE = /^#[0-9A-Fa-f]{6}$/;

export interface TagFormPanelProps {
  open: boolean;
  onClose: () => void;
  onCreate: (body: CreateOtDocumentTagRequest) => Promise<OtDocumentTag>;
  onSaved: (tag: OtDocumentTag) => void;
}

/** Formulario crear etiqueta documental (HU #10224 AC4). */
export function TagFormPanel({ open, onClose, onCreate, onSaved }: TagFormPanelProps) {
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [color, setColor] = useState("#FF0000");
  const [colorError, setColorError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset de formulario al abrir panel lateral
    setCode("");
    setName("");
    setColor("#FF0000");
    setColorError(null);
  }, [open]);

  const submit = async () => {
    if (!HEX_RE.test(color)) {
      setColorError("Color inválido — use formato #RRGGBB");
      return;
    }
    setColorError(null);
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
        <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
          Código
          <input className={`mt-1 ${OT_INPUT_CLS}`} value={code} onChange={(e) => setCode(e.target.value)} />
        </label>
        <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
          Nombre
          <input className={`mt-1 ${OT_INPUT_CLS}`} value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
          Color
          <div className="mt-1 flex items-center gap-2">
            <input
              type="color"
              aria-label="Selector de color"
              value={color}
              onChange={(e) => setColor(e.target.value.toUpperCase())}
              className="h-9 w-12 cursor-pointer rounded border"
              style={{ borderColor: "#DFE5ED" }}
            />
            <input
              className={OT_INPUT_CLS}
              value={color}
              onChange={(e) => setColor(e.target.value)}
              aria-invalid={colorError ? true : undefined}
            />
          </div>
          {colorError && (
            <p className="mt-1 text-[11px]" style={{ color: "#FF4E00" }} role="alert">
              {colorError}
            </p>
          )}
        </label>
      </div>
    </OtSidePanel>
  );
}
