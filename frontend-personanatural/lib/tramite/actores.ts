// Paso Actores (HU F3 y K5). Módulo puro, sin React ni red: arma las filas de actores por rol a partir de la definición del tipo y
// del borrador, valida su formato y decide qué falta. Ningún rol ni tipo de trámite está nombrado aquí: todo sale de la definición.
import type { ActorEstado, ActorRequest, ActorRol, DefinicionTipo, EstadoIdentidad, EstadoValidacion, ParteIdentidad } from "@/lib/api/cliente";
import { ciudadCanonica } from "@/lib/catalogos/ciudades";

export type TipoDocumento = "CC" | "CE" | "NIT";

/** Tipos del diseño de referencia. El NIT es persona jurídica: se ofrece (como en el diseño) pero el portal no lo admite. */
export const TIPOS_DOCUMENTO: readonly { valor: TipoDocumento; etiqueta: string }[] = [
  { valor: "CC", etiqueta: "Cédula de ciudadanía" },
  { valor: "CE", etiqueta: "Cédula de extranjería" },
  { valor: "NIT", etiqueta: "NIT" },
];

const PREFIJO_DOCUMENTO: Readonly<Record<string, string>> = { CC: "C.C.", CE: "C.E.", NIT: "NIT" };

/** «C.C. 1.020.456.789», como la ficha del propietario en el diseño de referencia. */
export function documentoLegible(tipo: string, numero: string): string {
  const prefijo = PREFIJO_DOCUMENTO[tipo] ?? tipo;
  const miles = numero.replace(/\D/g, "").replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  return miles ? `${prefijo} ${miles}` : prefijo;
}

/**
 * Si el paso 1 consultó el vehículo con un documento de persona natural, lo deja en la primera fila vacía
 * (el vendedor o titular). No marca la fila como consultada: el ciudadano sigue pulsando consultar para traer el nombre.
 */
export function sembrarDocumentoInicial(filas: readonly FilaActor[], documento: { tipo: string; numero: string } | undefined): FilaActor[] {
  const numero = documento?.numero.trim() ?? "";
  const tipo = TIPOS_DOCUMENTO.find((t) => t.valor === documento?.tipo)?.valor;
  if (!tipo || tipo === "NIT" || numero === "") return filas as FilaActor[];
  const destino = filas.find((f) => f.ordinal === 1 && f.numeroDocumento === "");
  if (!destino) return filas as FilaActor[];
  return filas.map((f) => (f.clave === destino.clave ? { ...f, tipoDocumento: tipo, numeroDocumento: numero } : f));
}

export const MENSAJE_PERSONA_JURIDICA = "Este trámite no admite personas jurídicas en este rol";
export const MENSAJE_CONSULTAR = "Consulta el documento de esta persona";
export const MENSAJE_ROL_NO_PERMITIDO = "Este rol no puede ser una persona natural en el portal, así que no se puede continuar con este trámite en línea.";

/** Lo que el ciudadano escribe de una persona, en memoria de la pantalla (nunca en el navegador ni en la URL). */
export interface FilaActor {
  /** Id local estable de la fila (no viaja al back). */
  clave: string;
  rol: string;
  /** Posición dentro del rol (1 a `max`): es la llave que el back usa para esta persona. */
  ordinal: number;
  tipoDocumento: TipoDocumento;
  numeroDocumento: string;
  nombre: string;
  correo: string;
  celular: string;
  direccion: string;
  ciudad: string;
  /** El documento ya se consultó (o ya estaba guardado): los demás campos quedan habilitados. */
  consultado: boolean;
  /** El nombre salió de la consulta de persona de la solicitud: queda de solo lectura (si no se encontró, se escribe a mano). */
  nombreConsultado: boolean;
  /** Dato mínimo de la consulta de conductor (solo lo que el BFF devuelve). */
  licencia: { estado: string | null; vigencia: string | null } | null;
}

export type CampoActor = "tipoDocumento" | "numeroDocumento" | "nombre" | "correo" | "celular" | "direccion" | "ciudad";
export type ErroresActor = Partial<Record<CampoActor, string>>;

// ── Formato ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/** Celular colombiano, igual que el BFF: 10 dígitos que empiezan por 3, o con el 57 delante. */
export const telefonoInvalido = (v: string): boolean => !/^3\d{9}$/.test(v) && !/^573\d{9}$/.test(v);

export const MENSAJE_CELULAR = "Ingresa un celular de 10 dígitos que empiece por 3";

export const correoInvalido = (v: string): boolean => {
  const c = v.trim();
  return c.length === 0 || c.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(c);
};

/** Cédulas: solo dígitos (4 a 15), igual que el back. */
export const documentoInvalido = (v: string): boolean => !/^\d{4,15}$/.test(v);

const soloDigitos = (v: string): string => v.replace(/\D/g, "");
export { soloDigitos };

/** Errores de una fila. `consultado = false` solo exige el documento: lo demás está deshabilitado hasta consultarlo. */
export function validarFila(fila: FilaActor, rol: ActorRol): ErroresActor {
  const e: ErroresActor = {};
  if (!rol.personaNaturalPermitida || fila.tipoDocumento === "NIT") e.numeroDocumento = MENSAJE_PERSONA_JURIDICA;
  else if (fila.numeroDocumento === "") e.numeroDocumento = "Escribe el número de documento";
  else if (documentoInvalido(fila.numeroDocumento)) e.numeroDocumento = "El número de documento debe tener entre 4 y 15 dígitos";
  else if (!fila.consultado) e.numeroDocumento = MENSAJE_CONSULTAR;
  if (!fila.consultado) return e;
  if (fila.nombre.trim() === "" || fila.nombre.trim().length > 150) e.nombre = "Escribe el nombre completo";
  if (telefonoInvalido(fila.celular)) e.celular = MENSAJE_CELULAR;
  if (correoInvalido(fila.correo)) e.correo = "Ingresa un correo electrónico válido";
  if (fila.direccion.trim().length > 200) e.direccion = "La dirección es demasiado larga";
  if (!ciudadCanonica(fila.ciudad)) e.ciudad = "Elige la ciudad";
  return e;
}

// ── Filas ───────────────────────────────────────────────────────────────────────────────────────────────────────────

let consecutivo = 0;
const claveNueva = (): string => `a-${++consecutivo}`;

export function filaVacia(rol: string, ordinal: number): FilaActor {
  return { clave: claveNueva(), rol, ordinal, tipoDocumento: "CC", numeroDocumento: "", nombre: "", correo: "", celular: "", direccion: "", ciudad: "", consultado: false, nombreConsultado: false, licencia: null };
}

function filaDeActor(a: ActorEstado): FilaActor {
  const tipo = a.tipoDocumento === "CE" ? "CE" : "CC";
  // Un actor que el back ya tiene guardado se tomó de una consulta antes: queda habilitado.
  return {
    clave: claveNueva(),
    rol: a.rol,
    ordinal: a.ordinal,
    tipoDocumento: tipo,
    numeroDocumento: a.numeroDocumento ?? "",
    nombre: a.nombre ?? "",
    correo: a.correo ?? "",
    celular: (a.celular ?? "").replace(/\D/g, ""),
    direccion: a.direccion ?? "",
    ciudad: a.ciudad ?? "",
    consultado: Boolean(a.numeroDocumento),
    nombreConsultado: false,
    licencia: null,
  };
}

/**
 * El portal pide una persona por rol. Si la definición trae copropietarios (hasta 4), aquí se ignoran:
 * el ciudadano ve un vendedor y un comprador, como el diseño.
 */
export function rolDeUnaPersona(rol: ActorRol): ActorRol {
  return { ...rol, min: Math.min(rol.min, 1), max: 1 };
}

/**
 * El catálogo parte el paso Actores en una sección `actor_form` por rol (clave `vendedor`, `comprador`).
 * Cada sección pinta solo ese rol. Si la clave no es un rol, se pintan todos (un solo formulario).
 */
export function actoresDeLaSeccion(definicion: DefinicionTipo, clavePaso: string | undefined): ActorRol[] {
  if (!clavePaso) return definicion.actores;
  const propios = definicion.actores.filter((a) => a.rol === clavePaso);
  return propios.length > 0 ? propios : definicion.actores;
}

/**
 * Filas iniciales: las del borrador (agrupadas por rol y ordenadas por posición) y, por cada rol, las vacías que falten hasta su
 * mínimo. Un rol que el borrador trae con más personas que el máximo de la definición se recorta al máximo.
 */
export function filasIniciales(definicion: DefinicionTipo, guardados: readonly ActorEstado[]): FilaActor[] {
  const filas: FilaActor[] = [];
  for (const definido of definicion.actores) {
    const rol = rolDeUnaPersona(definido);
    const delRol = guardados.filter((a) => a.rol === rol.rol).sort((a, b) => a.ordinal - b.ordinal).slice(0, Math.max(rol.max, 0)).map(filaDeActor);
    for (let i = delRol.length + 1; i <= rol.min; i++) delRol.push(filaVacia(rol.rol, ordinalLibre(delRol, rol)));
    filas.push(...delRol);
  }
  return filas;
}

/** Primera posición libre del rol (1 a `max`), o `max + 1` si no queda ninguna. */
export function ordinalLibre(filas: readonly FilaActor[], rol: ActorRol): number {
  const usados = new Set(filas.filter((f) => f.rol === rol.rol).map((f) => f.ordinal));
  for (let n = 1; n <= rol.max; n++) if (!usados.has(n)) return n;
  return rol.max + 1;
}

export const puedeAgregar = (filas: readonly FilaActor[], rol: ActorRol): boolean => rol.personaNaturalPermitida && filas.filter((f) => f.rol === rol.rol).length < rol.max;
export const puedeQuitar = (filas: readonly FilaActor[], rol: ActorRol): boolean => filas.filter((f) => f.rol === rol.rol).length > rol.min;

/**
 * Las personas que se enviarían ya están en el borrador, con el mismo documento y contacto.
 * Sirve para no repetir el guardado: un reintento limitado no debe trabar el paso.
 */
export function actoresYaGuardados(filas: readonly FilaActor[], guardados: readonly ActorEstado[]): boolean {
  const pendientes = actoresParaGuardar(filas);
  if (pendientes.length === 0) return true;
  return pendientes.every((a) =>
    guardados.some(
      (g) =>
        g.rol === a.rol &&
        g.ordinal === a.ordinal &&
        (g.tipoDocumento ?? "") === a.tipoDocumento &&
        (g.numeroDocumento ?? "") === a.numeroDocumento &&
        (g.nombre ?? "").trim() === a.nombre &&
        (g.correo ?? "").trim() === a.correo &&
        (g.celular ?? "").replace(/\D/g, "") === a.celular.replace(/\D/g, "") &&
        (g.direccion ?? "").trim() === (a.direccion ?? "").trim() &&
        (g.ciudad ?? "") === (ciudadCanonica(a.ciudad) ?? ""),
    ),
  );
}

/** Lo que se manda a PATCH /actores: las filas con documento, con el contacto ya normalizado. */
export function actoresParaGuardar(filas: readonly FilaActor[]): ActorRequest[] {
  return filas
    .filter((f) => f.consultado)
    .map((f) => {
      const ciudad = ciudadCanonica(f.ciudad);
      return {
        rol: f.rol,
        ordinal: f.ordinal,
        tipoDocumento: f.tipoDocumento,
        numeroDocumento: f.numeroDocumento,
        nombre: f.nombre.trim(),
        correo: f.correo.trim(),
        celular: f.celular,
        ...(f.direccion.trim() ? { direccion: f.direccion.trim() } : {}),
        ...(ciudad ? { ciudad } : {}),
      };
    });
}

/**
 * Pasa los errores por campo de un 422 (`actores[2].correo`) a la fila que corresponde. `indices` dice qué fila ocupó cada posición
 * del arreglo enviado.
 */
export function erroresDeServidor(errores: Readonly<Record<string, readonly string[]>> | undefined, indices: readonly string[]): Record<string, ErroresActor> {
  const salida: Record<string, ErroresActor> = {};
  for (const [llave, mensajes] of Object.entries(errores ?? {})) {
    const m = /^actores\[(\d+)\]\.(\w+)$/.exec(llave);
    if (!m) continue;
    const clave = indices[Number(m[1])];
    const campo = m[2] === "ordinal" || m[2] === "rol" ? "numeroDocumento" : (m[2] as CampoActor);
    if (clave && mensajes[0]) salida[clave] = { ...salida[clave], [campo]: mensajes[0] };
  }
  return salida;
}

// ── Identidad ───────────────────────────────────────────────────────────────────────────────────────────────────────

export const llaveParte = (rol: string, ordinal: number): string => `${rol}:${ordinal}`;

/** Estado de la validación de una fila: el sondeo manda; sin él, el que trae el borrador. */
export function estadoDeFila(fila: Pick<FilaActor, "rol" | "ordinal">, identidad: EstadoIdentidad | undefined, guardados: readonly ActorEstado[]): EstadoValidacion {
  const parte = identidad?.partes.find((p) => p.parte === fila.rol && p.ordinal === fila.ordinal);
  if (parte) return parte.estado;
  return guardados.find((a) => a.rol === fila.rol && a.ordinal === fila.ordinal)?.validacionEstado ?? "sin_solicitar";
}

export const enCurso = (estado: EstadoValidacion): boolean => estado === "pendiente" || estado === "pendiente_envio";

export function parteDe(fila: Pick<FilaActor, "rol" | "ordinal">, identidad: EstadoIdentidad | undefined): ParteIdentidad | undefined {
  return identidad?.partes.find((p) => p.parte === fila.rol && p.ordinal === fila.ordinal);
}

export const ETIQUETA_VALIDACION: Readonly<Record<string, string>> = {
  sin_solicitar: "pendiente",
  pendiente_envio: "enviando enlace",
  pendiente: "pendiente",
  aprobada: "aprobada",
  rechazada: "rechazada",
  vencida: "vencida",
};

export const TONO_VALIDACION = (estado: EstadoValidacion): "success" | "warning" | "info" => (estado === "aprobada" ? "success" : "warning");

/**
 * La validación se sigue mostrando y se puede enviar. No traba «Continuar» hasta que se vuelva a exigir:
 * con `true`, un rol con biometría sin identidad aprobada vuelve a impedir el paso.
 */
export const EXIGIR_IDENTIDAD_PARA_CONTINUAR = false;

/** La alerta «Falta validar la identidad» al enviar (HU #14069 AC2). Va junto con Identidad:ExigirAlRadicar en el BFF. */
export const EXIGIR_IDENTIDAD_AL_ENVIAR = true;

// ── Qué falta ───────────────────────────────────────────────────────────────────────────────────────────────────────

/** Roles con biometría cuya identidad aún no está aprobada. Vacío si no falta ninguna. */
export function motivoIdentidadPendiente(
  definicion: DefinicionTipo,
  filas: readonly FilaActor[],
  estadoDe: (fila: FilaActor) => EstadoValidacion,
): string | undefined {
  const sinAprobar = definicion.actores.filter((rol) => rol.biometria && filas.some((f) => f.rol === rol.rol && estadoDe(f) !== "aprobada"));
  if (sinAprobar.length === 0) return undefined;
  return `Falta validar la identidad de: ${sinAprobar.map((r) => r.etiqueta).join(", ")}.`;
}

/**
 * Primer motivo por el que el paso no está completo, o `undefined` si lo está. Cada rol se revisa en el orden de la definición:
 * mínimo de personas y datos válidos de cada una. La identidad aprobada solo traba el paso si
 * {@link EXIGIR_IDENTIDAD_PARA_CONTINUAR} está activo.
 */
export function motivoIncompleto(
  definicion: DefinicionTipo,
  filas: readonly FilaActor[],
  estadoDe: (fila: FilaActor) => EstadoValidacion,
): string | undefined {
  for (const rol of definicion.actores) {
    const delRol = filas.filter((f) => f.rol === rol.rol);
    if (!rol.personaNaturalPermitida && rol.min > 0) return MENSAJE_ROL_NO_PERMITIDO;
    if (delRol.length < rol.min) return `Registra a ${rol.etiqueta}.`;
    for (const f of delRol) {
      if (Object.keys(validarFila(f, rol)).length > 0) return `Completa los datos de ${rol.etiqueta}.`;
    }
  }
  if (!EXIGIR_IDENTIDAD_PARA_CONTINUAR) return undefined;
  return motivoIdentidadPendiente(definicion, filas, estadoDe);
}
