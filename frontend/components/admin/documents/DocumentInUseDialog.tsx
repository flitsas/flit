"use client";

// Modal emergente (persistente) que se muestra al intentar desactivar un documento que
// está en uso (409). A diferencia del toast, no se autodescarta: el usuario debe cerrarlo
// con «Entendido» o la X. Lista los tipos de trámite donde el documento está en uso.
import { Ban, X } from "lucide-react";

export interface DocumentInUseProcedureType {
  codigo: string;
  nombre: string;
}

export interface DocumentInUseDialogProps {
  documentName: string;
  procedureTypes: DocumentInUseProcedureType[];
  onClose: () => void;
}

export function DocumentInUseDialog({
  documentName,
  procedureTypes,
  onClose,
}: DocumentInUseDialogProps) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="doc-inuse-title"
    >
      <div
        className="w-full max-w-md rounded-2xl border bg-white p-6 shadow-2xl dark:bg-[#0B0F14]"
      >
        <div className="mb-3 flex items-start justify-between">
          <div className="flex items-center gap-2">
            <span
              className="flex h-9 w-9 items-center justify-center rounded-xl"
              style={{ background: "#FF4E00" }}
            >
              <Ban className="h-4.5 w-4.5 text-white" />
            </span>
            <h2 id="doc-inuse-title" className="text-base font-bold" style={{ color: "#FF4E00" }}>
              No se puede desactivar
            </h2>
          </div>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            className="text-slate-400 hover:text-slate-700"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <p className="text-xs opacity-80">
          El documento <strong>«{documentName}»</strong>{" "}
          {procedureTypes.length > 0
            ? "está en uso por estos tipos de trámite. Quítalo de ellos antes de desactivarlo:"
            : "está en uso por uno o más tipos de trámite. Quítalo de esos trámites antes de desactivarlo."}
        </p>

        {procedureTypes.length > 0 && (
          <ul className="mt-3 flex flex-col gap-1.5" aria-label="Trámites que usan el documento">
            {procedureTypes.map((p) => (
              <li
                key={p.codigo}
                className="flex items-center justify-between rounded-xl border px-3 py-2"
              >
                <span className="text-xs font-semibold">{p.nombre}</span>
                <span className="font-mono text-[10px] opacity-60">{p.codigo}</span>
              </li>
            ))}
          </ul>
        )}

        <div className="mt-5 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Entendido
          </button>
        </div>
      </div>
    </div>
  );
}
