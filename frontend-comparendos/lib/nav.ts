// Catálogo de navegación de Comparendos para @flit/shell (especificación GDC §7.0). Dice QUÉ existe y QUIÉN lo ve; el
// dock lo filtra por permiso y lo dibuja. Las capacidades de la empresa (OCR, reportes) no van en el token: las trae
// /api/v1/comparendos/sesion y se aplican aquí antes de entregarle el catálogo al dock.
import {
  Bell,
  BookOpen,
  CalendarDays,
  ClipboardList,
  Cog,
  Download,
  FileSearch,
  FileStack,
  FileText,
  Gauge,
  Inbox,
  KeyRound,
  Landmark,
  ListChecks,
  MailWarning,
  MapPinned,
  Plug,
  Receipt,
  ScanText,
  Send,
  Settings,
  ShieldCheck,
  Timer,
  Users,
  Wallet,
} from "lucide-react";
import type { NavCatalog, NavItem } from "@flit/shell/nav";
import type { Capacidades } from "@/lib/sesion";

export const P = {
  bandejaVer: "comparendos.bandeja.ver",
  bandejaExportar: "comparendos.bandeja.exportar",
  comparendoInactivar: "comparendos.comparendo.inactivar",
  comparendoObservar: "comparendos.comparendo.observar",
  contraventorAsignar: "comparendos.contraventor.asignar",
  contraventorConsultar: "comparendos.contraventor.consultar",
  contraventorDocumentos: "comparendos.contraventor.documentos",
  cambioSolicitar: "comparendos.cambio_contraventor.solicitar",
  cambioResponder: "comparendos.cambio_contraventor.responder",
  avisosEnviar: "comparendos.avisos.enviar",
  dpGenerar: "comparendos.dp.generar",
  dpEnviar: "comparendos.dp.enviar",
  dpEnviarMasivo: "comparendos.dp.enviar_masivo",
  dpResponder: "comparendos.dp.responder",
  pagosRegistrar: "comparendos.pagos.registrar",
  pagosCargarMasivo: "comparendos.pagos.cargar_masivo",
  ocrVer: "comparendos.ocr.ver",
  ocrGestionar: "comparendos.ocr.gestionar",
  monitoreoVer: "comparendos.monitoreo.ver",
  monitoreoGestionar: "comparendos.monitoreo.gestionar",
  representantesVer: "comparendos.representantes.ver",
  representantesGestionar: "comparendos.representantes.gestionar",
  otsGestionar: "comparendos.ots.gestionar",
  configuracionVer: "comparendos.configuracion.ver",
  configuracionEditar: "comparendos.configuracion.editar",
  plantillasVer: "comparendos.plantillas.ver",
  mensajesVer: "comparendos.mensajes.ver",
  reportesVer: "comparendos.reportes.ver",
  productividadVer: "comparendos.productividad.ver",
} as const;

export function comparendosNav(caps: Capacidades): NavCatalog {
  const items: NavItem[] = [
    { key: "bandeja", label: "Bandeja", href: "/", section: "operacion", icon: Inbox, permission: P.bandejaVer },
    ...(caps.ocr ? [{ key: "ocr", label: "Paquetes OCR", href: "/ocr", section: "operacion", icon: ScanText, permission: P.ocrVer }] : []),
    ...(caps.derechosPeticion
      ? [{ key: "dp-lotes", label: "Envíos masivos de DP", href: "/dp/lotes", section: "operacion", icon: Send, permission: P.dpEnviarMasivo }]
      : []),
    { key: "pagos", label: "Carga de pagos", href: "/pagos", section: "operacion", icon: Wallet, permission: P.pagosCargarMasivo },
    { key: "mensajes", label: "Centro de mensajes", href: "/mensajes", section: "operacion", icon: MailWarning, permission: P.mensajesVer },
    ...(caps.reportes
      ? [
          { key: "dashboard", label: "Dashboard", href: "/reportes", section: "reportes", icon: Gauge, permission: P.reportesVer },
          { key: "productividad", label: "Productividad", href: "/reportes/productividad", section: "reportes", icon: Users, permission: P.productividadVer },
        ]
      : []),
    { key: "exportaciones", label: "Mis exportaciones", href: "/exportaciones", section: "reportes", icon: Download },
    { key: "monitoreo", label: "NITs y placas", href: "/configuracion/monitoreo", section: "configuracion", icon: FileSearch, permission: P.monitoreoVer },
    { key: "representantes", label: "Representantes legales", href: "/configuracion/representantes", section: "configuracion", icon: ShieldCheck, permission: P.representantesVer },
    { key: "ots", label: "Organismos de tránsito", href: "/configuracion/organismos", section: "configuracion", icon: Landmark, permission: P.otsGestionar },
    { key: "correo", label: "Empresa y correo", href: "/configuracion/empresa", section: "configuracion", icon: Settings, permission: P.configuracionVer },
    { key: "plantillas", label: "Plantillas", href: "/configuracion/plantillas", section: "configuracion", icon: FileText, permission: P.plantillasVer },
    {
      key: "flit",
      label: "Administración FLIT",
      href: "",
      section: "administracion",
      icon: Cog,
      superAdminOnly: true,
      children: [
        { key: "adm-empresas", label: "Parametrizador por empresa", href: "/admin/empresas", section: "administracion", icon: KeyRound, superAdminOnly: true },
        { key: "adm-tipologias", label: "Tipologías de DP", href: "/admin/tipologias", section: "administracion", icon: BookOpen, superAdminOnly: true },
        { key: "adm-plantillas", label: "Editor de plantillas", href: "/admin/plantillas", section: "administracion", icon: FileStack, superAdminOnly: true },
        { key: "adm-ingesta", label: "Trabajos e ingesta", href: "/admin/ingesta", section: "administracion", icon: Plug, superAdminOnly: true },
        { key: "adm-log", label: "Log de migración", href: "/admin/log-migracion", section: "administracion", icon: ListChecks, superAdminOnly: true },
        { key: "adm-organismos", label: "Catálogo de organismos", href: "/admin/organismos", section: "administracion", icon: MapPinned, superAdminOnly: true },
        { key: "adm-municipios", label: "Fuentes municipales", href: "/admin/municipios", section: "administracion", icon: ClipboardList, superAdminOnly: true },
        { key: "adm-alertas", label: "Alertas de integración", href: "/admin/alertas", section: "administracion", icon: Bell, superAdminOnly: true },
        { key: "adm-scheduler", label: "Horario de avisos", href: "/admin/scheduler", section: "administracion", icon: Timer, superAdminOnly: true },
        { key: "adm-ocr", label: "Prompt OCR", href: "/admin/ocr", section: "administracion", icon: Receipt, superAdminOnly: true },
        { key: "adm-calendario", label: "Calendario", href: "/admin/calendario", section: "administracion", icon: CalendarDays, superAdminOnly: true },
      ],
    },
  ];

  return {
    sections: [
      { id: "operacion", label: "Operación", icon: Inbox, side: "left" },
      { id: "reportes", label: "Reportes", icon: Gauge, side: "left" },
      { id: "configuracion", label: "Configuración", icon: Settings, side: "right" },
      { id: "administracion", label: "Administración FLIT", icon: Cog, side: "right" },
    ],
    items,
  };
}
