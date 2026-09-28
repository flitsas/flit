import type { ComponentType, CSSProperties } from "react";
import {
  FileStack,
  ShieldCheck,
  BarChart3,
  Users,
  Lock,
  Radar,
  FolderCog,
} from "lucide-react";
import { OT_ADM_DOCK } from "@/components/admin/transit-offices/ot-nav";
import { COPY } from "@/lib/copy/copy-catalog";

/**
 * Secciones del dock de Trámites y a cuál va cada entrada. El catálogo (`tramitesNav.ts`) las usa; el dock lo dibuja
 * @flit/shell (B-13).
 *
 * Agrupadores del dock — orden estable de las píldoras.
 * Trámites e Identidad son grupos de un solo ítem → píldora directa (no submenú).
 * Dashboard no se lista: el FAB central abre Inicio.
 * `administracion`: Admin OT (pestañas hub → dock).
 * SuperAdmin: Compañías/Tránsito/Documental/Improntas/Quipux/RBAC/Auditoría y el
 * submenú anidado Plataforma (Mandatos, …) viven en `administradores`.
 * `integraciones` = Log QX + ICT (que a su vez anida Log ICT y Reportes ICT).
 * AdminCompany: la píldora "Administración" también cae en `administradores` (ítem único →
 * label del ítem, no del grupo).
 * HU #12850 (Feature #12846) — el grupo `preasignacion` se retiró: la consola de Preasignación
 * de rango dejó de existir (backend HU-A1 la deprecia; la asignación de placa del trámite ya
 * siempre reserva fuera de rango).
 */
export const DOCK_GROUP_ORDER = [
  "tramites",
  "identidad",
  "reportes",
  "usuarios",
  "administracion",
  "administradores",
  "integraciones",
] as const;

export type DockGroupId = (typeof DOCK_GROUP_ORDER)[number];

export type DockGroupSide = "left" | "right";

/** Reparto izquierda/derecha del FAB — declarado, no por mitades (HU #12723). */
export const DOCK_GROUP_SIDE: Record<DockGroupId, DockGroupSide> = {
  tramites: "left",
  identidad: "left",
  reportes: "left",
  usuarios: "right",
  administracion: "right",
  administradores: "right",
  integraciones: "right",
};

export const DOCK_GROUP_LABEL: Record<DockGroupId, string> = {
  tramites: COPY.B21Tramites,
  identidad: COPY.A17,
  reportes: COPY.B21Reportes,
  usuarios: COPY.B21Usuarios,
  administracion: "Administración",
  administradores: "Administradores",
  integraciones: "Integraciones",
};

/** Labels de las píldoras SPA (HU #12699 / A17 + B21). El id de Identidad sigue siendo `validaciones`. */
export const SPA_DOCK_ITEM_LABEL = {
  tramites: COPY.B21Tramites,
  reportes: COPY.B21Reportes,
  "reportes-detallados": "Reportes Detallados",
  validaciones: COPY.A17,
  "historial-placa": "Historial por placa",
  usuarios: COPY.B21Usuarios,
  ayuda: COPY.B21Ayuda,
} as const;

/**
 * Firma mínima de los iconos del dock (lucide-react). `aria-hidden` admite también la forma
 * string porque así se escribe en JSX (`aria-hidden="true"`).
 */
export type DockIconComponent = ComponentType<{
  className?: string;
  style?: CSSProperties;
  strokeWidth?: number;
  "aria-hidden"?: boolean | "true" | "false";
}>;

export const DOCK_GROUP_ICON: Record<DockGroupId, DockIconComponent> = {
  tramites: FileStack,
  identidad: ShieldCheck,
  reportes: BarChart3,
  usuarios: Users,
  administracion: FolderCog,
  administradores: Lock,
  integraciones: Radar,
};

/** Mapeo entrada del dock → agrupador (por key estable). */
export const DOCK_ITEM_GROUP: Record<string, DockGroupId> = {
  tramites: "tramites",
  // Módulo id sigue siendo `validaciones` en la SPA; el label visible es "Identidad".
  validaciones: "identidad",
  // HU #12194 — el historial por placa es una lectura del universo de trámites: cuelga de Trámites.
  "historial-placa": "tramites",
  reportes: "reportes",
  "reportes-detallados": "reportes",
  usuarios: "usuarios",
  // SuperAdmin + AdminCompany ("Administración"): consola / listado de compañías
  "admin-companies": "administradores",
  "mi-empresa": "administradores",
  // B-13 — «Red de clientes» (AdminCompany cabeza de grupo) no tenía grupo y el dock la descartaba en silencio.
  "admin-network": "administradores",
  "admin-documents": "administradores",
  "admin-improntas": "administradores",
  // Generación documental (Feature #12201): visible por módulo accesible, no por rol.
  "admin-generacion-documental": "administradores",
  "admin-quipux": "administradores",
  "admin-jobs": "administradores",
  // Tránsito anida Organismos y Causales de rechazo; solo el padre necesita grupo.
  "admin-transit": "administradores",
  // SuperAdmin — Plataforma anidada dentro de Administradores
  "admin-plataforma": "administradores",
  // Admin OT — pestañas hub en el dock
  // HU #12856 (Feature #12847) — Reglas/Requisitos/Configuración salieron de este mapa: ya no
  // viven en el dock de ningún usuario de tenant OT (admin u operador), solo en la barra de
  // pestañas del hub de Super Admin (OtHubLayout, que no consulta este mapa). El grupo
  // `administracion` sigue vivo porque Documentos, Mandatos y Validar impronta permanecen.
  [OT_ADM_DOCK.tramites]: "tramites",
  [OT_ADM_DOCK.documents]: "administracion",
  [OT_ADM_DOCK.usuarios]: "usuarios",
  [OT_ADM_DOCK.reportes]: "reportes",
  [OT_ADM_DOCK.mandatos]: "administracion",
  [OT_ADM_DOCK.imprintValidation]: "administracion",
  rbac: "administradores",
  auditoria: "administradores",
  "log-qx": "integraciones",
  // ICT anida Log ICT y Reportes ICT; solo el padre necesita grupo (mismo patrón que Tránsito).
  ict: "integraciones",
};
