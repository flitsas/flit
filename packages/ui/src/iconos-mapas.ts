// Mapas de íconos del Manual UI Kit 2026, copiados de propuesta-2 (propuesta.js). Fuente única para todas las apps:
// las pantallas piden el ícono por CLAVE de módulo/estado, nunca eligen el glifo por su cuenta.
import type { NombreIcono } from "./iconos-kit";

/** Clave de módulo o de cifra → glifo. Un valor con "@" es un SVG suelto de /assets/kit/ (disco de color). */
export const MAPA_ICONO: Record<string, NombreIcono | `@${string}`> = {
 "auditoria": "portapapeles",
 "ayuda": "ayuda",
 "banners": "megafono",
 "biometria": "biometria",
 "calendario": "calendario",
 "causales": "causal",
 "colaboradores": "usuarios",
 "comparendos": "comparendos",
 "configuracion": "ajustes",
 "consulta-placa": "buscar",
 "contrasena": "candado",
 "diagnostico": "consulta",
 "documentos": "documento",
 "empresas": "empresas",
 "filtros": "filtro",
 "firmas": "firma",
 "fuentes-oficiales": "fuentes-oficiales",
 "fur": "fur",
 "generacion-documental": "archivo",
 "historial-placa": "historial-placa",
 "identidad": "identidad",
 "improntas": "impronta",
 "inicio": "carpeta",
 "integraciones": "integraciones",
 "mandatos": "mandato",
 "matricula": "@tramite-matricula",
 "notificaciones": "campana",
 "organismos": "banco",
 "pendientes": "espera",
 "perfil": "persona",
 "preasignacion": "placa",
 "procesos": "procesos",
 "productos": "rejilla",
 "reportes": "reportes",
 "revocatorias": "@estado-revocado",
 "roles": "rol",
 "seguridad": "escudo",
 "tramites": "tramites",
 "traspaso": "@tramite-traspaso",
 "trazabilidad": "ruta",
 "vencidos": "vencido",
 "reportes-detallados": "reporte-detallado",
 "reportes-ict": "reporte-ict",
 "quipux": "radicacion",
 "log-qx": "log-mensajes",
 "log-ict": "log-llamadas",
 "tipos-tramite": "tipos-tramite",
 "tiempo": "cronometro",
 "finalizados": "finalizado",
 "en-curso": "avance",
 "rapido": "medidor",
 "devoluciones": "devolucion",
 "ingresos": "ingreso",
 "celular": "celular",
 "destacado": "medalla",
 "llamadas": "intercambio",
 "marca": "destello",
 "cargar": "archivo-subir",
 "correcciones": "lapiz",
 "errores": "nube-alerta",
 "simulador": "pesos",
 "seguimiento": "ruta"
} as Record<string, NombreIcono | `@${string}`>;

/** Cifras de Reportes por pestaña: claves de MAPA_ICONO en el orden de las tarjetas. */
export const ICONOS_REPORTE: Record<string, string[]> = {
 "resumen": [
  "tramites",
  "finalizados",
  "tiempo",
  "causales"
 ],
 "operacion": [
  "en-curso",
  "firmas",
  "diagnostico",
  "documentos"
 ],
 "organismo": [
  "organismos",
  "rapido",
  "pendientes",
  "devoluciones"
 ],
 "uso": [
  "colaboradores",
  "ingresos",
  "celular",
  "firmas"
 ],
 "productividad": [
  "perfil",
  "destacado",
  "tiempo",
  "correcciones"
 ]
};

/** Estado → disco de /assets/kit/estado-{x}.svg. */
export const ICONO_ESTADO: Record<string, string> = {
 "borrador": "borrador",
 "validacion": "preparado",
 "radicado": "entregado",
 "rechazado": "rechazado",
 "preparado": "preparado",
 "preasignacion": "sin-placa",
 "asignado": "placa",
 "entregado": "entregado",
 "subsanacion": "subsanado",
 "aprobado": "aprobado",
 "revocado": "revocado",
 "anulado": "anulado",
 "rechazada": "rechazado",
 "negada": "rechazado"
};

/** Estados sin disco propio → glifo blanco sobre disco del tono. */
export const GLIFO_ESTADO: Record<string, NombreIcono> = {
 "pendiente": "espera",
 "firma": "pluma",
 "finalizado": "finalizado",
 "aprobada": "check",
 "validada": "escudo",
 "fallida": "distinto",
 "activa": "encendido",
 "activo": "encendido",
 "invitado": "correo",
 "suspendido": "pausa",
 "inactivo": "menos",
 "publicado": "ojo",
 "programado": "reloj",
 "flit": "radicacion",
 "quipux": "externo",
 "listo": "archivo",
 "generando": "avance",
 "error": "alerta",
 "ok": "check",
 "reintento": "reintento",
 "habilitado": "asistente",
 "apagado": "asistente-apagado",
 "confirmado": "doble-check",
 "diferencia": "distinto",
 "completo": "portapapeles",
 "incompleto": "lista",
 "normal": "punto",
 "sensible": "rol",
 "bloqueado": "candado",
 "espera": "archivo-subir",
 "materializado": "tramites",
 "descartado": "papelera",
 "descuento": "porcentaje",
 "pagado": "pesos",
 "cobro": "mazo",
 "aldia": "carro",
 "alerta": "exclamacion",
 "libre": "candado-abierto",
 "usada": "placa",
 "reservada": "marcador"
} as Record<string, NombreIcono>;

/** Tono → SVG de respaldo cuando el estado no tiene ícono. */
export const ICONO_TONO: Record<string, string> = {
 "ok": "aviso-ok",
 "warn": "estado-pendiente",
 "danger": "estado-rechazado",
 "info": "aviso-info",
 "": "estado-borrador"
};

/** Ruta pública del SVG del kit. Las 4 apps copian assets/kit en public/assets/kit/. */
export const rutaKit = (nombre: string) => `/assets/kit/${nombre.replace(/^@/, "")}.svg`;
