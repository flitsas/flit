// Catálogo de navegación de Comparendos para @flit/shell (especificación GDC §7.0). Dice QUÉ existe y QUIÉN lo ve; el
// dock lo filtra por permiso y lo dibuja. Las capacidades de la empresa (OCR, reportes) no van en el token: las trae
// /api/v1/comparendos/sesion y se aplican aquí antes de entregarle el catálogo al dock.
import { MapPinned, ScanText } from "lucide-react";
import { iconoComo } from "@flit/ui/iconoComo";
import type { NavCatalog, NavItem, NavSection } from "@flit/shell/nav";
import type { Ilustracion } from "@flit/ui/ilustraciones";
import { rutaKit } from "@flit/ui/iconos-mapas";
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

/**
 * Ilustración de cada grupo y entrada del dock (HU #14486, como `tramitesNav`): solo presentación, ni la ruta ni quién
 * la ve cambian. Una clave sin ilustración se queda con su ícono de línea.
 */
const ILUSTRACION: Record<string, Ilustracion> = {
  operacion: "comparendos",
  reportes: "reportes",
  configuracion: "configuracion",
  administracion: "empresas",
  bandeja: "comparendos",
  ocr: "documentos",
  "dp-lotes": "notificaciones",
  pagos: "fur",
  mensajes: "notificaciones",
  dashboard: "reportes",
  productividad: "colaboradores",
  monitoreo: "consulta-placa",
  representantes: "firmas",
  ots: "organismos",
  correo: "empresas",
  plantillas: "generacion-documental",
  flit: "configuracion",
  "adm-empresas": "empresas",
  "adm-tipologias": "causales",
  "adm-plantillas": "generacion-documental",
  "adm-ingesta": "integraciones",
  "adm-log": "auditoria",
  "adm-organismos": "organismos",
  "adm-municipios": "fuentes-oficiales",
  "adm-alertas": "notificaciones",
  "adm-scheduler": "calendario",
  "adm-ocr": "documentos",
  "adm-calendario": "calendario",
};

function iconoDockDe(key: string): string | undefined {
  const nombre = ILUSTRACION[key];
  return nombre ? rutaKit(`modulo/${nombre}`) : undefined;
}

function conIlustracion(item: NavItem): NavItem {
  return { ...item, ilustracion: iconoDockDe(item.key), children: item.children?.map(conIlustracion) };
}

export function comparendosNav(caps: Capacidades): NavCatalog {
  const items: NavItem[] = [
    { key: "bandeja", label: "Bandeja", href: "/", section: "operacion", icon: iconoComo("radicacion"), permission: P.bandejaVer },
    ...(caps.ocr ? [{ key: "ocr", label: "Paquetes OCR", href: "/ocr", section: "operacion", icon: ScanText, permission: P.ocrVer }] : []),
    ...(caps.derechosPeticion
      ? [{ key: "dp-lotes", label: "Envíos masivos de DP", href: "/dp/lotes", section: "operacion", icon: iconoComo("radicacion"), permission: P.dpEnviarMasivo }]
      : []),
    { key: "pagos", label: "Carga de pagos", href: "/pagos", section: "operacion", icon: iconoComo("pago"), permission: P.pagosCargarMasivo },
    { key: "mensajes", label: "Centro de mensajes", href: "/mensajes", section: "operacion", icon: iconoComo("correo"), permission: P.mensajesVer },
    ...(caps.reportes
      ? [
          { key: "dashboard", label: "Dashboard", href: "/reportes", section: "reportes", icon: iconoComo("medidor"), permission: P.reportesVer },
          { key: "productividad", label: "Productividad", href: "/reportes/productividad", section: "reportes", icon: iconoComo("usuarios"), permission: P.productividadVer },
        ]
      : []),
    { key: "monitoreo", label: "NITs y placas", href: "/configuracion/monitoreo", section: "configuracion", icon: iconoComo("consulta"), permission: P.monitoreoVer },
    { key: "representantes", label: "Representantes legales", href: "/configuracion/representantes", section: "configuracion", icon: iconoComo("escudo"), permission: P.representantesVer },
    { key: "ots", label: "Organismos de tránsito", href: "/configuracion/organismos", section: "configuracion", icon: iconoComo("banco"), permission: P.otsGestionar },
    { key: "correo", label: "Empresa y correo", href: "/configuracion/empresa", section: "configuracion", icon: iconoComo("ajustes"), permission: P.configuracionVer },
    { key: "plantillas", label: "Plantillas", href: "/configuracion/plantillas", section: "configuracion", icon: iconoComo("documento"), permission: P.plantillasVer },
    {
      key: "flit",
      label: "Administración FLIT",
      href: "",
      section: "administracion",
      icon: iconoComo("ajustes"),
      superAdminOnly: true,
      children: [
        { key: "adm-empresas", label: "Parametrizador por empresa", href: "/admin/empresas", section: "administracion", icon: iconoComo("rol"), superAdminOnly: true },
        { key: "adm-tipologias", label: "Tipologías de DP", href: "/admin/tipologias", section: "administracion", icon: iconoComo("ayuda"), superAdminOnly: true },
        { key: "adm-plantillas", label: "Editor de plantillas", href: "/admin/plantillas", section: "administracion", icon: iconoComo("archivo"), superAdminOnly: true },
        { key: "adm-ingesta", label: "Trabajos e ingesta", href: "/admin/ingesta", section: "administracion", icon: iconoComo("integraciones"), superAdminOnly: true },
        { key: "adm-log", label: "Log de migración", href: "/admin/log-migracion", section: "administracion", icon: iconoComo("lista"), superAdminOnly: true },
        { key: "adm-organismos", label: "Catálogo de organismos", href: "/admin/organismos", section: "administracion", icon: MapPinned, superAdminOnly: true },
        { key: "adm-municipios", label: "Fuentes municipales", href: "/admin/municipios", section: "administracion", icon: iconoComo("portapapeles"), superAdminOnly: true },
        { key: "adm-alertas", label: "Alertas de integración", href: "/admin/alertas", section: "administracion", icon: iconoComo("campana"), superAdminOnly: true },
        { key: "adm-scheduler", label: "Horario de avisos", href: "/admin/scheduler", section: "administracion", icon: iconoComo("cronometro"), superAdminOnly: true },
        { key: "adm-ocr", label: "Prompt OCR", href: "/admin/ocr/prompts", section: "administracion", icon: iconoComo("pago"), superAdminOnly: true },
        { key: "adm-calendario", label: "Calendario", href: "/admin/calendario", section: "administracion", icon: iconoComo("calendario"), superAdminOnly: true },
      ],
    },
  ];

  return {
    sections: (
      [
        { id: "operacion", label: "Operación", icon: iconoComo("radicacion"), side: "left" },
        { id: "reportes", label: "Reportes", icon: iconoComo("medidor"), side: "left" },
        { id: "configuracion", label: "Configuración", icon: iconoComo("ajustes"), side: "right" },
        { id: "administracion", label: "Administración FLIT", icon: iconoComo("ajustes"), side: "right" },
      ] satisfies NavSection[]
    ).map((sec) => ({ ...sec, ilustracion: iconoDockDe(sec.id) })),
    items: items.map(conIlustracion),
  };
}
