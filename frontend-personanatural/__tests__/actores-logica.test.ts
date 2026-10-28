import { describe, expect, it } from "vitest";
import type { ActorEstado, ActorRol, EstadoIdentidad } from "@/lib/api/cliente";
import {
  actoresParaGuardar,
  correoInvalido,
  enmascararCorreo,
  enmascararNombre,
  erroresDeServidor,
  estadoDeFila,
  filasIniciales,
  filaVacia,
  motivoIncompleto,
  ordinalLibre,
  puedeAgregar,
  puedeQuitar,
  telefonoInvalido,
  validarFila,
  MENSAJE_PERSONA_JURIDICA,
  type FilaActor,
} from "@/lib/tramite/actores";
import { definicionDe } from "./fixtures-wizard";

const rol = (extra: Partial<ActorRol> = {}): ActorRol => ({ rol: "comprador", etiqueta: "Comprador", min: 1, max: 1, biometria: true, firma: false, personaNaturalPermitida: true, ...extra });
const valida = (extra: Partial<FilaActor> = {}): FilaActor => ({ ...filaVacia("comprador", 1), numeroDocumento: "52123456", nombre: "ANA RUIZ", correo: "ana@correo.co", celular: "3001234567", consultado: true, ...extra });

describe("formato de los datos de un actor", () => {
  it("el teléfono acepta de 7 a 10 dígitos y el correo exige arroba y dominio", () => {
    expect(telefonoInvalido("3001234567")).toBe(false);
    expect(telefonoInvalido("123456")).toBe(true);
    expect(telefonoInvalido("30012345678")).toBe(true);
    expect(telefonoInvalido("300 123 4567")).toBe(true);
    expect(correoInvalido("ana@correo.co")).toBe(false);
    expect(correoInvalido("ana@correo")).toBe(true);
    expect(correoInvalido("")).toBe(true);
  });

  it("valida la fila: documento, consulta, nombre, teléfono y correo, con mensajes claros", () => {
    expect(validarFila(valida(), rol())).toEqual({});
    expect(validarFila(valida({ numeroDocumento: "" , consultado: false }), rol()).numeroDocumento).toBe("Escribe el número de documento");
    expect(validarFila(valida({ numeroDocumento: "12" , consultado: false }), rol()).numeroDocumento).toContain("entre 4 y 15");
    expect(validarFila(valida({ consultado: false }), rol()).numeroDocumento).toBe("Consulta el documento de esta persona");
    const e = validarFila(valida({ nombre: " ", celular: "12", correo: "x" }), rol());
    expect(e).toEqual({ nombre: "Escribe el nombre completo", celular: "Ingresa un teléfono válido", correo: "Ingresa un correo electrónico válido" });
  });

  it("un NIT o un rol que no admite persona natural es persona jurídica: mensaje fijo y no se puede continuar", () => {
    expect(validarFila(valida({ tipoDocumento: "NIT" }), rol()).numeroDocumento).toBe(MENSAJE_PERSONA_JURIDICA);
    expect(validarFila(valida(), rol({ personaNaturalPermitida: false })).numeroDocumento).toBe(MENSAJE_PERSONA_JURIDICA);
  });

  it("enmascara el correo y el nombre de un tercero", () => {
    expect(enmascararCorreo("juan.perez@correo.com")).toBe("ju•••@correo.com");
    expect(enmascararCorreo("sin-arroba")).toBe("••••");
    expect(enmascararNombre("MARIA RODRIGUEZ LOPEZ")).toBe("MA••• RO••• LO•••");
  });
});

describe("filas por rol según la definición", () => {
  const definicion = definicionDe([["actor_form"]], {
    actores: [rol({ rol: "vendedor", etiqueta: "Propietario (vendedor)" }), rol({ rol: "copropietario", etiqueta: "Copropietario", min: 0, max: 3, biometria: false })],
  });
  const guardado = (r: string, ordinal: number): ActorEstado => ({ rol: r, ordinal, validacionEstado: "pendiente", tipoDocumento: "CC", numeroDocumento: "1234567", nombre: "A B", correo: "a@b.co", celular: "3001234567", direccion: null });

  it("crea las filas vacías que faltan hasta el mínimo y ninguna para un rol con mínimo 0", () => {
    const filas = filasIniciales(definicion, []);

    expect(filas.map((f) => `${f.rol}:${f.ordinal}`)).toEqual(["vendedor:1"]);
    expect(filas[0]?.consultado).toBe(false);
  });

  it("toma del borrador las personas por rol y posición (guardadas = consultadas) y recorta al máximo", () => {
    const filas = filasIniciales(definicion, [guardado("copropietario", 2), guardado("copropietario", 1), guardado("vendedor", 1), guardado("copropietario", 3), guardado("copropietario", 4)]);

    expect(filas.map((f) => `${f.rol}:${f.ordinal}`)).toEqual(["vendedor:1", "copropietario:1", "copropietario:2", "copropietario:3"]);
    expect(filas.every((f) => f.consultado)).toBe(true);
  });

  it("agrega hasta el máximo y quita hasta el mínimo, reutilizando la primera posición libre", () => {
    const copro = definicion.actores[1] as ActorRol;
    let filas: FilaActor[] = [];
    for (let i = 0; i < 3; i++) filas = [...filas, filaVacia("copropietario", ordinalLibre(filas, copro))];

    expect(filas.map((f) => f.ordinal)).toEqual([1, 2, 3]);
    expect(puedeAgregar(filas, copro)).toBe(false);
    filas = filas.filter((f) => f.ordinal !== 2);
    expect(ordinalLibre(filas, copro)).toBe(2);
    expect(puedeAgregar(filas, copro)).toBe(true);
    const vendedor = definicion.actores[0] as ActorRol;
    expect(puedeQuitar([filaVacia("vendedor", 1)], vendedor)).toBe(false);
    expect(puedeQuitar(filas, copro)).toBe(true);
  });

  it("manda al back solo las filas consultadas, con el contacto normalizado y sin dirección vacía", () => {
    const filas = [valida({ rol: "vendedor", nombre: "  ANA RUIZ ", direccion: " " }), valida({ consultado: false })];

    expect(actoresParaGuardar(filas)).toEqual([{ rol: "vendedor", ordinal: 1, tipoDocumento: "CC", numeroDocumento: "52123456", nombre: "ANA RUIZ", correo: "ana@correo.co", celular: "3001234567" }]);
  });
});

describe("estado y compuerta de la identidad", () => {
  const identidad: EstadoIdentidad = { solicitudId: "s-1", completa: false, partes: [{ parte: "comprador", ordinal: 1, estado: "pendiente", puedeReenviar: true, reenviosRestantes: 5, esperaSegundos: 0 }] };

  it("el sondeo manda sobre el borrador y sin él se usa el estado del borrador", () => {
    const f = valida();

    expect(estadoDeFila(f, identidad, [])).toBe("pendiente");
    expect(estadoDeFila(f, undefined, [{ rol: "comprador", ordinal: 1, validacionEstado: "aprobada", tipoDocumento: null, numeroDocumento: null, nombre: null, correo: null, celular: null, direccion: null }])).toBe("aprobada");
    expect(estadoDeFila(f, undefined, [])).toBe("sin_solicitar");
  });

  it("informa qué falta por rol: personas, datos válidos y, al final, las identidades sin aprobar", () => {
    const definicion = definicionDe([["actor_form"]], { actores: [rol({ rol: "vendedor", etiqueta: "Propietario (vendedor)" }), rol()] });
    const filas = [valida({ rol: "vendedor" }), valida()];

    expect(motivoIncompleto(definicion, [valida({ rol: "vendedor" })], () => "aprobada")).toBe("Registra a Comprador.");
    expect(motivoIncompleto(definicion, [valida({ rol: "vendedor" }), valida({ celular: "" })], () => "aprobada")).toBe("Completa los datos de Comprador.");
    expect(motivoIncompleto(definicion, filas, (f) => (f.rol === "vendedor" ? "aprobada" : "pendiente"))).toBe("Falta validar la identidad de: Comprador.");
    expect(motivoIncompleto(definicion, filas, () => "aprobada")).toBeUndefined();
  });

  it("un rol sin validación no exige identidad aprobada", () => {
    const definicion = definicionDe([["actor_form"]], { actores: [rol({ biometria: false })] });

    expect(motivoIncompleto(definicion, [valida()], () => "sin_solicitar")).toBeUndefined();
  });
});

describe("errores del servidor por persona", () => {
  it("lleva `actores[i].campo` a la fila que ocupó esa posición", () => {
    const salida = erroresDeServidor({ "actores[1].correo": ["El correo no es válido."], "actores[0].ordinal": ["Esa posición ya está ocupada."], "valores.x": ["no"] }, ["a-1", "a-2"]);

    expect(salida).toEqual({ "a-2": { correo: "El correo no es válido." }, "a-1": { numeroDocumento: "Esa posición ya está ocupada." } });
  });
});
