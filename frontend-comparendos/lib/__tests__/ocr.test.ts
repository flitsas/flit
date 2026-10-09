import { describe, expect, it } from "vitest";
import {
  accionesDocumento,
  aFechaInput,
  agregarArchivos,
  armarPaquete,
  cambiosCampos,
  confianza,
  errorArchivoOcr,
  erroresPaquete,
  hayEnCurso,
  MAXIMO_BYTES,
  progreso,
  valorInicial,
  vistaOriginal,
} from "@/lib/ocr";

const archivo = (name: string, size = 100) => new File([new Uint8Array(size)], name);

describe("errorArchivoOcr", () => {
  it("acepta JPG, PNG, TIFF y PDF sin importar mayúsculas", () => {
    for (const n of ["a.jpg", "b.JPEG", "c.png", "d.tif", "e.TIFF", "f.pdf"]) expect(errorArchivoOcr({ name: n, size: 10 })).toBeNull();
  });
  it("rechaza otros formatos, vacíos y de más de 20 MB", () => {
    expect(errorArchivoOcr({ name: "hoja.xlsx", size: 10 })).toMatch(/Formato/);
    expect(errorArchivoOcr({ name: "a.jpg", size: 0 })).toMatch(/vacío/);
    expect(errorArchivoOcr({ name: "a.pdf", size: MAXIMO_BYTES + 1 })).toMatch(/20 MB/);
  });
});

describe("agregarArchivos", () => {
  it("descarta repetidos y marca los inválidos", () => {
    const r = agregarArchivos([], [archivo("a.jpg"), archivo("a.jpg"), archivo("x.docx")]);
    expect(r).toHaveLength(2);
    expect(r[0].error).toBeNull();
    expect(r[1].error).toMatch(/Formato/);
  });
  it("rechaza lo que pase de 200 válidos", () => {
    const r = agregarArchivos([], Array.from({ length: 201 }, (_, i) => archivo(`f${i}.png`)));
    expect(r.filter((x) => !x.error)).toHaveLength(200);
    expect(r[200].error).toMatch(/200/);
  });
});

describe("erroresPaquete y armarPaquete", () => {
  it("exige nombre y un archivo válido", () => {
    expect(erroresPaquete(" ", [])).toEqual(["Escribe el nombre del paquete.", "Agrega al menos un archivo válido."]);
    expect(erroresPaquete("x".repeat(151), agregarArchivos([], [archivo("a.jpg")]))).toEqual(["El nombre admite hasta 150 caracteres."]);
    expect(erroresPaquete("Lote", agregarArchivos([], [archivo("a.jpg")]))).toEqual([]);
  });
  it("solo envía los válidos y la marca de iniciar", () => {
    const form = armarPaquete(" Lote ", agregarArchivos([], [archivo("a.jpg"), archivo("b.doc")]), true);
    expect(form.get("nombre")).toBe("Lote");
    expect(form.get("iniciar")).toBe("true");
    expect(form.getAll("archivos")).toHaveLength(1);
    expect(armarPaquete("L", [], false).get("iniciar")).toBeNull();
  });
});

describe("progreso y hayEnCurso", () => {
  it("cuenta lo que ya salió de la cola", () => {
    expect(progreso({ cantidadDocumentos: 50, cantidadPendientes: 5 })).toEqual({ procesados: 45, total: 50, porcentaje: 90, texto: "45/50 procesados" });
    expect(progreso({ cantidadDocumentos: 0, cantidadPendientes: 0 }).porcentaje).toBe(0);
  });
  it("detecta trabajo en curso", () => {
    expect(hayEnCurso(["Procesado", "EnProcesamiento"])).toBe(true);
    expect(hayEnCurso(["Completo", "Cerrado"])).toBe(false);
  });
});

describe("revisión de campos", () => {
  it("normaliza fechas y booleanos para el formulario", () => {
    expect(aFechaInput("2026-09-28")).toBe("2026-09-28");
    expect(aFechaInput("8/9/2026")).toBe("2026-09-08");
    expect(aFechaInput("ayer")).toBe("");
    expect(valorInicial("fecha_infraccion", "28/09/2026")).toBe("2026-09-28");
    expect(valorInicial("es_fotomulta", "Sí")).toBe("true");
    expect(valorInicial("placa", null)).toBe("");
  });
  it("solo devuelve lo que cambió y vacía como null", () => {
    expect(cambiosCampos({ valor: "", placa: "ABC123", nombre_infractor: "X" }, { valor: " 302050 ", placa: "ABC123", nombre_infractor: "" })).toEqual({
      valor: "302050",
      nombre_infractor: null,
    });
  });
  it("clasifica la confianza", () => {
    expect(confianza(0.934)).toEqual({ texto: "93 %", tono: "alta" });
    expect(confianza(0.7)?.tono).toBe("media");
    expect(confianza(0.2)?.tono).toBe("baja");
    expect(confianza(null)).toBeNull();
  });
  it("elige cómo mostrar el original", () => {
    expect(vistaOriginal("PDF", "a.pdf")).toBe("pdf");
    expect(vistaOriginal("IMAGE", "a.TIF")).toBe("descarga");
    expect(vistaOriginal("IMAGE", "a.jpg")).toBe("imagen");
  });
  it("decide los botones por estado", () => {
    expect(accionesDocumento({ estado: "Completo", reintentable: false, fuenteDuplicado: null })).toEqual({ editar: true, registrar: true, descartar: true, reintentar: false });
    expect(accionesDocumento({ estado: "Duplicado", reintentable: false, fuenteDuplicado: "ARCHIVO" }).editar).toBe(false);
    expect(accionesDocumento({ estado: "Fallido", reintentable: true, fuenteDuplicado: null }).reintentar).toBe(true);
    expect(accionesDocumento({ estado: "Registrado", reintentable: false, fuenteDuplicado: null })).toEqual({ editar: false, registrar: false, descartar: false, reintentar: false });
  });
});
