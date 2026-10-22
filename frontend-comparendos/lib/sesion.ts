// Sesión de Comparendos tal como la entrega la API (/api/v1/comparendos/sesion): permisos efectivos del usuario y
// capacidades de su empresa. El menú y los botones se arman con esto; la API vuelve a validar todo.
export interface Capacidades {
  derechosPeticion: boolean;
  consultaContraventor: boolean;
  cambioContraventor: boolean;
  ocr: boolean;
  reportes: boolean;
}

export interface SesionComparendos {
  usuario: { id: string; email: string; esSuperAdmin: boolean };
  permisos: string[];
  empresa: { id: string; nombre: string | null; capacidades: Capacidades } | null;
}

export const SIN_CAPACIDADES: Capacidades = {
  derechosPeticion: false,
  consultaContraventor: false,
  cambioContraventor: false,
  ocr: false,
  reportes: false,
};

export function tienePermiso(sesion: SesionComparendos | null, slug: string): boolean {
  return !!sesion && (sesion.usuario.esSuperAdmin || sesion.permisos.includes(slug));
}

/** El SuperAdmin ve todas las capacidades (lee cualquier empresa); un usuario, las de la suya. */
export function capacidades(sesion: SesionComparendos | null): Capacidades {
  if (!sesion) return SIN_CAPACIDADES;
  if (sesion.usuario.esSuperAdmin)
    return { derechosPeticion: true, consultaContraventor: true, cambioContraventor: true, ocr: true, reportes: true };
  return sesion.empresa?.capacidades ?? SIN_CAPACIDADES;
}
