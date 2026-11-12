/**
 * Ilustraciones de módulo del prototipo «Diseño Inteligente» (rama `propuesta`, assets/ilustraciones). Las 41 WebP
 * viven en `public/assets/ilustraciones/` de cada app; esta tabla dice cuál le toca a cada clave del dock y a cada
 * módulo, con las mismas asignaciones que usa el prototipo en `datos.js` (p. ej. Usuarios → colaboradores,
 * Quipux → integraciones, Confirmación RUNT → fuentes-oficiales).
 */
export const ILUSTRACIONES = [
  "auditoria", "ayuda", "banners", "biometria", "calendario", "causales", "colaboradores", "comparendos",
  "configuracion", "consulta-placa", "contrasena", "diagnostico", "documentos", "empresas", "filtros", "firmas",
  "fuentes-oficiales", "fur", "generacion-documental", "heroe-ingreso", "historial-placa", "identidad", "improntas",
  "inicio", "integraciones", "mandatos", "matricula", "notificaciones", "organismos", "pendientes", "perfil",
  "placa-colombia", "preasignacion", "procesos", "productos", "reportes", "revocatorias", "roles", "seguridad",
  "tema-claro", "tema-oscuro", "tramites", "traspaso", "trazabilidad", "vencidos",
] as const;

export type Ilustracion = (typeof ILUSTRACIONES)[number];

/** Clave del dock, del catálogo de navegación o del módulo → ilustración. */
const POR_CLAVE: Record<string, Ilustracion> = {
  // Grupos del dock
  inicio: "inicio",
  tramites: "tramites",
  identidad: "identidad",
  reportes: "reportes",
  usuarios: "colaboradores",
  administracion: "empresas",
  administradores: "configuracion",
  integraciones: "integraciones",
  // Módulos de la SPA de Trámites
  validaciones: "identidad",
  "historial-placa": "historial-placa",
  revocatorias: "revocatorias",
  rbac: "roles",
  auditoria: "auditoria",
  "log-qx": "integraciones",
  ict: "integraciones",
  "ict-logs": "integraciones",
  "ict-trazabilidad": "trazabilidad",
  "ict-reportes": "reportes",
  ayuda: "ayuda",
  perfil: "perfil",
  // Administradores (secciones y entradas)
  "admin-transit": "organismos",
  "admin-companies": "empresas",
  "mi-empresa": "empresas",
  "admin-network": "empresas",
  "admin-transit-offices": "organismos",
  "admin-rejection-reasons": "causales",
  "admin-documentos": "documentos",
  "admin-documents": "documentos",
  "admin-improntas": "improntas",
  "admin-generacion-documental": "generacion-documental",
  "admin-quipux": "integraciones",
  "admin-plataforma": "configuracion",
  "admin-tipos-tramite": "tramites",
  "admin-confirmacion-runt": "fuentes-oficiales",
  "admin-mandatos": "mandatos",
  "admin-fur": "fur",
  "admin-notificaciones": "notificaciones",
  "admin-configuracion-ia": "configuracion",
  "admin-mensajes-muertos": "notificaciones",
  "admin-descarga-masiva": "documentos",
  "admin-banners": "banners",
  "admin-persona-natural": "identidad",
  "admin-seguridad": "seguridad",
  "admin-jobs": "procesos",
  // Productos de la suite
  comparendos: "comparendos",
  diagnostico: "diagnostico",
};

/**
 * Ruta pública de una ilustración. UI Kit 2026 (propuesta-2): las ilustraciones WebP del diseño anterior se
 * reemplazan por el ícono del kit de cada clave (MAPA_ICONO) en su disco (`/assets/kit/modulo/<nombre>.svg`, generado
 * desde `iconos-kit.ts`). Así toda pantalla que todavía pide `ilustracionDe(...)` ya muestra el ícono del kit; las
 * pantallas se pasan a `<IconoModulo>` (que además sigue el tema) en su ronda. Solo la placa sigue en WebP.
 */
export function rutaIlustracion(nombre: Ilustracion): string {
  if (nombre === "placa-colombia" || nombre === "heroe-ingreso") return `/assets/ilustraciones/${nombre}.webp`;
  return `/assets/kit/modulo/${nombre}.svg`;
}

/** Ilustración que le toca a una clave del dock o a un módulo; `undefined` si no tiene. */
export function ilustracionDe(clave: string): string | undefined {
  const nombre = POR_CLAVE[clave];
  return nombre ? rutaIlustracion(nombre) : undefined;
}
