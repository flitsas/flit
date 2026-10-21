// Catálogo de navegación de Trámites para @flit/shell (B-13, HU #12989; guía del dock, Contrato A). Dice QUÉ existe y
// QUIÉN lo ve; el dock de la suite lo filtra y lo dibuja. Las reglas son las del Shell anterior, entrada por entrada:
// - módulos de la SPA (`/?m=…`): por módulo RBAC accesible (`/api/v1/security/modules`);
// - administración de plataforma (incluye Banners, HU #13439): solo SuperAdmin;
// - Confirmación RUNT, LOG QX, Trazabilidad ICT y Reportes ICT: por permiso del token (el SuperAdmin pasa
//   siempre); Log ICT es solo del SuperAdmin (Bug #13445);
// - las entradas que dependen del tipo de empresa (organismo de tránsito, cabeza de grupo, AdminCompany) solo se
//   declaran para quien corresponde, porque el SuperAdmin las vería todas.
import {
  BadgeCheck,
  BarChart3,
  Bell,
  BrainCircuit,
  MailWarning,
  Building2,
  ClipboardList,
  FileSignature,
  FileStack,
  FileArchive,
  FileText,
  FolderCog,
  Fingerprint,
  History,
  Image as ImageIcon,
  Landmark,
  ListChecks,
  Lock,
  Monitor,
  Network,
  Radar,
  Route,
  ScrollText,
  Send,
  ShieldCheck,
  Timer,
  Users,
} from "lucide-react";
import type { NavCatalog, NavIcon, NavItem } from "@flit/shell/nav";
import { OT_ADM_DOCK, otHubListPath, otHubModulePath } from "@/components/admin/transit-offices/ot-nav";
import { CONFIRMACION_RUNT_BASE_PATH } from "@/components/admin/plataforma/confirmacion-runt/confirmacion-runt-nav";
import { GENERACION_DOCUMENTAL_BASE_PATH } from "@/components/admin/generacion-documental/generacion-documental-nav";
import { OT_ADMIN_SPA_OMIT } from "@/lib/nav/modules";
import { navGatingFor, otHubNavGating, type OtHubDelegableTab } from "@/lib/nav/module-access";
import { COPY } from "@/lib/copy/copy-catalog";
import {
  DOCK_GROUP_ICON,
  DOCK_GROUP_LABEL,
  DOCK_GROUP_ORDER,
  DOCK_GROUP_SIDE,
  DOCK_ITEM_GROUP,
  SPA_DOCK_ITEM_LABEL,
} from "./dockGroups";

/** Lo que el token no dice por sí solo y cambia qué entradas existen. */
export interface TramitesNavContext {
  /** Cualquier rol de un organismo de tránsito: opera la superficie del organismo. */
  isOtUser: boolean;
  /** `ot_admin`: ve todas las pestañas del organismo; los demás roles OT, solo las de sus permisos OT (HU #13655). */
  isOtAdmin: boolean;
  isAdminCompany: boolean;
  isGroupParent: boolean;
  tenantId: string | null;
  /** Organismo del usuario OT. Sin él, las entradas del organismo no se declaran (aún no hay a dónde llevarlas). */
  otTransitOfficeId: string | null;
}

type Entry = Omit<NavItem, "section" | "children"> & { children?: Entry[] };

/** Los hijos van en la sección del padre; solo el padre necesita grupo en DOCK_ITEM_GROUP. */
function withSection(entry: Entry, section: string): NavItem {
  return { ...entry, section, children: entry.children?.map((c) => withSection(c, section)) };
}

function inSection(entry: Entry): NavItem {
  const section = DOCK_ITEM_GROUP[entry.key];
  if (!section) throw new Error(`La entrada «${entry.key}» del dock no tiene grupo en DOCK_ITEM_GROUP.`);
  return withSection(entry, section);
}

const spa = (id: keyof typeof SPA_DOCK_ITEM_LABEL, icon: NavIcon, href = `/?m=${id}`): Entry => ({
  key: id,
  label: SPA_DOCK_ITEM_LABEL[id],
  href,
  icon,
  // Quién lo ve sale de la tabla de acceso (ADR-0072), no de una regla propia del dock.
  ...navGatingFor(id),
});

export function tramitesNav(ctx: TramitesNavContext): NavCatalog {
  const entries: Entry[] = [];

  // Dashboard no va en el dock: el botón central (Inicio) abre el mismo módulo. Trámites vive en su ruta.
  // Usuario OT: las pestañas de su organismo reemplazan a los módulos homónimos (invariante dock ≡ URL).
  const spaEntries = [
    spa("tramites", FileStack, "/tramites"),
    spa("reportes", BarChart3),
    spa("validaciones", ShieldCheck),
    // HU #12194 — el id coincide con el `code` del módulo RBAC `historial-placa`.
    spa("historial-placa", History),
    spa("usuarios", Users),
  ];
  entries.push(...spaEntries.filter((e) => !(ctx.isOtUser && OT_ADMIN_SPA_OMIT.has(e.key))));

  const superAdmin = (key: string, label: string, href: string, icon: NavIcon): Entry => ({ key, label, href, icon, superAdminOnly: true });
  entries.push(
    superAdmin("admin-companies", "Compañías", "/admin/companies", Building2),
    {
      // Tránsito es contenedor: el catálogo de causales alimenta el modal de rechazo del organismo.
      key: "admin-transit",
      label: "Tránsito",
      href: "",
      icon: Landmark,
      superAdminOnly: true,
      children: [
        superAdmin("admin-transit-offices", "Organismos", otHubListPath(), Landmark),
        superAdmin("admin-rejection-reasons", "Causales de rechazo", "/admin/causales-rechazo", ClipboardList),
      ],
    },
    superAdmin("admin-documents", "Documental", "/admin/documents", FolderCog),
    superAdmin("admin-improntas", "Improntas", "/admin/improntas", Fingerprint),
    superAdmin("admin-quipux", "Quipux", "/admin/quipux", Send),
    superAdmin("admin-jobs", "Procesos periódicos", "/admin/jobs", Timer),
    { key: "rbac", label: "RBAC Admin", href: "/?m=rbac", icon: Lock, ...navGatingFor("rbac") },
    { key: "auditoria", label: "Auditoría", href: "/?m=auditoria", icon: ScrollText, ...navGatingFor("auditoria") },
    {
      // Plataforma es contenedor: lo núcleo es del SuperAdmin; Confirmación RUNT (HU #12313, justo después de Tipos de
      // trámites) va por permiso, así que un rol con solo ese permiso ve Plataforma con esa entrada. Banners es solo SuperAdmin.
      key: "admin-plataforma",
      label: "Plataforma",
      href: "",
      icon: Monitor,
      children: [
        superAdmin("admin-tipos-tramite", "Tipos de trámites", "/admin/plataforma/tipos-tramite", ListChecks),
        {
          key: "admin-confirmacion-runt",
          label: "Confirmación RUNT",
          href: CONFIRMACION_RUNT_BASE_PATH,
          icon: BadgeCheck,
          ...navGatingFor("admin-confirmacion-runt"),
        },
        superAdmin("admin-mandatos", "Mandatos", "/admin/plataforma/mandatos", FileSignature),
        superAdmin("admin-fur", "FUR", "/admin/plataforma/fur", FileText),
        superAdmin("admin-notificaciones", "Notificaciones", "/admin/plataforma/notificaciones", Bell),
        // Feature #13508 / HU #13511 — multi-proveedor OCR y DR. FLIT (solo SuperAdmin).
        superAdmin("admin-configuracion-ia", "Configuración de IA", "/admin/plataforma/configuracion-ia", BrainCircuit),
        // Epic #13316 (HU #13358): correos y webhooks que agotaron sus reintentos.
        superAdmin("admin-mensajes-muertos", "Mensajes muertos", "/admin/plataforma/mensajes-muertos", MailWarning),
        // HU #13420 — parámetros del motor de descarga masiva de consolidados (solo SuperAdmin).
        superAdmin("admin-descarga-masiva", "Descarga masiva", "/admin/plataforma/descarga-masiva", FileArchive),
        superAdmin("admin-banners", "Banners", "/admin/banners", ImageIcon),
      ],
    },
    // Generación documental (Feature #12201, R12): por módulo accesible, no por rol.
    {
      key: "admin-generacion-documental",
      label: "Generación documental",
      href: GENERACION_DOCUMENTAL_BASE_PATH,
      icon: FileText,
      ...navGatingFor("admin-generacion-documental"),
    },
  );

  // Usuario OT: pestañas del hub del organismo en el dock. El ot_admin las ve todas. Cualquier otro rol del organismo
  // (p. ej. Gestor OT, HU #13655) ve cada pestaña solo con su permiso OT, que el Admin OT le puede otorgar por usuario.
  // Reglas, Requisitos y Configuración son del SuperAdmin (HU #12856) y Preasignación ya no existe.
  const officeId = ctx.otTransitOfficeId;
  if (ctx.isOtUser && officeId) {
    // La regla de cada pestaña vive en la tabla de acceso (`OT_HUB_TAB_PERMISSION`), la misma que gobierna la ruta.
    const ot = (key: string, label: string, tab: OtHubDelegableTab, icon: NavIcon): Entry => ({
      key,
      label,
      href: otHubModulePath(officeId, tab),
      icon,
      ...otHubNavGating(tab, ctx.isOtAdmin),
    });
    entries.push(
      ot(OT_ADM_DOCK.tramites, COPY.B21Tramites, "client-procedures", FileStack),
      ot(OT_ADM_DOCK.documents, "Documentos", "documents", FileText),
      ot(OT_ADM_DOCK.usuarios, COPY.B21Usuarios, "usuarios", Users),
      ot(OT_ADM_DOCK.reportes, COPY.B21Reportes, "reportes", BarChart3),
      ot(OT_ADM_DOCK.mandatos, "Mandatos", "mandatos", FileSignature),
      ot(OT_ADM_DOCK.imprintValidation, "Validar impronta", "imprint-validation", Fingerprint),
    );
  }

  // (La tabla de acceso declara el requisito de rol de «mi-empresa» / «admin-network» —AdminCompany—; aquí se decide con
  // `ctx.isAdminCompany`, que sale del mismo claim, porque además dependen de `isGroupParent` y `tenantId`.)
  // Gestor: una sola entrada «Administración» a la consola de su compañía (/admin/companies lleva a su empresa).
  if (ctx.isAdminCompany) {
    entries.push({ key: "mi-empresa", label: "Administración", href: "/admin/companies", icon: Building2 });
    if (ctx.isGroupParent && ctx.tenantId) {
      entries.push({ key: "admin-network", label: "Red de clientes", href: `/admin/companies/${ctx.tenantId}/children`, icon: Building2 });
    }
  }

  entries.push(
    { key: "log-qx", label: "Log QX", href: "/?m=log-qx", icon: Radar, ...navGatingFor("log-qx") },
    {
      // ICT es contenedor: nombra el sistema y luego qué se quiere de él (HU #11619). Sin permiso propio, como
      // Plataforma: `buildDock` lo muestra solo si le queda algún hijo visible. Log ICT es solo del SuperAdmin;
      // Trazabilidad y Reportes van por su permiso, que tiene el Admin Company (Bug #13445, D10).
      key: "ict",
      label: "ICT",
      href: "",
      icon: Network,
      children: [
        { key: "ict-logs", label: "Log ICT", href: "/?m=ict-logs", icon: Network, ...navGatingFor("ict-logs") },
        { key: "ict-trazabilidad", label: "Trazabilidad ICT", href: "/?m=ict-trazabilidad", icon: Route, ...navGatingFor("ict-trazabilidad") },
        { key: "ict-reportes", label: "Reportes ICT", href: "/?m=ict-reportes", icon: BarChart3, ...navGatingFor("ict-reportes") },
      ],
    },
  );

  return {
    sections: DOCK_GROUP_ORDER.map((id) => ({ id, label: DOCK_GROUP_LABEL[id], icon: DOCK_GROUP_ICON[id], side: DOCK_GROUP_SIDE[id] })),
    items: entries.map(inSection),
  };
}
