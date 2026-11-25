// Usuarios de Impuestos de la compañía (contracts/openapi/impuestos.v1.yaml 1.0.0, tag Usuarios; plan §5.1). Delegados en
// la plataforma con la forma de Diagnóstico (ADR-0089). Todas las rutas admiten `empresa` (solo Super Admin). Las
// acciones responden 204 sin la fila: quien llama relee la lista.
import { apiFetch } from "@/lib/api";
import { rotular } from "@/lib/rotulos";

export type RolImpuestos = "gestor_impuestos" | "admin_impuestos";
export type EstadoUsuario = "ACTIVO" | "INVITADO" | "SUSPENDIDO";

export interface UsuarioImpuestos {
  id: string;
  nombre: string;
  email: string;
  rol: RolImpuestos | string;
  estado: EstadoUsuario | string;
}

export interface PaginaUsuarios {
  items: UsuarioImpuestos[];
  total: number;
  page: number;
  pageSize: number;
}

export interface InvitacionUsuario {
  email: string;
  nombre: string;
  rol: RolImpuestos;
}

/** Orden en que se ofrecen los roles (de más a menos alcance). */
export const ROLES_IMPUESTOS: readonly RolImpuestos[] = ["admin_impuestos", "gestor_impuestos"];

/** x-flit-etiquetas de RolImpuestos. */
export const ETIQUETA_ROL: Readonly<Record<string, string>> = {
  gestor_impuestos: "Gestor Impuestos",
  admin_impuestos: "Administrador Impuestos",
};

/** Descripción de cada rol en «Invitar usuario» (spec-visual §3.8; D-GESTOR). */
export const DESCRIPCION_ROL: Readonly<Record<RolImpuestos, string>> = {
  admin_impuestos: "Todo en su compañía: liquidar, cerrar lotes, usuarios y configuración.",
  gestor_impuestos: "Ve toda la compañía y liquida por placa o masivo. No gestiona usuarios ni configuración.",
};

/** x-flit-etiquetas del estado del usuario. */
export const ETIQUETA_ESTADO_USUARIO: Readonly<Record<string, string>> = { ACTIVO: "Activo", INVITADO: "Invitado", SUSPENDIDO: "Suspendido" };

/** x-flit-literales.errores de las rutas de usuarios. */
export const ERRORES_USUARIOS: Readonly<Record<string, string>> = {
  EMPRESA_NO_PERMITIDA: "Solo el Super Admin puede elegir la compañía.",
  EMPRESA_REQUERIDA: "Elige una compañía.",
  EMPRESA_NO_ENCONTRADA: "La compañía no existe o no tiene Impuestos.",
  CORREO_INVALIDO: "Un correo no es válido.",
  NOMBRE_REQUERIDO: "Escribe el nombre.",
  ROL_NO_PERMITIDO: "El rol no es válido para Impuestos.",
  ROL_NO_ASIGNABLE: "No puedes asignar ese rol.",
  OBJETIVO_PROTEGIDO: "Este usuario se gestiona desde el administrador de la plataforma.",
  ACCION_SOBRE_SI_MISMO: "No puedes hacer esta acción sobre tu propio usuario.",
  USUARIO_NO_ENCONTRADO: "El usuario no existe en Impuestos de esta compañía.",
  USUARIO_SUSPENDIDO: "El usuario está suspendido en Impuestos.",
  INVITACION_PENDIENTE: "El usuario todavía no acepta su invitación.",
  ULTIMO_ADMINISTRADOR: "La compañía debe conservar al menos un Administrador Impuestos.",
  ROL_YA_VIGENTE: "El usuario ya tiene un rol vigente en Impuestos.",
  DEMASIADAS_SOLICITUDES: "Hiciste demasiadas solicitudes. Espera un momento e intenta de nuevo.",
  PLATAFORMA_NO_DISPONIBLE: "La plataforma no responde. Intenta más tarde.",
  PAGINACION_INVALIDA: "La paginación no es válida.",
  FILTRO_INVALIDO: "Un filtro no es válido.",
};

export const etiquetaRol = (rol: string | null | undefined) => rotular({ contexto: "rol de Impuestos", codigo: rol, respaldo: ETIQUETA_ROL });
export const etiquetaEstadoUsuario = (estado: string | null | undefined) =>
  rotular({ contexto: "estado del usuario", codigo: estado, respaldo: ETIQUETA_ESTADO_USUARIO });

/**
 * Roles que puede dar quien gestiona (contrato: solo el Super Admin da admin_impuestos; el Administrador Impuestos solo
 * gestor_impuestos, si no 403 ROL_NO_ASIGNABLE).
 */
export function rolesAsignables(esSuperAdmin: boolean): RolImpuestos[] {
  return esSuperAdmin ? [...ROLES_IMPUESTOS] : ["gestor_impuestos"];
}

export const TAMANO_PAGINA_USUARIOS = 50;

const BASE = "/usuarios";
const deUsuario = (id: string, accion: "rol" | "suspender" | "reactivar") => `${BASE}/${encodeURIComponent(id)}/${accion}`;

export function listarUsuarios(filtros: { q: string; page: number; pageSize: number }, empresa: string | null, signal?: AbortSignal): Promise<PaginaUsuarios> {
  const params = new URLSearchParams();
  if (filtros.q.trim()) params.set("q", filtros.q.trim());
  params.set("page", String(filtros.page));
  params.set("pageSize", String(filtros.pageSize));
  return apiFetch<PaginaUsuarios>(`${BASE}?${params.toString()}`, { signal, empresa });
}

export const invitarUsuario = (invitacion: InvitacionUsuario, empresa: string | null) =>
  apiFetch<UsuarioImpuestos>(BASE, { method: "POST", body: invitacion, empresa });

export const cambiarRolUsuario = (id: string, rol: RolImpuestos, empresa: string | null) =>
  apiFetch<void>(deUsuario(id, "rol"), { method: "PUT", body: { rol }, empresa });

export const suspenderUsuario = (id: string, empresa: string | null) => apiFetch<void>(deUsuario(id, "suspender"), { method: "POST", empresa });

export const reactivarUsuario = (id: string, empresa: string | null) => apiFetch<void>(deUsuario(id, "reactivar"), { method: "POST", empresa });
