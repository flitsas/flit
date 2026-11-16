// Módulo Usuarios de Diagnóstico (HU #14588). Tipos y rótulos del contrato `contrato-api.md` §3.3 (esquemas
// ListaUsuariosDiagnostico, UsuarioDiagnostico, ListaEmpresasUsuarios). El servidor calcula `acciones` y
// `roles_asignables` para el actor: la UI solo ofrece lo que llega ahí (SP-10, SP-11=a, SP-20).
import type { NombreIcono } from "@flit/ui/Icono";
import type { TonoEtiqueta } from "@/components/tablero/vistasDetalle/comun";

/** Roles del producto Diagnóstico, por código (lista blanca, SP-10). Nunca roles de otros productos. */
export const ROLES_DIAGNOSTICO = ["usuario_diagnostico", "admin_diagnostico"] as const;
export type RolDiagnostico = (typeof ROLES_DIAGNOSTICO)[number];

export const ESTADOS_USUARIO = ["ACTIVO", "INVITADO", "SUSPENDIDO"] as const;
export type EstadoUsuario = (typeof ESTADOS_USUARIO)[number];

/** Acciones por fila. No hay eliminar, reenviar ni cancelar invitación en esta ola (H-U2). */
export const ACCIONES_USUARIO = ["CAMBIAR_ROL", "SUSPENDER", "REACTIVAR"] as const;
export type AccionUsuario = (typeof ACCIONES_USUARIO)[number];

export const MOTIVOS_SIN_ACCIONES = ["USTED_MISMO", "ADMIN_COMPANIA", "SOLO_SUPERADMIN", "INVITACION", "CUENTA_INACTIVA"] as const;
export type MotivoSinAcciones = (typeof MOTIVOS_SIN_ACCIONES)[number];

export interface UsuarioDiagnostico {
  /** Usuario (tipo USUARIO) o invitación (tipo INVITACION); va en `{usuario}` de las rutas. */
  id: string;
  tipo: "USUARIO" | "INVITACION";
  nombre: string;
  /** @pii:medium — solo en la celda visible, nunca en aria-label, title ni data-* (SP-20). */
  correo: string;
  rol: RolDiagnostico;
  rol_etiqueta: string;
  estado: EstadoUsuario;
  acciones: AccionUsuario[];
  motivo_sin_acciones: MotivoSinAcciones | null;
}

/** Compañía de la cabecera en el backoffice (D-6): `GET /backoffice/empresas/{empresa}/usuarios` la trae en el 200. */
export interface EmpresaCabecera {
  id: string;
  nombre: string;
  nit: string | null;
}

export interface ListaUsuarios {
  /** Solo en el grupo del backoffice (D-6). Opcional para tolerar un back anterior: sin ella, la cabecera queda en esqueleto. */
  empresa?: EmpresaCabecera;
  pagina: number;
  tamano: number;
  total: number;
  /** Lo que el actor puede dar al invitar o cambiar rol (SP-11): admin → [usuario]; SuperAdmin → los dos. */
  roles_asignables: RolDiagnostico[];
  usuarios: UsuarioDiagnostico[];
}

export interface FiltrosUsuarios {
  busqueda: string;
  rol: RolDiagnostico | null;
  estado: EstadoUsuario | null;
  pagina: number;
  tamano: number;
}

export interface InvitacionUsuario {
  correo: string;
  nombre: string;
  rol: RolDiagnostico;
}

export interface EmpresaUsuarios {
  id: string;
  nombre: string;
  nit: string | null;
  estado: string;
  usuarios_diagnostico: number;
  administradores_diagnostico: number;
}

export interface ListaEmpresasUsuarios {
  pagina: number;
  tamano: number;
  total: number;
  empresas: EmpresaUsuarios[];
}

/** Filtro «Usuarios» de la tabla de compañías. No hay filtro de estado (H-U3). */
export type FiltroConUsuarios = "CON" | "SIN";

export interface FiltrosEmpresas {
  busqueda: string;
  usuarios: FiltroConUsuarios | null;
  pagina: number;
  tamano: number;
}

/** Tope de la búsqueda (contrato §3.3: string ≤ 100). */
export const BUSQUEDA_MAXIMA = 100;
export const NOMBRE_MAXIMO = 150;
export const CORREO_MAXIMO = 254;
/** Filas por página por defecto (D-6: 25) y tope del contrato (§3.3: tamano 1..100, pagina × tamano ≤ 10 000). */
export const TAMANO_POR_DEFECTO = 25;
export const TAMANO_MAXIMO = 100;
export const FILAS_MAXIMAS = 10_000;
/** Espera tras la última tecla del buscador antes de consultar (como el listado del inicio). */
export const ESPERA_BUSQUEDA_MS = 300;

export const ETIQUETA_ROL: Readonly<Record<RolDiagnostico, string>> = {
  usuario_diagnostico: "Usuario de Diagnóstico",
  admin_diagnostico: "Administrador de Diagnóstico",
};

/** Descripción corta de cada rol en los radios de invitar y cambiar rol. */
export const DESCRIPCION_ROL: Readonly<Record<RolDiagnostico, string>> = {
  usuario_diagnostico: "Crea y consulta los diagnósticos de la compañía.",
  admin_diagnostico: "Además gestiona los usuarios de Diagnóstico de la compañía.",
};

export const ETIQUETA_ESTADO: Readonly<Record<EstadoUsuario, string>> = { ACTIVO: "Activo", INVITADO: "Invitado", SUSPENDIDO: "Suspendido" };

/** Estado = color + icono + texto (T2): verde activo, azul invitado, naranja suspendido. */
export const ESTILO_ESTADO: Readonly<Record<EstadoUsuario, { tono: TonoEtiqueta; icono: NombreIcono }>> = {
  ACTIVO: { tono: "success", icono: "check" },
  INVITADO: { tono: "info", icono: "correo" },
  SUSPENDIDO: { tono: "warning", icono: "pausa" },
};

/** Por qué una fila no tiene acciones (texto visible en la celda «Acciones»). */
export const TEXTO_MOTIVO: Readonly<Record<MotivoSinAcciones, string>> = {
  USTED_MISMO: "Es tu usuario",
  ADMIN_COMPANIA: "Administrador de la compañía: se gestiona en el hub",
  SOLO_SUPERADMIN: "Solo un SuperAdmin puede cambiarlo",
  INVITACION: "Invitación pendiente",
  CUENTA_INACTIVA: "Cuenta inactiva en la plataforma",
};

/**
 * Aviso tras invitar: el mismo en todos los casos (SP-15, H-UI 3). Es el texto del 202 del contrato; la UI lo tiene fijo
 * y no pinta el `mensaje` del servidor (AP-9).
 */
export const AVISO_INVITACION = "Si el correo es válido, la persona recibirá la invitación o el acceso a Diagnóstico.";
