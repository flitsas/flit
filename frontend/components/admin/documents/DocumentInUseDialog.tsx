"use client";

// Modal emergente (persistente) que se muestra al intentar desactivar un documento que
// está en uso (409). A diferencia del toast, no se autodescarta: el usuario debe cerrarlo
// con «Entendido» o la X. Lista los tipos de trámite donde el documento está en uso.
import { Ban } from "lucide-react";
import { Modal } from "@/components/atom/Modal";

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
    <Modal
      open
      onClose={onClose}
      size="sm"
      icon={Ban}
      iconBg="#FF4E00"
      title="No se puede desactivar"
      titleClassName="text-base font-bold text-[#FF4E00]"
    >
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
    </Modal>
  );
}
