// Capa de datos del módulo Usuarios de Diagnóstico (HU #14588, contrato-api.md §3.2/§3.3). Dos grupos de rutas con la
// misma forma: `/usuarios*` (tenant del token; nunca se manda `empresa` en la query, sería 400 EMPRESA_NO_PERMITIDA) y
// `/backoffice/empresas/{empresa}/usuarios*` (solo SuperAdmin). La API vuelve a exigir el permiso en cada ruta.
import { ApiError, apiFetch } from "@/lib/api";
import { mensajeErrorFijo } from "@/lib/errores";
import {
  ACCIONES_USUARIO,
  BUSQUEDA_MAXIMA,
  ESTADOS_USUARIO,
  FILAS_MAXIMAS,
  TAMANO_MAXIMO,
  MOTIVOS_SIN_ACCIONES,
  ROLES_DIAGNOSTICO,
  type AccionUsuario,
  type EmpresaCabecera,
  type EmpresaUsuarios,
  type EstadoUsuario,
  type FiltrosEmpresas,
  type FiltrosUsuarios,
  type InvitacionUsuario,
  type ListaEmpresasUsuarios,
  type ListaUsuarios,
  type MotivoSinAcciones,
  type RolDiagnostico,
  type UsuarioDiagnostico,
} from "./tipos";

/** Raíz de las rutas de usuarios: la del token sin empresa; la del backoffice con ella. */
export function rutaUsuarios(empresa: string | null): string {
  return empresa ? `/backoffice/empresas/${encodeURIComponent(empresa)}/usuarios` : "/usuarios";
}

/**
 * Paginación dentro del contrato (§3.3): tamano entre 1 y 100 y pagina entre 1 y floor(10 000 / tamano). Se aplica al leer
 * la URL y antes de llamar, para no provocar un 400 PAGINACION_INVALIDA con una URL editada a mano.
 */
export function acotarPaginacion(pagina: number, tamano: number): { pagina: number; tamano: number } {
  const t = Math.min(TAMANO_MAXIMO, Math.max(1, Math.trunc(Number.isFinite(tamano) ? tamano : 1)));
  const p = Math.min(Math.floor(FILAS_MAXIMAS / t), Math.max(1, Math.trunc(Number.isFinite(pagina) ? pagina : 1)));
  return { pagina: p, tamano: t };
}

const esDe = <T extends string>(lista: readonly T[], v: unknown): v is T => typeof v === "string" && (lista as readonly string[]).includes(v);
const entero = (v: unknown, def: number) => (typeof v === "number" && Number.isFinite(v) ? v : def);

/** Una fila del contrato. Acciones fuera de la lista blanca se descartan; una invitación nunca trae acciones (H-U2). */
function filaDesde(dato: unknown): UsuarioDiagnostico | null {
  if (!dato || typeof dato !== "object") return null;
  const d = dato as Record<string, unknown>;
  if (typeof d.id !== "string" || typeof d.nombre !== "string" || !esDe(ESTADOS_USUARIO, d.estado) || !esDe(ROLES_DIAGNOSTICO, d.rol)) return null;
  const tipo = d.tipo === "INVITACION" ? "INVITACION" : "USUARIO";
  const acciones = tipo === "INVITACION" || !Array.isArray(d.acciones) ? [] : d.acciones.filter((a): a is AccionUsuario => esDe(ACCIONES_USUARIO, a));
  return {
    id: d.id,
    tipo,
    nombre: d.nombre,
    correo: typeof d.correo === "string" ? d.correo : "",
    rol: d.rol,
    rol_etiqueta: typeof d.rol_etiqueta === "string" ? d.rol_etiqueta : "",
    estado: d.estado as EstadoUsuario,
    acciones: [...new Set(acciones)],
    motivo_sin_acciones: esDe(MOTIVOS_SIN_ACCIONES, d.motivo_sin_acciones) ? (d.motivo_sin_acciones as MotivoSinAcciones) : null,
  };
}

function filaOError(dato: unknown): UsuarioDiagnostico {
  const fila = filaDesde(dato);
  if (!fila) throw new Error("El usuario llegó con un formato inesperado.");
  return fila;
}

/** `empresa` del 200 del backoffice (D-6); un valor sin id o sin nombre se ignora (la cabecera queda en esqueleto). */
function empresaCabeceraDesde(dato: unknown): EmpresaCabecera | undefined {
  if (!dato || typeof dato !== "object") return undefined;
  const e = dato as Record<string, unknown>;
  if (typeof e.id !== "string" || typeof e.nombre !== "string" || !e.nombre.trim()) return undefined;
  return { id: e.id, nombre: e.nombre, nit: typeof e.nit === "string" && e.nit.trim() ? e.nit : null };
}

function listaDesde(dato: unknown): ListaUsuarios {
  const d = (dato ?? {}) as Record<string, unknown>;
  // Un cuerpo sin arreglo es un contrato roto, no una lista vacía: se muestra como error, nunca como «sin usuarios».
  if (!Array.isArray(d.usuarios)) throw new Error("La lista de usuarios llegó con un formato inesperado.");
  const roles = Array.isArray(d.roles_asignables) ? d.roles_asignables.filter((r): r is RolDiagnostico => esDe(ROLES_DIAGNOSTICO, r)) : [];
  const usuarios = d.usuarios.flatMap((u) => {
    const f = filaDesde(u);
    return f ? [f] : [];
  });
  const empresa = empresaCabeceraDesde(d.empresa);
  return {
    ...(empresa ? { empresa } : {}),
    pagina: entero(d.pagina, 1),
    tamano: entero(d.tamano, usuarios.length),
    total: entero(d.total, usuarios.length),
    roles_asignables: [...new Set(roles)],
    usuarios,
  };
}

function consulta(pares: [string, string | number | null | undefined][]): string {
  const q = new URLSearchParams();
  for (const [k, v] of pares) if (v !== null && v !== undefined && v !== "") q.append(k, String(v));
  return q.toString();
}

/** GET /usuarios (o el del backoffice): búsqueda recortada, rol y estado en explode, paginado. */
export async function listarUsuarios(empresa: string | null, filtros: FiltrosUsuarios, signal?: AbortSignal): Promise<ListaUsuarios> {
  const { pagina, tamano } = acotarPaginacion(filtros.pagina, filtros.tamano);
  const q = consulta([
    ["busqueda", filtros.busqueda.trim().slice(0, BUSQUEDA_MAXIMA)],
    ["rol", filtros.rol],
    ["estado", filtros.estado],
    ["pagina", pagina],
    ["tamano", tamano],
  ]);
  return listaDesde(await apiFetch<unknown>(`${rutaUsuarios(empresa)}?${q}`, { signal, cache: "no-store" }));
}

/**
 * POST …/invitaciones con solo { correo, nombre, rol } por código (SP-10): cualquier otro campo se descarta aquí. La
 * respuesta es el 202 uniforme (SP-15): la UI no distingue casos.
 */
export async function invitarUsuario(empresa: string | null, datos: InvitacionUsuario): Promise<void> {
  const cuerpo = { correo: datos.correo.trim(), nombre: datos.nombre.trim(), rol: datos.rol };
  await apiFetch<unknown>(`${rutaUsuarios(empresa)}/invitaciones`, { method: "POST", body: cuerpo });
}

/** PUT …/{usuario}/rol (SP-9=a: solo toca el rol de Diagnóstico). Devuelve la fila con las acciones recalculadas. */
export async function cambiarRolUsuario(empresa: string | null, usuario: string, rol: RolDiagnostico): Promise<UsuarioDiagnostico> {
  return filaOError(await apiFetch<unknown>(`${rutaUsuarios(empresa)}/${encodeURIComponent(usuario)}/rol`, { method: "PUT", body: { rol } }));
}

/** POST …/{usuario}/suspension: suspende solo en Diagnóstico (SP-12=a). */
export async function suspenderUsuario(empresa: string | null, usuario: string): Promise<UsuarioDiagnostico> {
  return filaOError(await apiFetch<unknown>(`${rutaUsuarios(empresa)}/${encodeURIComponent(usuario)}/suspension`, { method: "POST" }));
}

/** DELETE …/{usuario}/suspension: reactiva con el rol que tenía al suspenderse. */
export async function reactivarUsuario(empresa: string | null, usuario: string): Promise<UsuarioDiagnostico> {
  return filaOError(await apiFetch<unknown>(`${rutaUsuarios(empresa)}/${encodeURIComponent(usuario)}/suspension`, { method: "DELETE" }));
}

function empresaDesde(dato: unknown): EmpresaUsuarios | null {
  if (!dato || typeof dato !== "object") return null;
  const d = dato as Record<string, unknown>;
  if (typeof d.id !== "string" || typeof d.nombre !== "string") return null;
  return {
    id: d.id,
    nombre: d.nombre,
    nit: typeof d.nit === "string" && d.nit.trim() ? d.nit : null,
    estado: typeof d.estado === "string" ? d.estado : "ACTIVA",
    usuarios_diagnostico: entero(d.usuarios_diagnostico, 0),
    administradores_diagnostico: entero(d.administradores_diagnostico, 0),
  };
}

/** GET /backoffice/usuarios/empresas (solo SuperAdmin): compañías con Diagnóstico activo y sus conteos. Sin estado (H-U3). */
export async function listarEmpresasUsuarios(filtros: FiltrosEmpresas, signal?: AbortSignal): Promise<ListaEmpresasUsuarios> {
  const { pagina, tamano } = acotarPaginacion(filtros.pagina, filtros.tamano);
  const q = consulta([
    ["busqueda", filtros.busqueda.trim().slice(0, BUSQUEDA_MAXIMA)],
    ["usuarios", filtros.usuarios],
    ["pagina", pagina],
    ["tamano", tamano],
  ]);
  const d = ((await apiFetch<unknown>(`/backoffice/usuarios/empresas?${q}`, { signal, cache: "no-store" })) ?? {}) as Record<string, unknown>;
  if (!Array.isArray(d.empresas)) throw new Error("La lista de compañías llegó con un formato inesperado.");
  const empresas = d.empresas.flatMap((e) => {
    const x = empresaDesde(e);
    return x ? [x] : [];
  });
  return { pagina: entero(d.pagina, 1), tamano: entero(d.tamano, empresas.length), total: entero(d.total, empresas.length), empresas };
}

/** AP-9 (SF-6): texto fijo por `code` de las rutas de usuarios (contrato §3.3). Nunca el `message` del servidor. */
export const ERRORES_USUARIOS: Readonly<Record<string, string>> = {
  FILTRO_INVALIDO: "Uno de los filtros no es válido. Límpialos e intenta de nuevo.",
  PAGINACION_INVALIDA: "Esa página no existe. Vuelve a la primera página.",
  EMPRESA_NO_PERMITIDA: "No puedes elegir otra compañía desde aquí.",
  EMPRESA_NO_ENCONTRADA: "Esa compañía no existe o no tiene Diagnóstico activo.",
  SIN_EMPRESA: "Tu usuario no tiene una compañía asignada.",
  PRODUCTO_NO_HABILITADO: "Tu compañía no tiene Diagnóstico activo.",
  PLATAFORMA_NO_DISPONIBLE: "La plataforma de usuarios no responde. Intenta de nuevo en unos segundos.",
  PLATAFORMA_NO_CONFIGURADA: "La gestión de usuarios no está configurada en este ambiente. Avisa a soporte.",
  IDENTIDAD_NO_DISPONIBLE: "El servicio de compañías no responde. Intenta de nuevo en unos segundos.",
  CORREO_INVALIDO: "Escribe un correo electrónico válido.",
  NOMBRE_REQUERIDO: "Escribe el nombre completo.",
  ROL_NO_PERMITIDO: "Ese rol no se puede asignar en Diagnóstico.",
  ROL_NO_ASIGNABLE: "No puedes asignar ese rol. Solo un SuperAdmin da o quita el rol de Administrador de Diagnóstico.",
  DEMASIADAS_SOLICITUDES: "Hiciste muchas invitaciones seguidas. Espera unos minutos y vuelve a intentarlo.",
  ACCION_SOBRE_SI_MISMO: "No puedes cambiar tu propio usuario desde aquí.",
  OBJETIVO_PROTEGIDO: "Es un administrador de la compañía: su acceso a Diagnóstico se gestiona en el hub.",
  USUARIO_NO_ENCONTRADO: "No encontramos ese usuario. Recarga la lista.",
  USUARIO_SUSPENDIDO: "El usuario está suspendido: reactívalo primero.",
  INVITACION_PENDIENTE: "La invitación aún no se ha aceptado: no admite cambios.",
  ULTIMO_ADMINISTRADOR: "Debe quedar al menos un administrador de Diagnóstico activo en la compañía.",
  ROL_YA_VIGENTE: "El usuario ya tiene un rol de Diagnóstico vigente. Actualizamos la lista.",
};

/** Texto fijo de un error de usuarios: por `code`, 403 genérico o `respaldo`. Un 5xx sin `code` conocido, el respaldo. */
export function traducirErrorUsuarios(error: unknown, respaldo: string): string {
  return mensajeErrorFijo(error, respaldo, ERRORES_USUARIOS);
}

/** Código de un 403 de la lista (lleva a la página 403) o null. */
export function accesoNegadoUsuarios(error: unknown): string | null {
  return error instanceof ApiError && error.status === 403 ? error.code || "SIN_PERMISO" : null;
}

/** Campo de un 400/422 de invitar (`campo` del cuerpo o deducido del code). */
export function campoDeError(error: unknown): "correo" | "nombre" | "rol" | null {
  if (!(error instanceof ApiError)) return null;
  if (error.campo === "correo" || error.code === "CORREO_INVALIDO") return "correo";
  if (error.campo === "nombre" || error.code === "NOMBRE_REQUERIDO") return "nombre";
  if (error.campo === "rol" || error.code === "ROL_NO_PERMITIDO" || error.code === "ROL_NO_ASIGNABLE") return "rol";
  return null;
}
