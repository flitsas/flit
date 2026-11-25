// Sesión de Impuestos tal como la entrega la API (GET /api/v1/impuestos/sesion, plan §5.1):
// `{usuario, empresa{id,nombre}, roles, permisos, esSuperAdmin, aprovisionamiento{estado, firmaConfirmada}}`.
// El menú y los banners se arman con esto; la API vuelve a validar cada permiso.

export interface SesionImpuestos {
  usuario: { id: string; email?: string | null; nombre?: string | null };
  empresa: { id: string; nombre?: string | null };
  roles: string[];
  permisos: string[];
  esSuperAdmin: boolean;
  aprovisionamiento?: { estado?: string | null; firmaConfirmada?: boolean | null } | null;
}

/** Permisos del producto (plan §5.2, `Plataforma/Permisos.cs` de core-impuestos). */
export const PERMISOS = {
  dashboardVer: "impuestos.dashboard.ver",
  liquidacionesVer: "impuestos.liquidaciones.ver",
  liquidacionesCrear: "impuestos.liquidaciones.crear",
  liquidacionesCerrar: "impuestos.liquidaciones.cerrar",
  recibosDescargar: "impuestos.recibos.descargar",
  reportesVer: "impuestos.reportes.ver",
  reportesExportar: "impuestos.reportes.exportar",
  usuariosGestionar: "impuestos.usuarios.gestionar",
  configuracionVer: "impuestos.configuracion.ver",
  configuracionEditar: "impuestos.configuracion.editar",
  contratoEditar: "impuestos.configuracion.contrato.editar",
  backofficeVer: "impuestos.backoffice.ver",
  credencialesCargar: "impuestos.credenciales.cargar",
} as const;

export type Permiso = (typeof PERMISOS)[keyof typeof PERMISOS];

/** ¿La sesión tiene el permiso? El Super Admin pasa siempre (no recibe rol de producto, F5b-CF1); la API lo revalida. */
export function tienePermiso(sesion: SesionImpuestos | null | undefined, permiso: Permiso): boolean {
  if (!sesion) return false;
  return sesion.esSuperAdmin === true || sesion.permisos.map((p) => p.toLowerCase()).includes(permiso);
}

/** Banners de estado del producto (plan §6.1). */
export type BannerEstado = "FIRMA_SIN_CONFIRMAR" | "SIN_WEBHOOK" | "SUSPENDIDO";

/**
 * Qué banners pinta la sesión (plan §3.1): «Firma sin confirmar» en LISTO hasta el primer webhook válido, «Modo sin
 * webhook» en LISTO_SIN_WEBHOOK y «Producto suspendido: solo lectura» en SUSPENDIDO. Sin el bloque de aprovisionamiento
 * (un back anterior) no se pinta nada.
 */
export function bannersDeSesion(sesion: SesionImpuestos | null | undefined): BannerEstado[] {
  const a = sesion?.aprovisionamiento;
  if (!a?.estado) return [];
  if (a.estado === "SUSPENDIDO") return ["SUSPENDIDO"];
  if (a.estado === "LISTO_SIN_WEBHOOK") return ["SIN_WEBHOOK"];
  if (a.estado === "LISTO" && a.firmaConfirmada === false) return ["FIRMA_SIN_CONFIRMAR"];
  return [];
}
