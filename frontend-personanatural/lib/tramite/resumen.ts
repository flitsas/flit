// Paso Resumen (HU F5, reducida). Módulo puro, sin React ni red: arma, a partir de la definición del tipo y del borrador, lo que el
// ciudadano ve para revisar antes de enviar. Todo es solo lectura y sale de la definición: ningún tipo de trámite está nombrado aquí.
import type { ActorEstado, DefinicionTipo, Pendiente, Solicitud } from "@/lib/api/cliente";
import { formatearPesos } from "@/lib/formato";
import { documentoLegible } from "@/lib/tramite/actores";
import { grupoDePendiente, GRUPO_POR_SECCION, type GrupoPaso } from "@/lib/tramite/pasos";
import { DOC_COMPROBANTE_PAGO, tamanoLegible, TRANSFORMACIONES } from "@/lib/tramite/requisitos";

export interface FilaResumen {
  etiqueta: string;
  valor: string;
}

/** Un grupo de filas bajo un subtítulo opcional (por ejemplo, una persona dentro de «Actores»). */
export interface BloqueResumen {
  titulo?: string;
  filas: FilaResumen[];
}

export interface SeccionResumen {
  id: string;
  titulo: string;
  /** Paso al que lleva «Editar», o `null` si la sección no se edita (viene de la configuración del trámite). */
  editar: GrupoPaso | null;
  bloques: BloqueResumen[];
}

export const SIN_DATO = "—";

function documentoCompleto(tipo: string | null, numero: string | null): string {
  const digitos = (numero ?? "").replace(/\s/g, "");
  if (digitos === "") return SIN_DATO;
  return documentoLegible(tipo ?? "", digitos).trim() || SIN_DATO;
}

function textoOVacio(valor: string | null | undefined, vacio: string): string {
  const limpio = valor?.trim() ?? "";
  return limpio === "" ? vacio : limpio;
}

// ── Qué paso edita cada sección ─────────────────────────────────────────────────────────────────────────────────────

/** El paso tiene algo que pintar para este tipo (un paso sin secciones no se ofrece como destino de «Editar»). */
export const pasoTieneSecciones = (grupo: GrupoPaso, definicion: DefinicionTipo): boolean =>
  definicion.pasos.some((p) => p.sectionTypes.some((s) => GRUPO_POR_SECCION[s] === grupo));

// ── Secciones del resumen ───────────────────────────────────────────────────────────────────────────────────────────

function etiquetaDelRol(definicion: DefinicionTipo, actor: ActorEstado): string {
  const rol = definicion.actores.find((r) => r.rol === actor.rol);
  const base = rol?.etiqueta || actor.rol;
  return rol && rol.max > 1 ? `${base} ${actor.ordinal}` : base;
}

function bloqueDeActor(definicion: DefinicionTipo, actor: ActorEstado): BloqueResumen {
  return {
    titulo: etiquetaDelRol(definicion, actor),
    filas: [
      { etiqueta: "Documento", valor: documentoCompleto(actor.tipoDocumento, actor.numeroDocumento) },
      { etiqueta: "Nombre", valor: textoOVacio(actor.nombre, SIN_DATO) },
      { etiqueta: "Correo", valor: textoOVacio(actor.correo, SIN_DATO) },
      { etiqueta: "Celular", valor: textoOVacio(actor.celular, SIN_DATO) },
      { etiqueta: "Dirección", valor: textoOVacio(actor.direccion, "Sin registrar") },
      { etiqueta: "Ciudad", valor: textoOVacio(actor.ciudad, SIN_DATO) },
    ],
  };
}

/** Texto de un campo del tipo: las casillas guardan «true» o «false». */
const valorDeCampo = (valor: string | undefined): string => {
  const v = (valor ?? "").trim();
  if (v === "") return SIN_DATO;
  return v === "true" ? "Sí" : v === "false" ? "No" : v;
};

/**
 * Las secciones que se muestran, en el orden del wizard. Una sección sin datos no aparece (no deja huecos). `valorInformativo` es
 * la cotización que el ciudadano ya calculó en la barra lateral, si existe: solo se muestra, no hay cobro en el portal.
 */
export function armarResumen(definicion: DefinicionTipo, solicitud: Solicitud, valorInformativo?: number | null): SeccionResumen[] {
  const secciones: SeccionResumen[] = [];

  const tramite: FilaResumen[] = [
    { etiqueta: "Tipo de trámite", valor: definicion.name },
    { etiqueta: "Organismo de Tránsito", valor: solicitud.organismoNombre || SIN_DATO },
    { etiqueta: "Radicado del portal", valor: solicitud.codigoPublico },
  ];
  if (valorInformativo !== undefined && valorInformativo !== null) tramite.push({ etiqueta: "Valor del trámite (informativo)", valor: formatearPesos(valorInformativo) });
  secciones.push({ id: "tramite", titulo: "Trámite", editar: null, bloques: [{ filas: tramite }] });

  const vehiculo = solicitud.placa ?? solicitud.vin;
  if (vehiculo) {
    secciones.push({
      id: "vehiculo",
      titulo: "Vehículo",
      editar: pasoTieneSecciones("diagnostico", definicion) ? "diagnostico" : null,
      bloques: [{ filas: [{ etiqueta: solicitud.placa ? "Placa" : "VIN", valor: vehiculo }] }],
    });
  }

  const actores = [...solicitud.actores].sort((a, b) => a.rol.localeCompare(b.rol) || a.ordinal - b.ordinal);
  if (actores.length > 0) {
    secciones.push({ id: "actores", titulo: "Actores", editar: "actores", bloques: actores.map((a) => bloqueDeActor(definicion, a)) });
  }

  for (const paso of definicion.pasos) {
    if (paso.campos.length === 0) continue;
    const grupo = GRUPO_POR_SECCION[paso.sectionTypes[0] ?? ""] ?? "requisitos";
    secciones.push({
      id: `campos:${paso.clave}`,
      titulo: paso.titulo || "Datos del trámite",
      editar: grupo,
      bloques: [{ filas: paso.campos.map((c) => ({ etiqueta: c.etiqueta, valor: valorDeCampo(solicitud.fieldValues[c.fieldKey]) })) }],
    });
  }

  const comercial: FilaResumen[] = [];
  const valor = solicitud.datos.comercial?.valor;
  if (valor !== undefined && valor !== null && valor > 0) comercial.push({ etiqueta: "Valor comercial", valor: formatearPesos(valor) });
  const prenda = solicitud.datos.prenda;
  if (prenda) {
    comercial.push({ etiqueta: "Prenda", valor: prenda.tiene ? "Sí" : "No" });
    if (prenda.tiene) {
      comercial.push({ etiqueta: "Acreedor", valor: prenda.acreedor?.trim() || SIN_DATO });
      comercial.push({ etiqueta: "NIT del acreedor", valor: prenda.nitAcreedor?.trim() || SIN_DATO });
    }
  }
  if (comercial.length > 0) secciones.push({ id: "comercial", titulo: "Condiciones comerciales", editar: "requisitos", bloques: [{ filas: comercial }] });

  const adicionales: FilaResumen[] = [];
  const transformaciones = solicitud.datos.requisitos?.transformaciones ?? [];
  if (transformaciones.length > 0) {
    adicionales.push({ etiqueta: "Transformaciones", valor: transformaciones.map((c) => TRANSFORMACIONES.find((t) => t.codigo === c)?.etiqueta ?? c).join(", ") });
  }
  const observaciones = solicitud.datos.requisitos?.observaciones?.trim();
  if (observaciones) adicionales.push({ etiqueta: "Observaciones", valor: observaciones });
  if (adicionales.length > 0) secciones.push({ id: "adicionales", titulo: "Información adicional", editar: "requisitos", bloques: [{ filas: adicionales }] });

  // Solo el nombre del documento y del archivo: nunca su contenido. El comprobante de pago no hace parte de esta entrega.
  const documentos = solicitud.adjuntos.filter((a) => a.tipo !== DOC_COMPROBANTE_PAGO);
  if (documentos.length > 0) {
    secciones.push({
      id: "documentos",
      titulo: "Documentos cargados",
      editar: "requisitos",
      bloques: [
        {
          filas: documentos.map((a) => ({
            etiqueta: definicion.documentos.find((d) => d.docCode === a.tipo)?.nombre ?? a.tipo,
            valor: `${a.nombreArchivo} · ${tamanoLegible(a.tamanoBytes)}`,
          })),
        },
      ],
    });
  }

  return secciones;
}

// ── Pendientes ──────────────────────────────────────────────────────────────────────────────────────────────────────

/** Texto claro de algo que falta, a partir del código estable del back. */
export function textoPendiente(pendiente: Pendiente, definicion: DefinicionTipo): string {
  switch (pendiente.codigo) {
    case "actor_requerido": {
      const rol = definicion.actores.find((r) => r.rol === pendiente.referencia);
      return `Falta registrar a: ${rol?.etiqueta || pendiente.referencia || "una persona"}.`;
    }
    case "comercial_requerido":
      return "Falta el valor comercial.";
    case "prenda_acreedor_requerido":
      return "Falta el acreedor de la prenda.";
    case "prenda_nit_requerido":
      return "Falta el NIT del acreedor de la prenda.";
    case "documento_requerido": {
      const doc = definicion.documentos.find((d) => d.docCode === pendiente.referencia);
      return `Falta cargar el documento: ${doc?.nombre || pendiente.referencia || "un documento"}.`;
    }
    case "campo_requerido": {
      const campo = definicion.pasos.flatMap((p) => p.campos).find((c) => c.fieldKey === pendiente.referencia);
      return `Falta completar el campo: ${campo?.etiqueta || pendiente.referencia || "un dato del trámite"}.`;
    }
    case "comprobante_pago_requerido":
      return "El servicio pide un requisito que este portal todavía no puede completar.";
    default:
      return "Falta información por completar.";
  }
}

/** Paso al que llevar al ciudadano para resolver un pendiente, o `null` si el portal no tiene dónde corregirlo (o el paso está vacío). */
export function destinoDePendiente(pendiente: Pendiente, definicion: DefinicionTipo): GrupoPaso | null {
  const grupo = grupoDePendiente(pendiente, definicion);
  if (grupo === "envio" || grupo === "resumen") return null;
  return pasoTieneSecciones(grupo, definicion) ? grupo : null;
}
