"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@flit/ui/Button";
import { ICONO_TONO, rutaKit } from "@flit/ui/iconos-mapas";
import type { StatusBadgeTone } from "@flit/ui/StatusBadge";
import {
  ETIQUETA_ACCION_APROVISIONAMIENTO,
  ETIQUETA_ESTADO_APROVISIONAMIENTO,
  ETIQUETA_FIRMA_SIN_CONFIRMAR,
  retryTenantProductProvisioning,
  type ProvisioningStatus,
  type TenantProductProvisioning,
} from "@/lib/api/platform";
import { StatusBadge } from "@/components/atom/StatusBadge";

// HU #14695 · M19 (plan §3.1 y §1.2 R1; platform.v1.yaml 1.1.0; spec-visual §3.11) — estado de la conexión de la
// compañía con el proveedor de Impuestos, bajo el interruptor del producto. Mismo contenedor que
// CredencialImpuestoDiagnostico. Etiquetas y acciones salen del contrato (`x-flit-etiquetas`); el motivo es `message`,
// que ya pone el servicio. Nunca solo color: chip con ícono y texto.

const PRODUCT_CODE = "impuestos";
const SIN_CLASIFICAR = "Sin clasificar";

/** Tono por estado (spec-visual §1.5; PENDIENTE en `info` por decisión del orquestador). */
const TONO: Record<ProvisioningStatus, StatusBadgeTone> = {
  PENDIENTE: "info",
  LISTO: "success",
  LISTO_SIN_WEBHOOK: "success",
  ERROR: "danger",
  INCIERTO: "purple",
  REQUIERE_CREDENCIALES: "warning",
  REQUIERE_REVISION_NIT: "warning",
  SUSPENDIDO: "neutral",
};

const ICONO_POR_TONO: Record<StatusBadgeTone, string> = {
  success: ICONO_TONO.ok,
  info: ICONO_TONO.info,
  warning: ICONO_TONO.warn,
  purple: ICONO_TONO.warn,
  danger: ICONO_TONO.danger,
  neutral: ICONO_TONO[""],
  tinta: ICONO_TONO[""],
};

/** Etiqueta visible del estado; un código que no está en el contrato dice «Sin clasificar». */
export function etiquetaEstadoAprovisionamiento(p: TenantProductProvisioning | null): string {
  if (!p) return "Sin estado todavía";
  return Object.hasOwn(ETIQUETA_ESTADO_APROVISIONAMIENTO, p.status) ? ETIQUETA_ESTADO_APROVISIONAMIENTO[p.status] : SIN_CLASIFICAR;
}

function tonoDe(p: TenantProductProvisioning | null): StatusBadgeTone {
  if (!p || !Object.hasOwn(TONO, p.status)) return "neutral";
  return TONO[p.status];
}

/** `{url de Impuestos}/backoffice/empresas/{tenantId}` (contrato `ProvisioningAction`). */
function rutaBackoffice(impuestosUrl: string, tenantId: string): string {
  return `${impuestosUrl.replace(/\/+$/, "")}/backoffice/empresas/${encodeURIComponent(tenantId)}`;
}

export interface EstadoAprovisionamientoProps {
  tenantId: string;
  provisioning: TenantProductProvisioning | null;
  /** `url` del producto `impuestos` en GET /platform/me/apps. `null` si el usuario no lo tiene: no se pinta enlace. */
  impuestosUrl: string | null;
  /** Abre «Editar compañía» para corregir el NIT. Sin ella se indica dónde corregirlo. */
  onEditarCompania?: () => void;
  /** Tras pedir el reintento con éxito, para releer los productos. */
  onReintentado: () => Promise<void> | void;
}

export function EstadoAprovisionamiento({ tenantId, provisioning, impuestosUrl, onEditarCompania, onReintentado }: EstadoAprovisionamientoProps) {
  const [reintentando, setReintentando] = useState(false);
  const [errorReintento, setErrorReintento] = useState(false);

  const etiqueta = etiquetaEstadoAprovisionamiento(provisioning);
  const tono = tonoDe(provisioning);
  const accion = provisioning?.action ?? null;

  const reintentar = async () => {
    setErrorReintento(false);
    setReintentando(true);
    try {
      await retryTenantProductProvisioning(tenantId, PRODUCT_CODE);
      await onReintentado();
    } catch {
      setErrorReintento(true);
    } finally {
      setReintentando(false);
    }
  };

  const enlaceBackoffice = (texto: string) =>
    impuestosUrl ? (
      <a
        href={rutaBackoffice(impuestosUrl, tenantId)}
        target="_blank"
        rel="noopener"
        className="font-semibold text-[color:var(--di-action)] underline underline-offset-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {texto}
        <span className="sr-only">(se abre en otra pestaña)</span>
      </a>
    ) : (
      <span>Hazlo desde Impuestos › Backoffice</span>
    );

  let accionUi: ReactNode = null;
  if (accion === "REINTENTAR") {
    accionUi = (
      <Button variant="secondary" size="sm" cargando={reintentando} onClick={() => void reintentar()}>
        {ETIQUETA_ACCION_APROVISIONAMIENTO.REINTENTAR}
      </Button>
    );
  } else if (accion === "CARGAR_CREDENCIALES" || accion === "CARGAR_SECRETO") {
    accionUi = enlaceBackoffice(ETIQUETA_ACCION_APROVISIONAMIENTO[accion]);
  } else if (accion === "CORREGIR_NIT") {
    accionUi = onEditarCompania ? (
      <Button variant="secondary" size="sm" onClick={onEditarCompania}>
        {ETIQUETA_ACCION_APROVISIONAMIENTO.CORREGIR_NIT}
      </Button>
    ) : (
      <span>Corrige el NIT con «Editar compañía».</span>
    );
  }

  return (
    <div role="group" aria-label="Conexión con el proveedor de Impuestos" className="ml-12 space-y-2 rounded-xl border px-4 py-3 text-xs">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold">Conexión con el proveedor:</span>
        <StatusBadge tone={tono} icono={rutaKit(ICONO_POR_TONO[tono])} ariaLabel={`Conexión con el proveedor: ${etiqueta}`} label={etiqueta} />
        {provisioning?.status === "LISTO" && provisioning.signatureConfirmed === false && (
          <StatusBadge
            tone="purple"
            icono={rutaKit(ICONO_POR_TONO.purple)}
            ariaLabel={`Conexión con el proveedor: ${ETIQUETA_FIRMA_SIN_CONFIRMAR}`}
            label={ETIQUETA_FIRMA_SIN_CONFIRMAR}
          />
        )}
        {accionUi && <div className="ml-auto flex items-center gap-2">{accionUi}</div>}
      </div>
      {provisioning === null ? (
        <p className="opacity-70">Aparece en unos segundos tras encender Impuestos.</p>
      ) : (
        provisioning.message && <p className="opacity-70">{provisioning.message}</p>
      )}
      {errorReintento && (
        <p role="alert" className="font-medium" style={{ color: "var(--destructive)" }}>
          No se pudo pedir el reintento. Intenta de nuevo.
        </p>
      )}
    </div>
  );
}
