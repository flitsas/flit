"use client";

import { useEffect, useId, useRef, useState } from "react";
import { AlertTriangle, X } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  fetchOtClientProcedure,
  fetchOtDocuments,
  type OtApiScope,
  type OtProcedureAttachment,
} from "@/lib/api/admin-ot";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { puedeDecidirOt } from "@/lib/tramites/estados";
import { OtDetalleAcordeon } from "./detalle/OtDetalleAcordeon";
import { OtDetalleActores } from "./detalle/OtDetalleActores";
import { OtDetalleShell } from "./detalle/OtDetalleShell";
import { OtDetalleTramiteVehiculo } from "./detalle/OtDetalleTramiteVehiculo";
import { OtDetalleDocumentos } from "./detalle/OtDetalleDocumentos";
import { OtCargando } from "./detalle/OtDetallePrimitivos";
import { pendientesDelTramite } from "./detalle/ot-detalle-pendientes";
import {
  OT_APROBAR_GRADIENTE,
  OT_BLUE,
  OT_BORDER,
  OT_NAVY,
  OT_ORANGE,
  OT_WARN,
  OT_WARN_TEXT,
} from "./detalle/ot-detalle-visual";
import { formatOtProcedureStatus, procedureStatusTone } from "./ot-utils";

/**
 * Bloques del detalle. Ya no son pasos de un recorrido sino acordeones independientes; el nombre
 * sigue sirviendo para decir con cuál abre el modal según por dónde se entró desde la bandeja.
 */
export type OtDetalleSeccionId = "vehiculo" | "actores" | "documentos";

const SECCIONES: { id: OtDetalleSeccionId; titulo: string }[] = [
  { id: "vehiculo", titulo: "Detalles del trámite y vehículo" },
  { id: "actores", titulo: "Actores del Trámite" },
  { id: "documentos", titulo: "Documentos del Trámite" },
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
   * Acordeón que abre desplegado. La bandeja tiene dos entradas al mismo trámite —«ver detalle» y
   * «ver documentos»— y ambas llevan a este modal: la segunda simplemente abre el suyo.
   */
  initialSection?: OtDetalleSeccionId;
  /**
   * Decisión del organismo desde el pie del modal (HU #12062). Son LOS MISMOS manejadores que ya
   * recibe la tabla de la bandeja: el modal es hermano suyo dentro de `ClientProceduresSection`, así
   * que aprobar desde aquí abre exactamente el mismo diálogo que aprobar desde la fila. No hay una
   * segunda vía de aprobación ni reglas duplicadas.
   */
  onApprove?: (procedure: OtClientProcedure) => void;
  onReject?: (procedure: OtClientProcedure) => void;
  /** Feature #10587 — asignar placa a un trámite en preasignado; desbloquea la decisión. */
  onAssignPlate?: (procedure: OtClientProcedure) => void;
  /** A false —solo lectura o SuperAdmin supervisando— el pie de decisión no se pinta. */
  showApprovalActions?: boolean;
}

/**
 * Modal de detalle del trámite en la bandeja del OT (HU #11930, rediseñado en el Feature #12059).
 *
 * Sigue el prototipo aprobado (`flit-2.0/src/components/atom/ot/OTDetalleModal.tsx`): hoja de 900px
 * con encabezado y pie fijos y un cuerpo de TRES acordeones independientes —trámite y vehículo,
 * actores, documentos—.
 *
 * Lo que se fue frente a la versión anterior: la navegación por pasos, la tarjeta lateral del
 * vehículo y la sección «Datos comerciales» (fuera por decisión de producto: el organismo no decide
 * sobre el precio de la operación).
 *
 * Ya NO comparte nada con `components/operacion/detalle/**`. El detalle del gestor sigue su propio
 * camino y este el del prototipo; con módulos comunes, tocar uno movía el otro.
 */
export function ClientProcedureDetailModal({
  open,
  procedure,
  onClose,
  scope,
  readOnly = false,
  initialSection = "vehiculo",
  onApprove,
  onReject,
  onAssignPlate,
  showApprovalActions = false,
}: ClientProcedureDetailModalProps) {
  /** La banda de aviso explica por qué el pie está deshabilitado; los botones la referencian. */
  const avisosId = useId();
  const [detail, setDetail] = useState<OtClientProcedure | null>(procedure);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [docs, setDocs] = useState<OtProcedureAttachment[]>([]);
  const [abiertos, setAbiertos] = useState<Record<OtDetalleSeccionId, boolean>>({
    vehiculo: false,
    actores: false,
    documentos: false,
  });

  /**
   * Trámite cuyo estado inicial ya se sembró. Distingue «abrir otro trámite» de «el mismo trámite
   * cambió»: al asignarle placa desde el pie, la bandeja rehace el objeto y este efecto se repite;
   * recolocar entonces los acordeones cerraría lo que el operador tenía abierto.
   */
  const sembradoRef = useRef<string | null>(null);

  useEffect(() => {
    if (!open || !procedure) {
      sembradoRef.current = null;
      return;
    }

    const controller = new AbortController();

    // El setState va dentro de la función async, no en el cuerpo síncrono del efecto:
    // react-hooks/set-state-in-effect tumba el lint en cuanto se hace al revés.
    const load = async () => {
      // La fila manda mientras llega la red: si acaba de asignarse la placa, esa es la buena.
      setDetail(procedure);
      if (sembradoRef.current !== procedure.id) {
        sembradoRef.current = procedure.id;
        // Se despliega el acordeón por el que se entró y solo ese: los otros dos se abren a mano.
        setAbiertos({
          vehiculo: initialSection === "vehiculo",
          actores: initialSection === "actores",
          documentos: initialSection === "documentos",
        });
        setDocs([]);
      }
      setDetailLoading(true);
      setDetailError(null);

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

  const alternar = (id: OtDetalleSeccionId) =>
    setAbiertos((prev) => ({ ...prev, [id]: !prev[id] }));

  /**
   * Un trámite fuera de `entregado` ya fue resuelto (o nunca llegó): el organismo no decide sobre
   * él. Es un bloqueo distinto de los pendientes —que sí se pueden subsanar— y por eso se nombra
   * aparte antes de mezclarlo con ellos en la banda de aviso.
   */
  const bloqueo =
    row.status === "entregado"
      ? null
      : `El trámite está en «${formatOtProcedureStatus(row.status)}»: el organismo solo decide sobre los que tiene entregados.`;

  const decidible = bloqueo === null && puedeDecidirOt(row.plateFlowStatus, row.soatEstado);
  const puedeAsignarPlaca =
    showApprovalActions && Boolean(onAssignPlate) && row.plateFlowStatus === "preasignado";
  const hayPie = showApprovalActions && Boolean(onApprove || onReject);

  const avisos = [bloqueo, ...pendientesDelTramite(row, docs.length)].filter(
    (a): a is string => a !== null,
  );
  const titulo = "Gestión y Aprobación del Trámite";

  const header = ({ titleId }: { titleId: string }) => (
    <div className="flex flex-wrap items-start justify-between gap-3 px-1 py-1">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <h2
            id={titleId}
            className="text-[22px] font-bold leading-tight"
            style={{ color: OT_BLUE }}
          >
            {titulo}
          </h2>
          {/* El prototipo asume un trámite pendiente; la bandeja también lista aprobados y
              rechazados, y sin el sello no habría forma de distinguirlos dentro del modal. */}
          <StatusBadge
            label={formatOtProcedureStatus(row.status)}
            tone={procedureStatusTone(row.status)}
          />
        </div>
        <p className="mt-1 text-[12px] text-slate-600 dark:text-white/60">
          Radicado: <span className="font-mono">{row.referenceNumber}</span> · Tipo de trámite:{" "}
          {row.procedureTypeName ?? row.procedureTypeId} · Placa: {row.placa?.trim() || "—"} · VIN:{" "}
          {row.vin?.trim() || "—"}
        </p>
      </div>
      <button
        type="button"
        onClick={onClose}
        aria-label="Cerrar"
        className="rounded-xl border bg-white p-2 dark:border-white/5 dark:bg-[#0B0F14]"
        style={{ borderColor: OT_BORDER, color: OT_NAVY }}
      >
        <X className="h-4 w-4" aria-hidden="true" />
      </button>

      {/* Banda de aviso del prototipo. Los pendientes (Bug #11585) viven AQUÍ y no dentro de un
          acordeón: son la razón por la que el trámite todavía no se puede resolver, y plegados
          dejarían de avisar justo cuando hacen falta. */}
      {avisos.length > 0 ? (
        <div
          className="mt-3 flex w-full items-start gap-2 rounded-xl px-3 py-2.5"
          style={{ background: `${OT_WARN}1A`, border: `1px solid ${OT_WARN}55` }}
          role="status"
          id={avisosId}
        >
          <AlertTriangle
            className="mt-0.5 h-4 w-4 shrink-0"
            style={{ color: OT_WARN }}
            aria-hidden="true"
          />
          <div className="text-[11.5px] font-medium" style={{ color: OT_WARN_TEXT }}>
            <strong>Pendientes antes de decidir:</strong>
            <ul className="mt-1 list-disc space-y-0.5 pl-4">
              {avisos.map((aviso) => (
                <li key={aviso}>{aviso}</li>
              ))}
            </ul>
          </div>
        </div>
      ) : null}
    </div>
  );

  const contenido = (id: OtDetalleSeccionId) => {
    if (id === "vehiculo") {
      return detailLoading ? (
        <OtCargando etiqueta="Cargando el detalle del trámite" filas={5} />
      ) : (
        <OtDetalleTramiteVehiculo
          procedure={row}
          onAssignPlate={puedeAsignarPlaca ? onAssignPlate : undefined}
        />
      );
    }
    if (id === "actores") {
      return <OtDetalleActores procedure={row} />;
    }
    return (
      <OtDetalleDocumentos procedureId={row.id} scope={scope} readOnly={readOnly} />
    );
  };

  /**
   * Pie de decisión del prototipo. NO ejecuta la decisión: delega en los manejadores que ya usaba la
   * bandeja, que son los que abren el diálogo de motivos de rechazo o el de adjuntar la licencia de
   * tránsito. Esos diálogos viven en `ClientProceduresSection` y se pintan POR ENCIMA de este modal.
   */
  const footer = hayPie ? (
    <div className="flex flex-wrap items-center justify-center gap-3">
      {onReject ? (
        <button
          type="button"
          onClick={() => onReject(row)}
          disabled={!decidible}
          aria-describedby={!decidible && avisos.length > 0 ? avisosId : undefined}
          className="h-11 min-w-[200px] rounded-xl px-6 text-[13px] font-semibold text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-40"
          style={{ background: OT_ORANGE }}
        >
          Rechazar trámite
        </button>
      ) : null}
      {onApprove ? (
        <button
          type="button"
          onClick={() => onApprove(row)}
          disabled={!decidible}
          aria-describedby={!decidible && avisos.length > 0 ? avisosId : undefined}
          className={`h-11 min-w-[200px] rounded-xl px-6 text-[13px] font-semibold text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-40 ${OT_APROBAR_GRADIENTE}`}
        >
          Aprobar trámite
        </button>
      ) : null}
    </div>
  ) : undefined;

  return (
    <OtDetalleShell open onClose={onClose} title={titulo} header={header} footer={footer}>
      {detailError ? (
        <p className="m-0 text-[11px]" style={{ color: "#B45309" }} role="alert">
          {detailError}
        </p>
      ) : null}

      {SECCIONES.map((s) => (
        <OtDetalleAcordeon
          key={s.id}
          titulo={s.titulo}
          abierto={abiertos[s.id]}
          onToggle={() => alternar(s.id)}
        >
          {contenido(s.id)}
        </OtDetalleAcordeon>
      ))}
    </OtDetalleShell>
  );
}
