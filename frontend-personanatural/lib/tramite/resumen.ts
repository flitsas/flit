// Paso Resumen (HU F5, reducida). Módulo puro, sin React ni red: arma, a partir de la definición del tipo y del borrador, lo que el
// ciudadano ve para revisar antes de enviar. Todo es solo lectura y sale de la definición: ningún tipo de trámite está nombrado aquí.
import type { ActorEstado, DefinicionTipo, EstadoIdentidad, Pendiente, Solicitud } from "@/lib/api/cliente";
import { formatearFecha, formatearPesos } from "@/lib/formato";
import { esCorreoTecnico } from "@/lib/punto/contacto";
import { documentoLegible, estadoDeFila } from "@/lib/tramite/actores";
import {
  CLAVE_DESTINO_POR_DEFECTO,
  CLAVE_NOMBRE_DESTINO,
  declaracionDestino,
  declaracionesActivas,
  declaracionesDe,
  nombreDelDocumento,
  opcionElegida,
  soportesDe,
  valorActualEnRunt,
  type ValoresRunt,
} from "@/lib/tramite/declaraciones";
import { esEditable, grupoDePendiente, GRUPO_POR_SECCION, type GrupoPaso } from "@/lib/tramite/pasos";
import { nombreVisibleDeAdjunto, type VehiculoRecordado } from "@/lib/tramite/presentacion";
import { DOC_COMPROBANTE_PAGO, tamanoLegible, TRANSFORMACIONES } from "@/lib/tramite/requisitos";
import type { TonoBadge } from "@/lib/tramite/tonos";

export interface FilaResumen {
  etiqueta: string;
  valor: string;
  /** Si viene, el valor se pinta como insignia con ese tono (estado de la identidad). */
  tono?: TonoBadge;
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

  const declaradas = declaracionesDe(definicion).filter((x) => (solicitud.fieldValues[x.fieldKey] ?? "").trim() !== "");
  if (declaradas.length > 0) {
    secciones.push({
      id: "declaraciones",
      titulo: "Declaraciones del trámite",
      editar: "requisitos",
      bloques: [
        {
          filas: declaradas.map((x) => {
            const valorDeclarado = solicitud.fieldValues[x.fieldKey] ?? "";
            return { etiqueta: x.etiqueta, valor: x.opciones.find((o) => o.codigo === valorDeclarado)?.etiqueta ?? valorDeclarado };
          }),
        },
      ],
    });
  }

  const comercial: FilaResumen[] = [];
  const valor = solicitud.datos.comercial?.valor;
  if (valor !== undefined && valor !== null && valor > 0) comercial.push({ etiqueta: "Valor comercial", valor: formatearPesos(valor) });
  const prenda = solicitud.datos.prenda;
  if (prenda) {
    const levanta = prenda.tiene && prenda.decision === "levantar";
    comercial.push({ etiqueta: "Prenda", valor: levanta ? "Sí, se levantará" : prenda.tiene ? "Sí" : "No" });
    if (levanta) comercial.push({ etiqueta: "Entidad ante la que se levantó", valor: prenda.entidadLevantamiento?.trim() || SIN_DATO });
    if (prenda.tiene && !levanta) {
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
    case "prenda_decision_requerida":
      return "Falta decidir qué se hará con la prenda.";
    case "prenda_acreedor_requerido":
      return "Falta el acreedor de la prenda.";
    case "prenda_entidad_requerida":
      return "Falta la entidad ante la que se levantó la prenda.";
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
    case "declaracion_requerida": {
      const declaracion = (definicion.declaraciones ?? []).find((x) => x.clave === pendiente.referencia);
      return `Falta completar: ${declaracion?.etiqueta || pendiente.referencia || "un dato del trámite"}.`;
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

// ── Lo declarado en Requisitos, para la tarjeta «Datos del trámite» del paso Resumen ─────────────────────────────────

/**
 * Una fila por cada declaración activa con valor (etiqueta de la declaración y valor legible: la etiqueta de la opción elegida o el
 * valor del catálogo) y, bajo ella, una fila por cada soporte que pide ese valor y ya está cargado («Soporte cambio color» → «Cargado»).
 * La secretaría de destino sale con el nombre que el servidor guardó, si lo hay.
 */
export function filasDeDeclaraciones(definicion: DefinicionTipo, solicitud: Solicitud): FilaResumen[] {
  const transformaciones = solicitud.datos.requisitos?.transformaciones ?? [];
  const filas: FilaResumen[] = [];
  for (const decl of declaracionesActivas(definicion, transformaciones)) {
    const valor = (solicitud.fieldValues[decl.fieldKey] ?? "").trim();
    if (valor === "") continue;
    filas.push({ etiqueta: decl.etiqueta, valor: opcionElegida(decl, valor)?.etiqueta ?? valor });
    for (const soporte of soportesDe(decl, valor, definicion)) {
      if (solicitud.adjuntos.some((a) => a.tipo === soporte.docCode)) filas.push({ etiqueta: soporte.nombre, valor: "Cargado" });
    }
  }
  const destino = declaracionDestino(definicion);
  const destinoClave = destino?.fieldKey || CLAVE_DESTINO_POR_DEFECTO;
  if (destino && (solicitud.fieldValues[destinoClave] ?? "").trim() !== "") {
    const nombre = (solicitud.fieldValues[CLAVE_NOMBRE_DESTINO] ?? "").trim();
    filas.push({ etiqueta: destino.etiqueta || "Organismo de destino", valor: nombre || SIN_DATO });
  }
  return filas;
}

// ── Resumen previo al envío: «Datos del trámite» por secciones ───────────────────────────────────────────────────────

export interface ContextoResumen {
  canal?: "web" | "punto";
  /** Lo que el diagnóstico ya consultó del vehículo; sin esto no se pinta la sección (no se consulta de nuevo). */
  vehiculo?: VehiculoRecordado | null;
  valoresRunt?: ValoresRunt | null;
  /** Estado real de la identidad (GET identidad): manda sobre el que trae el borrador. */
  identidad?: EstadoIdentidad | null;
}

const limpio = (v: string | null | undefined): string => (v ?? "").trim();

/** Solo las filas con valor: lo que no llega no se pinta ni deja huecos. */
const conValor = (pares: ReadonlyArray<readonly [string, string | null | undefined]>): FilaResumen[] =>
  pares.flatMap(([etiqueta, valor]) => (limpio(valor) === "" ? [] : [{ etiqueta, valor: limpio(valor) }]));

const TEXTO_IDENTIDAD: Readonly<Record<string, { texto: string; tono: TonoBadge }>> = {
  aprobada: { texto: "Validada", tono: "success" },
  rechazada: { texto: "Rechazada", tono: "warning" },
  vencida: { texto: "Vencida", tono: "warning" },
};

/** «Validada» solo si el estado real es aprobada; cualquier otro (sin solicitar, en curso) es «Pendiente». */
export function textoIdentidad(estado: string): { texto: string; tono: TonoBadge } {
  return TEXTO_IDENTIDAD[estado] ?? { texto: "Pendiente", tono: "warning" };
}

function valorDeCampoLegible(tipo: string, valor: string): string {
  if (valor === "true") return "Sí";
  if (valor === "false") return "No";
  return /fecha|date/i.test(tipo) ? (formatearFecha(valor) ?? valor) : valor;
}

/** Campos del tipo que solo repiten la placa del trámite o el documento del titular: ya salen en «Trámite» y «Titular y actores». */
const CLAVES_REPETIDAS: ReadonlySet<string> = new Set(["plate", "owner_document_type", "owner_document_number"]);

function filasPropias(definicion: DefinicionTipo, solicitud: Solicitud, ctx: ContextoResumen): FilaResumen[] {
  const filas: FilaResumen[] = [];
  const transformaciones = solicitud.datos.requisitos?.transformaciones ?? [];

  const valorComercial = solicitud.datos.comercial?.valor;
  if (valorComercial !== undefined && valorComercial !== null && valorComercial > 0) filas.push({ etiqueta: "Valor comercial", valor: formatearPesos(valorComercial) });

  const prenda = solicitud.datos.prenda;
  if (prenda) {
    const levanta = prenda.tiene && prenda.decision === "levantar";
    filas.push({ etiqueta: "Prenda", valor: levanta ? "Sí, se levantará" : prenda.tiene ? "Sí" : "No" });
    if (prenda.tiene) filas.push(...conValor([["Acreedor", prenda.acreedor], ["NIT del acreedor", prenda.nitAcreedor]]));
    if (levanta) filas.push(...conValor([["Entidad ante la que se levantó", prenda.entidadLevantamiento]]));
  }

  // Declaraciones del tipo (color, combustible, carrocería, blindaje, causal…): lo actual en el RUNT y lo declarado, con etiquetas legibles.
  for (const decl of declaracionesActivas(definicion, transformaciones)) {
    const valor = limpio(solicitud.fieldValues[decl.fieldKey]);
    if (valor === "") continue;
    const actual = valorActualEnRunt(decl, ctx.valoresRunt);
    if (actual) filas.push({ etiqueta: actual.etiqueta, valor: actual.valor });
    filas.push({ etiqueta: decl.etiqueta, valor: opcionElegida(decl, valor)?.etiqueta ?? valor });
  }
  const destino = declaracionDestino(definicion);
  if (destino && limpio(solicitud.fieldValues[destino.fieldKey || CLAVE_DESTINO_POR_DEFECTO]) !== "") {
    filas.push(...conValor([[destino.etiqueta || "Organismo de destino", solicitud.fieldValues[CLAVE_NOMBRE_DESTINO]]]));
  }

  // Campos del tipo (excepto los que ya salieron como declaración).
  const yaDeclarados = new Set(declaracionesDe(definicion).flatMap((d) => [d.fieldKey, ...d.fieldKeysExtra]));
  const vistos = new Set<string>();
  for (const campo of definicion.pasos.flatMap((p) => p.campos)) {
    if (yaDeclarados.has(campo.fieldKey) || vistos.has(campo.fieldKey)) continue;
    vistos.add(campo.fieldKey);
    if (CLAVES_REPETIDAS.has(campo.fieldKey)) continue;
    const valor = limpio(solicitud.fieldValues[campo.fieldKey]);
    if (valor !== "") filas.push({ etiqueta: campo.etiqueta, valor: valorDeCampoLegible(campo.tipo, valor) });
  }

  if (transformaciones.length > 0) {
    filas.push({ etiqueta: "Trámites complementarios", valor: transformaciones.map((c) => TRANSFORMACIONES.find((t) => t.codigo === c)?.etiqueta ?? c).join(", ") });
  }
  filas.push(...conValor([["Observaciones", solicitud.datos.requisitos?.observaciones]]));
  return filas;
}

/**
 * Secciones de «Datos del trámite» del Resumen (antes de enviar), todas a partir de la definición y del borrador. Una fila o sección
 * sin datos no se incluye. Si la solicitud ya no es editable (ya se envió) solo queda la sección «Trámite»: ni personas, ni
 * documentos, ni nombres de archivo (HU #14049). El correo técnico del punto nunca se muestra.
 */
export function armarDatosResumen(definicion: DefinicionTipo, solicitud: Solicitud, ctx: ContextoResumen = {}): SeccionResumen[] {
  const punto = ctx.canal === "punto";
  const editable = esEditable(solicitud.estado);
  const destinoEditar = (grupo: GrupoPaso): GrupoPaso | null => (editable && pasoTieneSecciones(grupo, definicion) ? grupo : null);
  const secciones: SeccionResumen[] = [];
  const agregar = (id: string, titulo: string, editar: GrupoPaso | null, bloques: BloqueResumen[]) => {
    const con = bloques.filter((b) => b.filas.length > 0);
    if (con.length > 0) secciones.push({ id, titulo, editar, bloques: con });
  };

  agregar("tramite", "Trámite", null, [
    {
      filas: conValor([
        ["Tipo de trámite", definicion.name],
        ["Organismo de tránsito", solicitud.organismoNombre],
        ["Placa", solicitud.placa],
        ["VIN", solicitud.vin],
      ]),
    },
  ]);
  if (!editable) return secciones;

  const v = ctx.vehiculo;
  if (v) {
    agregar("vehiculo", "Vehículo", punto ? null : destinoEditar("diagnostico"), [
      { filas: conValor([["Marca", v.marca], ["Línea", v.linea], ["Modelo", v.modeloAno], ["Clase", v.clase], ["Color", v.color], ["Servicio", v.tipoServicio]]) },
    ]);
  }

  const actores = [...solicitud.actores].sort((a, b) => a.rol.localeCompare(b.rol) || a.ordinal - b.ordinal);
  agregar(
    "actores",
    "Titular y actores",
    destinoEditar("actores"),
    actores.map((a) => ({
      titulo: etiquetaDelRol(definicion, a),
      filas: conValor([
        ["Nombre", a.nombre],
        ["Documento", limpio(a.numeroDocumento) === "" ? "" : documentoCompleto(a.tipoDocumento, a.numeroDocumento)],
        ["Teléfono", a.celular],
        ["Correo", punto && esCorreoTecnico(a.correo) ? "" : a.correo],
        ["Ciudad", a.ciudad],
        ["Dirección", a.direccion],
      ]),
    })),
  );

  const rolesConIdentidad = new Set(definicion.actores.filter((r) => r.biometria).map((r) => r.rol));
  if (rolesConIdentidad.size > 0 || definicion.flags.requiereBiometria) {
    const partes = rolesConIdentidad.size > 0 ? actores.filter((a) => rolesConIdentidad.has(a.rol)) : actores;
    agregar("identidad", "Validación de identidad", destinoEditar("actores"), [
      {
        filas: partes.map((a) => {
          const estado = textoIdentidad(estadoDeFila(a, ctx.identidad ?? undefined, solicitud.actores));
          return { etiqueta: etiquetaDelRol(definicion, a), valor: estado.texto, tono: estado.tono };
        }),
      },
    ]);
  }

  agregar("propios", "Información del trámite", destinoEditar("requisitos"), [{ filas: filasPropias(definicion, solicitud, ctx) }]);

  // Solo el nombre del documento y del archivo (nunca su contenido). El comprobante de pago no hace parte de esta entrega.
  agregar("documentos", "Documentos cargados", destinoEditar("requisitos"), [
    {
      filas: solicitud.adjuntos
        .filter((a) => a.tipo !== DOC_COMPROBANTE_PAGO)
        .map((a) => ({ etiqueta: nombreDelDocumento(definicion, a.tipo), valor: nombreVisibleDeAdjunto(a, solicitud.solicitudId) })),
    },
  ]);

  return secciones;
}
