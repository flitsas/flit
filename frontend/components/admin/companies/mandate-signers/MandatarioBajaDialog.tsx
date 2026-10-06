"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { AlertTriangle } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import {
  impactFromConfirmationError,
  type MandateSigner,
  type MandateSignerImpact,
} from "@/lib/api/admin-mandate-signers";
import { mensajeErrorAccion, type AccionBaja } from "@/lib/plataforma/mandatario-baja";

export interface MandatarioBajaDialogProps {
  signer: MandateSigner;
  accion: AccionBaja;
  /** Consulta el impacto de la baja (solo lectura). */
  loadImpact: (signal: AbortSignal) => Promise<MandateSignerImpact>;
  /** Ejecuta la baja. `confirmarImpacto` es true cuando el usuario vio un impacto y lo confirmó. */
  onConfirm: (confirmarImpacto: boolean) => Promise<void>;
  onClose: () => void;
  officeLabel: (transitOfficeId: string) => string;
  companyLabel: (companyTenantId: string) => string;
}

type Carga = { fase: "cargando" } | { fase: "error" } | { fase: "listo"; impact: MandateSignerImpact };

/**
 * HU #13140 — confirmación previa de desactivar o eliminar un mandatario. Con impacto (único activo
 * de una compañía en un organismo, defaults o trámites sin aprobar) lo lista y exige confirmar; sin
 * impacto es una confirmación corta. Cancelar no envía nada.
 */
export function MandatarioBajaDialog({
  signer,
  accion,
  loadImpact,
  onConfirm,
  onClose,
  officeLabel,
  companyLabel,
}: MandatarioBajaDialogProps) {
  const [carga, setCarga] = useState<Carga>({ fase: "cargando" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Guarda contra el doble clic: el estado `busy` llega un render tarde, la ref no.
  const enviando = useRef(false);
  // El diálogo se monta para un mandatario fijo: basta la consulta recibida al abrirse.
  const loadRef = useRef(loadImpact);

  const cargar = useCallback((signal: AbortSignal) => {
    setCarga({ fase: "cargando" });
    loadRef
      .current(signal)
      .then((impact) => {
        if (!signal.aborted) setCarga({ fase: "listo", impact });
      })
      .catch(() => {
        if (!signal.aborted) setCarga({ fase: "error" });
      });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    cargar(controller.signal);
    return () => controller.abort();
  }, [cargar]);

  const eliminar = accion === "eliminar";
  const impact = carga.fase === "listo" ? carga.impact : null;
  const conImpacto = impact?.hasImpact === true;

  const confirmar = async () => {
    if (enviando.current || carga.fase !== "listo") return;
    enviando.current = true;
    setBusy(true);
    setError(null);
    try {
      await onConfirm(conImpacto);
      onClose();
    } catch (err) {
      const nuevoImpacto = impactFromConfirmationError(err);
      if (nuevoImpacto) {
        // El impacto cambió desde que se abrió el diálogo: se muestra el nuevo y se pide confirmar de nuevo.
        setCarga({ fase: "listo", impact: nuevoImpacto });
        setError("El impacto cambió mientras decidías. Revísalo y confirma de nuevo.");
      } else {
        setError(mensajeErrorAccion(err));
      }
    } finally {
      enviando.current = false;
      setBusy(false);
    }
  };

  const titulo = eliminar ? "Eliminar mandatario" : "Desactivar mandatario";
  const verbo = eliminar ? "Eliminar" : "Desactivar";

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      icon={AlertTriangle}
      iconBg={conImpacto || eliminar ? "#FF4E00" : "#F9AC00"}
      title={titulo}
      titleClassName="text-base font-bold text-[#162744] dark:text-white"
      size="md"
      zClassName="z-[110]"
    >
      <div className="space-y-3 text-xs" data-testid="mandatario-baja-dialog">
        {carga.fase === "cargando" && (
          <p role="status" className="text-[#59677D] dark:text-white/65">
            Revisando qué afecta…
          </p>
        )}

        {carga.fase === "error" && (
          <div role="alert" className="space-y-2">
            <p className="text-[#E5484D]">No se pudo revisar el impacto. Inténtalo de nuevo.</p>
            <button
              type="button"
              className="rounded-xl border px-3 py-1.5 text-xs font-semibold"
              onClick={() => {
                const controller = new AbortController();
                cargar(controller.signal);
              }}
            >
              Reintentar
            </button>
          </div>
        )}

        {carga.fase === "listo" && !conImpacto && (
          <p>
            {eliminar ? (
              <>
                ¿Eliminar a <strong>{signer.fullName}</strong>? Sale de las listas y se conserva su historial.
              </>
            ) : (
              <>
                ¿Desactivar a <strong>{signer.fullName}</strong>? No firmará mandatos hasta que lo reactives.
              </>
            )}
          </p>
        )}

        {impact && conImpacto && (
          <div
            className="space-y-2 rounded-xl border px-3 py-2 leading-relaxed"
            style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)" }}
            role="note"
            data-testid="mandatario-baja-impacto"
          >
            <p className="font-semibold text-[#162744] dark:text-white">
              {eliminar ? "Eliminar" : "Desactivar"} a {signer.fullName} tiene estos efectos:
            </p>
            {impact.onlyActiveFor.length > 0 && (
              <div>
                <p className="font-medium">Quedan sin mandatario activo:</p>
                <ul className="ml-4 list-disc">
                  {impact.onlyActiveFor.map((l) => (
                    <li key={`${l.companyTenantId}-${l.transitOfficeId}`}>
                      {companyLabel(l.companyTenantId)} en {officeLabel(l.transitOfficeId)}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {impact.defaults.length > 0 && (
              <div>
                <p className="font-medium">Deja de ser el default de:</p>
                <ul className="ml-4 list-disc">
                  {impact.defaults.map((d) => (
                    <li key={`${d.kind}-${d.companyTenantId ?? "ot"}-${d.transitOfficeId}`}>
                      {d.kind === "office"
                        ? `Mandatario general de ${officeLabel(d.transitOfficeId)}`
                        : `${d.companyTenantId ? companyLabel(d.companyTenantId) : "Compañía"} en ${officeLabel(d.transitOfficeId)}`}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {impact.pendingProcedures > 0 && (
              <p>
                <strong>
                  {impact.pendingProcedures === 1
                    ? "1 trámite radicado sin aprobar"
                    : `${impact.pendingProcedures} trámites radicados sin aprobar`}
                </strong>{" "}
                lo usan: se reasignan con la prelación (mandatario propio, asociado o default del OT). Si
                no queda nadie, el OT decide al aprobar.
              </p>
            )}
            {eliminar && <p>Se conserva el historial de los trámites ya firmados.</p>}
          </div>
        )}

        {error && (
          <p role="alert" className="text-[#E5484D]" data-testid="mandatario-baja-error">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void confirmar()}
            disabled={busy || carga.fase !== "listo"}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: eliminar || conImpacto ? "#FF4E00" : "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {conImpacto ? `Sí, ${verbo.toLowerCase()}` : verbo}
          </button>
        </div>
      </div>
    </Modal>
  );
}
