"use client";

import { useEffect, useState, type ReactNode } from "react";
import { ChevronDown, FileText } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  fetchOtClientProcedure,
  fetchOtDocuments,
  type OtApiScope,
  type OtProcedureAttachment,
} from "@/lib/api/admin-ot";
import type { OtClientProcedure, OtClientProcedureActor } from "@/lib/api/types-ot";
import {
  plateFlowChipStyle,
  plateFlowLabel,
} from "@/lib/tramites/estados";
import { OtSidePanel } from "./OtSidePanel";
import { formatOtDate, formatOtProcedureStatus, procedureStatusTone } from "./ot-utils";

function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[6.5rem_1fr] gap-2 py-1.5">
      <dt className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </dt>
      <dd className="text-xs font-medium text-foreground break-words">{children ?? "—"}</dd>
    </div>
  );
}

function Section({
  title,
  defaultOpen = true,
  children,
  badge,
}: {
  title: string;
  defaultOpen?: boolean;
  children: ReactNode;
  badge?: string;
}) {
  return (
    <details
      open={defaultOpen}
      className="group rounded-xl border bg-card"
      style={{ borderColor: "#DFE5ED" }}
    >
      <summary className="flex cursor-pointer list-none items-center justify-between gap-2 px-3 py-2.5 text-xs font-bold text-foreground [&::-webkit-details-marker]:hidden">
        <span className="inline-flex items-center gap-2">
          {title}
          {badge ? (
            <span
              className="rounded-full px-1.5 py-0.5 text-[10px] font-semibold"
              style={{ background: "rgba(85,126,255,0.12)", color: "#557EFF" }}
            >
              {badge}
            </span>
          ) : null}
        </span>
        <ChevronDown className="h-3.5 w-3.5 shrink-0 opacity-60 transition group-open:rotate-180" />
      </summary>
      <div className="border-t px-3 pb-3 pt-1" style={{ borderColor: "#DFE5ED" }}>
        {children}
      </div>
    </details>
  );
}

function soatEstadoLabel(value: string | null | undefined): string {
  if (!value) return "—";
  if (value === "vigente") return "Vigente";
  if (value === "vencido") return "Vencido";
  if (value === "unknown") return "Desconocido";
  return value;
}

function actorTypeLabel(type: string): string {
  const map: Record<string, string> = {
    comprador: "Comprador",
    vendedor: "Vendedor",
    propietario: "Propietario",
    mandatario: "Mandatario",
    representante_legal: "Representante legal",
  };
  return map[type] ?? type;
}

function personTypeLabel(type: string | null | undefined): string {
  if (type === "natural") return "Natural";
  if (type === "juridical") return "Jurídica";
  return "—";
}

function documentTipoLabel(tipo: string): string {
  const map: Record<string, string> = {
    fur: "FUR",
    compraventa: "Compraventa",
    consolidado: "Consolidado",
    consolidado_maestro: "Consolidado maestro",
    impronta: "Impronta",
    licencia_transito: "Licencia de tránsito",
    certificado_identidad: "Certificado de identidad",
  };
  return map[tipo] ?? tipo;
}

function val(v: string | null | undefined): string {
  const t = v?.trim();
  return t ? t : "—";
}

export interface ClientProcedureDetailPanelProps {
  open: boolean;
  procedure: OtClientProcedure | null;
  onClose: () => void;
  scope?: OtApiScope;
  onVerDocumentos?: (row: OtClientProcedure) => void;
  onVerConsolidado?: (row: OtClientProcedure, force?: boolean) => void;
  consolidadoActing?: boolean;
}

/**
 * Panel lateral derecho — detalle completo del trámite OT (secciones desplegables).
 * Carga actores/vehículo vía GET detalle y documentos vía endpoint de expediente.
 */
export function ClientProcedureDetailPanel({
  open,
  procedure,
  onClose,
  scope,
  onVerDocumentos,
  onVerConsolidado,
  consolidadoActing = false,
}: ClientProcedureDetailPanelProps) {
  const [detail, setDetail] = useState<OtClientProcedure | null>(procedure);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [docs, setDocs] = useState<OtProcedureAttachment[]>([]);
  const [docsLoading, setDocsLoading] = useState(false);
  const [docsError, setDocsError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || !procedure) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- limpia el detalle al cerrar el panel
    setDetail(null);
      setDocs([]);
      setDetailError(null);
      setDocsError(null);
      return;
    }

    setDetail(procedure);
    setDetailLoading(true);
    setDetailError(null);
    setDocsLoading(true);
    setDocsError(null);

    const controller = new AbortController();
    void (async () => {
      try {
        const full = await fetchOtClientProcedure(procedure.id, controller.signal, scope);
        if (!controller.signal.aborted) setDetail(full);
      } catch {
        if (!controller.signal.aborted) {
          setDetailError("No se pudo refrescar el detalle; se muestran los datos de la bandeja.");
        }
      } finally {
        if (!controller.signal.aborted) setDetailLoading(false);
      }
    })();

    void (async () => {
      try {
        const res = await fetchOtDocuments(procedure.id, scope);
        if (!controller.signal.aborted) setDocs(res.data ?? []);
      } catch {
        if (!controller.signal.aborted) setDocsError("No se pudieron cargar los documentos.");
      } finally {
        if (!controller.signal.aborted) setDocsLoading(false);
      }
    })();

    return () => controller.abort();
  }, [open, procedure, scope]);

  const row = detail ?? procedure;
  if (!open || !row) return null;

  const plateChip = plateFlowChipStyle(row.plateFlowStatus);
  const actors: OtClientProcedureActor[] = row.actors ?? [];
  const canConsolidado =
    (row.status === "entregado" || row.status === "aprobado") && !!onVerConsolidado;

  const isTraspaso = (row.procedureTypeName ?? "").toLowerCase().includes("traspaso");
  const expectedActors = isTraspaso ? ["comprador", "vendedor"] : ["comprador"];
  const missingActors = expectedActors.filter(
    (t) => !actors.some((a) => a.actorType.toLowerCase() === t),
  );

  const vehicleFields = [
    { label: "Placa", value: row.placa },
    { label: "VIN", value: row.vin },
    { label: "Marca", value: row.marca },
    { label: "Línea", value: row.linea },
    { label: "Modelo", value: row.modelo },
    { label: "Color", value: row.color },
    { label: "Clase", value: row.clase },
    { label: "Servicio", value: row.servicio },
    { label: "Combustible", value: row.combustible },
  ];
  const missingVehicle = vehicleFields.filter((f) => !f.value?.trim()).map((f) => f.label);

  const pendingPlatePreasignada = row.plateFlowStatus === "preasignado";
  const pendingPlateAsignada = row.plateFlowStatus === "asignado";
  const pendingSoat = !!row.soatEstado && row.soatEstado !== "vigente";
  const showSinPendientesFallback =
    missingActors.length === 0 &&
    missingVehicle.length === 0 &&
    docs.length > 0 &&
    !pendingPlatePreasignada &&
    !pendingPlateAsignada;
  const hasObservaciones =
    pendingPlatePreasignada || pendingPlateAsignada || pendingSoat || showSinPendientesFallback;

  return (
    <OtSidePanel
      open
      title={`Detalle — ${row.referenceNumber}`}
      ariaLabel="Detalle del trámite de cliente"
      onClose={onClose}
      zClassName="z-[60]"
      width="lg"
      footer={
        <div className="flex flex-wrap gap-2">
          {onVerDocumentos ? (
            <button
              type="button"
              className="rounded-lg border px-3 py-2 text-xs font-semibold text-foreground"
              onClick={() => onVerDocumentos(row)}
            >
              Abrir documentos
            </button>
          ) : null}
          {canConsolidado ? (
            <button
              type="button"
              disabled={consolidadoActing}
              className="rounded-lg border px-3 py-2 text-xs font-semibold disabled:opacity-50 text-foreground"
              onClick={() => onVerConsolidado?.(row)}
            >
              {consolidadoActing ? "Abriendo…" : "Ver consolidado"}
            </button>
          ) : null}
          {/* Salida manual: reconstruye el PDF ignorando la marca de vigencia. El expediente se
              invalida solo cuando cambia, pero si el operador duda de lo que ve no debe quedarse
              sin forma de comprobarlo. */}
          {canConsolidado ? (
            <button
              type="button"
              disabled={consolidadoActing}
              className="rounded-lg border px-3 py-2 text-xs font-semibold disabled:opacity-50 text-muted-foreground"
              title="Reconstruye el expediente consolidado con el contenido actual del trámite"
              onClick={() => onVerConsolidado?.(row, true)}
            >
              Regenerar
            </button>
          ) : null}
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        {detailLoading ? (
          <p className="m-0 text-[11px] text-muted-foreground">Actualizando detalle…</p>
        ) : null}
        {detailError ? (
          <p className="m-0 text-[11px]" style={{ color: "#b45309" }}>
            {detailError}
          </p>
        ) : null}

        <Section title="Resumen" defaultOpen>
          <dl className="m-0">
            <DetailRow label="Radicado">{row.referenceNumber}</DetailRow>
            <DetailRow label="Tipo">{row.procedureTypeName ?? row.procedureTypeId}</DetailRow>
            <DetailRow label="Empresa">{row.clientTenantName ?? row.clientTenantId}</DetailRow>
            <DetailRow label="Estado">
              <div className="flex flex-wrap items-center gap-1.5">
                <StatusBadge
                  label={formatOtProcedureStatus(row.status)}
                  tone={procedureStatusTone(row.status)}
                />
                {plateChip ? (
                  <span
                    className="rounded-full px-2 py-0.5 text-[10px] font-semibold"
                    style={{
                      background: plateChip.bg,
                      color: plateChip.color,
                      border: `1px solid ${plateChip.border}`,
                    }}
                  >
                    {plateFlowLabel(row.plateFlowStatus)}
                  </span>
                ) : null}
                {row.prioritario ? (
                  <span
                    className="rounded-full px-2 py-0.5 text-[10px] font-semibold"
                    style={{
                      background: "rgba(245,158,11,0.15)",
                      color: "#b45309",
                      border: "1px solid rgba(245,158,11,0.35)",
                    }}
                  >
                    Prioritario
                  </span>
                ) : null}
              </div>
            </DetailRow>
            <DetailRow label="Radicación">{formatOtDate(row.createdAt)}</DetailRow>
            <DetailRow label="Entrega">
              {row.submittedAt ? formatOtDate(row.submittedAt) : "—"}
            </DetailRow>
            <DetailRow label="SOAT RUNT">{soatEstadoLabel(row.soatEstado)}</DetailRow>
            <DetailRow label="Checks">
              <span className="flex flex-wrap gap-1.5">
                <span
                  className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                    row.soatPagado
                      ? "bg-emerald-50 text-emerald-700"
                      : "bg-slate-100 text-slate-500"
                  }`}
                >
                  SOAT {row.soatPagado ? "pagado" : "pendiente"}
                </span>
                <span
                  className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                    row.impuestoDepartamentalPagado
                      ? "bg-sky-50 text-sky-700"
                      : "bg-slate-100 text-slate-500"
                  }`}
                >
                  Impuesto {row.impuestoDepartamentalPagado ? "pagado" : "pendiente"}
                </span>
              </span>
            </DetailRow>
            <DetailRow label="Dígito placa">{val(row.platePreferredLastDigit)}</DetailRow>
          </dl>
        </Section>

        <Section
          title="Actores"
          badge={actors.length ? String(actors.length) : undefined}
          defaultOpen
        >
          {actors.length === 0 ? (
            <p className="m-0 text-xs text-muted-foreground">Sin actores registrados.</p>
          ) : (
            <ul className="m-0 flex list-none flex-col gap-2 p-0">
              {actors.map((a, idx) => (
                <li
                  key={`${a.actorType}-${a.documentNumber}-${idx}`}
                  className="rounded-lg border px-2.5 py-2"
                  style={{ borderColor: "#DFE5ED" }}
                >
                  <p className="m-0 text-[11px] font-bold" style={{ color: "#557EFF" }}>
                    {actorTypeLabel(a.actorType)}
                  </p>
                  <dl className="m-0 mt-1">
                    <DetailRow label="Nombre">{val(a.fullName)}</DetailRow>
                    <DetailRow label="Documento">
                      {a.documentType ? `${a.documentType} ` : ""}
                      {val(a.documentNumber)}
                    </DetailRow>
                    <DetailRow label="Persona">{personTypeLabel(a.personType)}</DetailRow>
                    <DetailRow label="Email">{val(a.email)}</DetailRow>
                    <DetailRow label="Teléfono">{val(a.phone)}</DetailRow>
                  </dl>
                </li>
              ))}
            </ul>
          )}
          {missingActors.length > 0 ? (
            <p className="mb-0 mt-2 text-[11px]" style={{ color: "#b45309" }}>
              Faltan actores esperados: {missingActors.map(actorTypeLabel).join(", ")}.
            </p>
          ) : null}
        </Section>

        <Section title="Vehículo" defaultOpen>
          <dl className="m-0">
            {vehicleFields.map((f) => (
              <DetailRow key={f.label} label={f.label}>
                {val(f.value)}
              </DetailRow>
            ))}
          </dl>
          {missingVehicle.length > 0 ? (
            <p className="mb-0 mt-2 text-[11px]" style={{ color: "#b45309" }}>
              Datos de vehículo sin capturar: {missingVehicle.join(", ")}.
            </p>
          ) : null}
        </Section>

        <Section
          title="Documentos"
          badge={docsLoading ? "…" : String(docs.length)}
          defaultOpen
        >
          {docsLoading ? (
            <p className="m-0 text-xs text-muted-foreground">Cargando documentos…</p>
          ) : docsError ? (
            <p className="m-0 text-xs" style={{ color: "#b45309" }}>
              {docsError}
            </p>
          ) : docs.length === 0 ? (
            <p className="m-0 text-xs text-muted-foreground">
              Aún no hay documentos en el expediente.
            </p>
          ) : (
            <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
              {docs.map((d) => (
                <li
                  key={d.id}
                  className="flex items-start gap-2 rounded-lg border px-2.5 py-2"
                  style={{ borderColor: "#DFE5ED" }}
                >
                  <FileText className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: "#557EFF" }} />
                  <div className="min-w-0 flex-1">
                    <p className="m-0 truncate text-xs font-semibold">{d.filename}</p>
                    <p className="m-0 text-[10px] text-muted-foreground">
                      {documentTipoLabel(d.tipo)}
                      {d.sizeBytes
                        ? ` · ${d.sizeBytes < 1024 * 1024 ? `${Math.round(d.sizeBytes / 1024)} KB` : `${(d.sizeBytes / (1024 * 1024)).toFixed(1)} MB`}`
                        : ""}
                    </p>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Section>

        {hasObservaciones ? (
          <Section title="Observaciones / pendientes" defaultOpen={false}>
            <ul className="m-0 list-disc space-y-1 pl-4 text-xs text-foreground">
              {pendingPlatePreasignada ? <li>Pendiente asignar placa por el OT.</li> : null}
              {pendingPlateAsignada ? (
                <li>Pendiente proceso del gestor (Asignado → Terminado) antes de decidir.</li>
              ) : null}
              {pendingSoat ? (
                <li>SOAT RUNT no vigente ({soatEstadoLabel(row.soatEstado)}).</li>
              ) : null}
              {showSinPendientesFallback ? (
                <li className="text-muted-foreground">Sin pendientes destacados en este resumen.</li>
              ) : null}
            </ul>
          </Section>
        ) : null}
      </div>
    </OtSidePanel>
  );
}
