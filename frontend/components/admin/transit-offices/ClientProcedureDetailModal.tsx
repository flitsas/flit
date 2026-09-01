"use client";

import { useEffect, useState } from "react";
import { Coins, FileText, FolderCheck, Users, X } from "lucide-react";
import { DetalleStepper } from "@/components/operacion/detalle/DetalleStepper";
import { DetalleTramiteShell } from "@/components/operacion/detalle/DetalleTramiteShell";
import {
  DETALLE_BLUE,
  DETALLE_BORDER,
  DETALLE_NAVY,
} from "@/components/operacion/detalle/detalle-visual";
import { SeccionCargando } from "@/components/operacion/detalle/primitivos";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  fetchOtClientProcedure,
  fetchOtDocuments,
  type OtApiScope,
  type OtProcedureAttachment,
} from "@/lib/api/admin-ot";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { OtDetalleActores } from "./detalle/OtDetalleActores";
import { OtDetalleComercial } from "./detalle/OtDetalleComercial";
import { OtDetalleTramiteVehiculo } from "./detalle/OtDetalleTramiteVehiculo";
import { OtDetalleVehiculoSidebar } from "./detalle/OtDetalleVehiculoSidebar";
import { OtDocumentosTab } from "./OtDocumentosTab";
import { formatOtProcedureStatus, procedureStatusTone } from "./ot-utils";

export type OtDetalleSeccionId = "vehiculo" | "actores" | "documentos" | "comercial";

type SeccionId = OtDetalleSeccionId;

const SECCIONES: { id: SeccionId; label: string; Icon: typeof FileText }[] = [
  { id: "vehiculo", label: "Trámite y vehículo", Icon: FileText },
  { id: "actores", label: "Actores", Icon: Users },
  { id: "documentos", label: "Documentos", Icon: FolderCheck },
  { id: "comercial", label: "Datos comerciales", Icon: Coins },
];

export interface ClientProcedureDetailModalProps {
  open: boolean;
  /** Fila de la bandeja con la que se abrió: pinta el modal sin esperar a la red. */
  procedure: OtClientProcedure | null;
  onClose: () => void;
  scope?: OtApiScope;
  /** El OT en modo lectura no puede actualizar el consolidado del expediente. */
  readOnly?: boolean;
  /**
   * Sección con la que abre. La bandeja tiene dos entradas al mismo trámite —«ver detalle» y «ver
   * documentos»— y ambas llevan a este modal: la segunda simplemente aterriza en su sección.
   */
  initialSection?: OtDetalleSeccionId;
}

/**
 * Modal de detalle del trámite en la bandeja del OT (HU #11930).
 *
 * Sustituye al panel lateral y adopta el shell del detalle del gestor —canvas claro sobre overlay,
 * encabezado compuesto, navegación por pasos y tarjeta lateral de vehículo—, de modo que el mismo
 * trámite se lea igual en los dos módulos.
 *
 * Es SOLO LECTURA sobre los datos del trámite: el OT consulta, previsualiza y descarga. La única
 * acción que ofrece —actualizar el consolidado del expediente— vive dentro de la sección Documentos,
 * que antes era un segundo modal encima de este.
 *
 * Fuera de alcance por decisión de producto: pre-vuelo de requisitos, trazabilidad de identidad y
 * línea de tiempo del trámite.
 */
export function ClientProcedureDetailModal({
  open,
  procedure,
  onClose,
  scope,
  readOnly = false,
  initialSection = "vehiculo",
}: ClientProcedureDetailModalProps) {
  const [detail, setDetail] = useState<OtClientProcedure | null>(procedure);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [docs, setDocs] = useState<OtProcedureAttachment[]>([]);
  const [seccion, setSeccion] = useState<SeccionId>(initialSection);

  useEffect(() => {
    if (!open || !procedure) {
      return;
    }

    const controller = new AbortController();

    // El setState va dentro de la función async, no en el cuerpo síncrono del efecto:
    // react-hooks/set-state-in-effect tumba el lint en cuanto se hace al revés.
    const load = async () => {
      setDetail(procedure);
      setSeccion(initialSection);
      setDetailLoading(true);
      setDetailError(null);
      setDocs([]);

      const [full, expediente] = await Promise.allSettled([
        fetchOtClientProcedure(procedure.id, controller.signal, scope),
        fetchOtDocuments(procedure.id, scope),
      ]);

      if (controller.signal.aborted) return;

      if (full.status === "fulfilled") {
        setDetail(full.value);
      } else {
        // La fila de la bandeja ya está pintada: se avisa del desfase en vez de vaciar el modal.
        setDetailError("No se pudo refrescar el detalle; se muestran los datos de la bandeja.");
      }
      if (expediente.status === "fulfilled") {
        setDocs(expediente.value.data ?? []);
      }
      setDetailLoading(false);
    };

    void load();
    return () => controller.abort();
  }, [open, procedure, scope, initialSection]);

  const row = detail ?? procedure;
  if (!open || !row) return null;

  const titulo = row.procedureTypeName?.trim()
    ? `Detalle de ${row.procedureTypeName.trim().toLocaleLowerCase("es")}`
    : "Detalle del trámite";

  const header = ({ titleId }: { titleId: string }) => (
    <div className="flex flex-wrap items-start justify-between gap-3 px-1 py-2">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <h2 id={titleId} className="text-[22px] font-bold leading-tight" style={{ color: DETALLE_BLUE }}>
            {titulo}
          </h2>
          <StatusBadge
            label={formatOtProcedureStatus(row.status)}
            tone={procedureStatusTone(row.status)}
          />
        </div>
        <p className="mt-1 text-[12px]" style={{ color: "#475569" }}>
          <span className="font-mono">{row.referenceNumber}</span> · {row.placa ?? "—"} ·{" "}
          Empresa: {row.clientTenantName ?? "—"}
        </p>
      </div>
      <button
        type="button"
        onClick={onClose}
        aria-label="Cerrar"
        className="rounded-xl border border-[#DFE5ED] bg-white p-2 dark:border-white/5 dark:bg-[#0B0F14]"
        style={{ borderColor: DETALLE_BORDER, color: DETALLE_NAVY }}
      >
        <X className="h-4 w-4" aria-hidden="true" />
      </button>
    </div>
  );

  const pasos = SECCIONES.map((s) => ({
    id: s.id,
    label: s.label,
    Icon: s.Icon,
    // El detalle es informativo: ningún paso es un trámite por cumplir, así que ninguno se marca
    // como "completo" —eso sugeriría un progreso que el OT no está recorriendo—.
    completo: false,
  }));

  const seccionActiva = SECCIONES.find((s) => s.id === seccion) ?? SECCIONES[0];

  return (
    <DetalleTramiteShell open onClose={onClose} title={titulo} header={header}>
      <div className="flex flex-col gap-3">
        {detailError ? (
          <p className="m-0 text-[11px]" style={{ color: "#B45309" }} role="alert">
            {detailError}
          </p>
        ) : null}

        <DetalleStepper pasos={pasos} pasoActivoId={seccion} onSelect={(id) => setSeccion(id as SeccionId)} />

        <div className="grid gap-4 md:grid-cols-12">
          <div className="md:col-span-4">
            <OtDetalleVehiculoSidebar procedure={row} />
          </div>

          <div
            className="md:col-span-8"
            role="tabpanel"
            id={`detalle-panel-${seccion}`}
            aria-labelledby={`detalle-tab-${seccion}`}
          >
            <div className="mb-3 flex items-center gap-2">
              <seccionActiva.Icon className="h-4 w-4 shrink-0" style={{ color: DETALLE_BLUE }} aria-hidden="true" />
              <h3 className="text-sm font-bold" style={{ color: DETALLE_BLUE }}>
                {seccionActiva.label}
              </h3>
            </div>

            {seccion === "vehiculo" ? (
              detailLoading ? (
                <SeccionCargando etiqueta="Cargando el detalle del trámite" filas={5} />
              ) : (
                <OtDetalleTramiteVehiculo procedure={row} totalDocumentos={docs.length} />
              )
            ) : null}

            {seccion === "actores" ? <OtDetalleActores procedure={row} /> : null}

            {seccion === "documentos" ? (
              <OtDocumentosTab
                procedureId={row.id}
                referenceNumber={row.referenceNumber}
                scope={scope}
                readOnly={readOnly}
              />
            ) : null}

            {seccion === "comercial" ? <OtDetalleComercial procedure={row} /> : null}
          </div>
        </div>
      </div>
    </DetalleTramiteShell>
  );
}
