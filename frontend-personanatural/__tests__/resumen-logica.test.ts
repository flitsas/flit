import { describe, expect, it } from "vitest";
import type { ActorEstado, DeclaracionTipo, DefinicionTipo, EstadoIdentidad, Solicitud } from "@/lib/api/cliente";
import { armarDatosResumen, armarResumen, destinoDePendiente, pasoTieneSecciones, textoPendiente, type SeccionResumen } from "@/lib/tramite/resumen";
import { adjunto, definicionCompleta, solicitudCompleta } from "./fixtures-envio";
import { definicionDe } from "./fixtures-wizard";

const filasDe = (secciones: SeccionResumen[], id: string) => secciones.find((s) => s.id === id)?.bloques.flatMap((b) => b.filas) ?? [];
const valorDe = (secciones: SeccionResumen[], id: string, etiqueta: string) => filasDe(secciones, id).find((f) => f.etiqueta === etiqueta)?.valor;

const COLOR: DeclaracionTipo = {
  clave: "color",
  control: "seleccion_catalogo",
  etiqueta: "Color nuevo",
  obligatoria: false,
  opciones: [],
  fieldKey: "vehicle_color",
  fieldKeysExtra: ["cambio_color"],
  docCodeSoporte: "soporte_cambio_color",
  soporteObligatorio: true,
  catalogo: "colores",
  activadaPor: "CAMBIO_COLOR",
};
const CAUSAL: DeclaracionTipo = {
  ...COLOR,
  clave: "causal",
  control: "seleccion",
  etiqueta: "Causal de cancelación",
  fieldKey: "cancelacion_causal",
  fieldKeysExtra: [],
  docCodeSoporte: "",
  soporteObligatorio: false,
  catalogo: "",
  activadaPor: "",
  opciones: [{ codigo: "PERDIDA_TOTAL", etiqueta: "Pérdida total", docCodes: [], exigeSoporte: false }],
};
const conDeclaraciones = (declaraciones: DeclaracionTipo[], extra: Partial<DefinicionTipo> = {}): DefinicionTipo => ({ ...definicionCompleta(), declaraciones, ...extra });
const actorDe = (rol: string, extra: Partial<ActorEstado> = {}): ActorEstado => ({
  rol,
  ordinal: 1,
  validacionEstado: "sin_solicitar",
  tipoDocumento: "CC",
  numeroDocumento: "1020456789",
  nombre: "ANA RUIZ",
  correo: null,
  celular: null,
  direccion: null,
  ...extra,
});
const identidad = (partes: Array<[string, EstadoIdentidad["partes"][number]["estado"]]>): EstadoIdentidad => ({
  solicitudId: "s-1",
  completa: partes.every(([, e]) => e === "aprobada"),
  partes: partes.map(([parte, estado]) => ({ parte, ordinal: 1, estado, puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 })),
});

describe("«Datos del trámite» del Resumen por secciones", () => {
  it("levantar prenda: muestra la prenda con su entidad y los documentos cargados con su etiqueta y archivo", () => {
    const d = { ...definicionCompleta(), documentos: [...definicionCompleta().documentos, { docCode: "prenda_levantamiento", nombre: "Levantamiento de prenda", obligatorio: true, orden: 3, instruccionCargue: "", condicion: "" }] };
    const s = solicitudCompleta({
      datos: { prenda: { tiene: true, decision: "levantar", acreedor: null, nitAcreedor: null, entidadLevantamiento: "BANCO UNO" }, comercial: { valor: 45_000_000 } },
      adjuntos: [adjunto("a1", "prenda_levantamiento", "levantamiento.pdf"), adjunto("a2", "comprobante_pago", "soporte.pdf")],
    });
    const secciones = armarDatosResumen(d, s);

    expect(valorDe(secciones, "propios", "Prenda")).toBe("Sí, se levantará");
    expect(valorDe(secciones, "propios", "Entidad ante la que se levantó")).toBe("BANCO UNO");
    expect(valorDe(secciones, "propios", "Acreedor")).toBeUndefined();
    expect(filasDe(secciones, "documentos")).toEqual([{ etiqueta: "Levantamiento de prenda", valor: "levantamiento.pdf" }]);
  });

  it("prenda vigente: acreedor y NIT", () => {
    const secciones = armarDatosResumen(definicionCompleta(), solicitudCompleta({ datos: { prenda: { tiene: true, acreedor: "BANCO DE PRUEBA", nitAcreedor: "900123456", decision: "registrar" } } }));

    expect(valorDe(secciones, "propios", "Acreedor")).toBe("BANCO DE PRUEBA");
    expect(valorDe(secciones, "propios", "NIT del acreedor")).toBe("900123456");
  });

  it("cambio de color: muestra el color actual del RUNT y el nuevo, y la causal con su etiqueta, no su código", () => {
    const s = solicitudCompleta({
      datos: { requisitos: { transformaciones: ["CAMBIO_COLOR"], observaciones: null } },
      fieldValues: { vehicle_color: "ROJO", cancelacion_causal: "PERDIDA_TOTAL" },
    });
    const secciones = armarDatosResumen(conDeclaraciones([COLOR, CAUSAL]), s, { valoresRunt: { color: "AZUL" } });

    expect(valorDe(secciones, "propios", "Color actual en el RUNT")).toBe("AZUL");
    expect(valorDe(secciones, "propios", "Color nuevo")).toBe("ROJO");
    expect(valorDe(secciones, "propios", "Causal de cancelación")).toBe("Pérdida total");
    expect(valorDe(secciones, "propios", "Trámites complementarios")).toBeDefined();
  });

  it("los campos del tipo salen con su etiqueta y las casillas como Sí/No", () => {
    const d = definicionCompleta();
    const campos = d.pasos.flatMap((p) => p.campos);
    expect(campos.length).toBeGreaterThan(0);
    const secciones = armarDatosResumen(d, solicitudCompleta({ fieldValues: { cilindraje: "1600", color: "true" } }));

    expect(valorDe(secciones, "propios", "Cilindraje")).toBe("1600");
    expect(valorDe(secciones, "propios", "Color")).toBe("Sí");
  });

  it("en el punto el correo técnico no aparece; en el web, el correo real sí", () => {
    const s = solicitudCompleta({ actores: [actorDe("vendedor", { correo: "validaciones+punto-abc123@flit.co" }), actorDe("comprador", { correo: "luis@mail.co", nombre: "LUIS" })] });
    const punto = armarDatosResumen(definicionCompleta(), s, { canal: "punto" });
    const bloques = punto.find((x) => x.id === "actores")?.bloques ?? [];

    expect(JSON.stringify(punto)).not.toContain("validaciones+punto-");
    expect(bloques.find((b) => b.titulo === "Propietario (vendedor)")?.filas.some((f) => f.etiqueta === "Correo")).toBe(false);
    expect(bloques.find((b) => b.titulo === "Comprador")?.filas.find((f) => f.etiqueta === "Correo")?.valor).toBe("luis@mail.co");
  });

  it("identidad: la real (aprobada o pendiente) manda sobre la copia del borrador", () => {
    const s = solicitudCompleta({ actores: [actorDe("vendedor", { validacionEstado: "pendiente" }), actorDe("comprador", { validacionEstado: "pendiente" })] });
    const real = armarDatosResumen(definicionCompleta(), s, { identidad: identidad([["vendedor", "aprobada"], ["comprador", "pendiente"]]) });
    const sinLectura = armarDatosResumen(definicionCompleta(), s);

    expect(valorDe(real, "identidad", "Propietario (vendedor)")).toBe("Validada");
    expect(valorDe(real, "identidad", "Comprador")).toBe("Pendiente");
    expect(filasDe(real, "identidad").map((f) => f.tono)).toEqual(["warning", "success"]);
    expect(valorDe(sinLectura, "identidad", "Comprador")).toBe("Pendiente");
  });

  it("sin roles que validen identidad no hay sección de identidad", () => {
    const d = { ...definicionCompleta(), actores: definicionCompleta().actores.map((r) => ({ ...r, biometria: false })), flags: { ...definicionCompleta().flags, requiereBiometria: false } };

    expect(armarDatosResumen(d, solicitudCompleta()).some((x) => x.id === "identidad")).toBe(false);
  });

  it("lo que no llega no se pinta: ni filas vacías ni secciones sin datos", () => {
    const vacio: Solicitud = solicitudCompleta({
      organismoNombre: "",
      placa: null,
      vin: null,
      datos: {},
      fieldValues: {},
      adjuntos: [],
      actores: [actorDe("vendedor", { nombre: "  ", numeroDocumento: null, ciudad: "" })],
    });
    const secciones = armarDatosResumen(definicionCompleta(), vacio, { vehiculo: { marca: "", linea: null } });

    expect(secciones.some((x) => x.id === "vehiculo" || x.id === "documentos" || x.id === "propios" || x.id === "actores")).toBe(false);
    for (const f of secciones.flatMap((x) => x.bloques.flatMap((b) => b.filas))) {
      expect(f.valor.trim()).not.toBe("");
      expect(f.valor).not.toBe("—");
    }
  });

  it("vehículo solo con lo que llegó, sin inventar", () => {
    const secciones = armarDatosResumen(definicionCompleta(), solicitudCompleta(), { vehiculo: { marca: "MAZDA", linea: "3", modeloAno: "2020", clase: null, color: " ", tipoServicio: undefined } });

    expect(filasDe(secciones, "vehiculo")).toEqual([
      { etiqueta: "Marca", valor: "MAZDA" },
      { etiqueta: "Línea", valor: "3" },
      { etiqueta: "Modelo", valor: "2020" },
    ]);
    expect(armarDatosResumen(definicionCompleta(), solicitudCompleta()).some((x) => x.id === "vehiculo")).toBe(false);
  });

  it("«Editar» lleva al paso de cada sección, y el vehículo no se edita en el punto", () => {
    const web = armarDatosResumen(definicionCompleta(), solicitudCompleta(), { vehiculo: { marca: "MAZDA" } });
    const punto = armarDatosResumen(definicionCompleta(), solicitudCompleta(), { canal: "punto", vehiculo: { marca: "MAZDA" } });
    const editar = (x: SeccionResumen[]) => Object.fromEntries(x.map((s) => [s.id, s.editar]));

    expect(editar(web)).toMatchObject({ tramite: null, vehiculo: "diagnostico", actores: "actores", identidad: "actores", propios: "requisitos", documentos: "requisitos" });
    expect(editar(punto).vehiculo).toBeNull();
  });

  it("Habeas Data: con la solicitud ya enviada solo queda el trámite; ni personas ni archivos", () => {
    const secciones = armarDatosResumen(definicionCompleta(), solicitudCompleta({ estado: "en_cola" }), { vehiculo: { marca: "MAZDA" } });
    const texto = JSON.stringify(secciones);

    expect(secciones.map((s) => s.id)).toEqual(["tramite"]);
    expect(texto).not.toMatch(/ANA RUIZ|LUIS PEREZ|paz-y-salvo|1020456789|52987654|luis\.perez|MAZDA/);
    expect(secciones.every((s) => s.editar === null)).toBe(true);
  });
});

describe("datos del actor en el resumen", () => {
  it("documento, correo, celular, dirección y ciudad se ven completos", () => {
    const actores = armarResumen(definicionCompleta(), solicitudCompleta()).find((s) => s.id === "actores");
    const comprador = actores?.bloques.find((b) => b.titulo === "Comprador");
    const valor = (etiqueta: string) => comprador?.filas.find((f) => f.etiqueta === etiqueta)?.valor;

    expect(valor("Documento")).toBe("C.C. 52.987.654");
    expect(valor("Nombre")).toBe("LUIS PEREZ");
    expect(valor("Correo")).toBe("luis.perez@mail.co");
    expect(valor("Celular")).toBe("3109876543");
    expect(valor("Dirección")).toBe("Calle 1 # 2-3");
    expect(valor("Ciudad")).toBe("—");
  });
});

describe("pendientes en lenguaje claro", () => {
  const d = definicionCompleta();

  it("cada código tiene su texto, con el nombre del rol, del documento o del campo", () => {
    expect(textoPendiente({ codigo: "actor_requerido", referencia: "comprador", paso: "p2" }, d)).toBe("Falta registrar a: Comprador.");
    expect(textoPendiente({ codigo: "documento_requerido", referencia: "paz_salvo", paso: "p3" }, d)).toBe("Falta cargar el documento: Paz y salvo de impuestos.");
    expect(textoPendiente({ codigo: "campo_requerido", referencia: "cilindraje", paso: "p3" }, d)).toBe("Falta completar el campo: Cilindraje.");
    expect(textoPendiente({ codigo: "comercial_requerido", referencia: null, paso: "p3" }, d)).toBe("Falta el valor comercial.");
    expect(textoPendiente({ codigo: "prenda_acreedor_requerido", referencia: null, paso: null }, d)).toBe("Falta el acreedor de la prenda.");
    expect(textoPendiente({ codigo: "algo_nuevo", referencia: null, paso: null }, d)).toBe("Falta información por completar.");
  });

  it("el requisito de cobro que aún pide el back se dice sin hablar de pagos y no tiene paso al que ir", () => {
    const p = { codigo: "comprobante_pago_requerido", referencia: null, paso: null };

    expect(textoPendiente(p, d)).toBe("El servicio pide un requisito que este portal todavía no puede completar.");
    expect(destinoDePendiente(p, d)).toBeNull();
  });

  it("lleva al paso donde se corrige, y a ninguno si el tipo no tiene ese paso", () => {
    expect(destinoDePendiente({ codigo: "actor_requerido", referencia: "comprador", paso: "p2" }, d)).toBe("actores");
    expect(destinoDePendiente({ codigo: "documento_requerido", referencia: "paz_salvo", paso: "p3" }, d)).toBe("requisitos");
    const sinActores = definicionDe([["vehicle_query"], ["document_checklist"]]);

    expect(pasoTieneSecciones("actores", sinActores)).toBe(false);
    expect(destinoDePendiente({ codigo: "actor_requerido", referencia: "x", paso: null }, sinActores)).toBeNull();
  });
});

describe("«Información del trámite» no repite placa ni documento del titular", () => {
  it("quita los campos que solo repiten placa o documento de un actor y conserva el resto", () => {
    const base = definicionCompleta();
    const sol = solicitudCompleta();
    const doc = sol.actores[0]?.numeroDocumento ?? "";
    const campo = (fieldKey: string, etiqueta: string) => ({ fieldKey, etiqueta, tipo: "text", obligatorio: false, opciones: [], validationSchema: "" });
    const definicion = { ...base, pasos: base.pasos.map((p) => (p.clave === "p3" ? { ...p, campos: [...p.campos, campo("placa_x", "Placa"), campo("doc_x", "Documento del titular"), campo("nota", "Nota")] } : p)) };
    const conRepetidos = { ...sol, fieldValues: { ...sol.fieldValues, placa_x: sol.placa ?? "", doc_x: doc, nota: "hola" } };
    const etiquetas = filasDe(armarDatosResumen(definicion, conRepetidos), "propios").map((f) => f.etiqueta);
    expect(etiquetas).not.toContain("Placa");
    expect(etiquetas).not.toContain("Documento del titular");
    expect(etiquetas).toContain("Nota");
  });
});
