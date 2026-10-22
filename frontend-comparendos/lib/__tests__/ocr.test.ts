import { describe, expect, it } from "vitest";
import {
  accionesDocumento,
  aFechaInput,
  agregarArchivos,
  armarPaquete,
  cambiosCampos,
  errorArchivoOcr,
  erroresPaquete,
  etiquetaEvaluacion,
  hayEnCurso,
  hojaPaquete,
  marcasRevision,
  MAXIMO_BYTES,
  motivoDocumento,
  motivoFallo,
  progreso,
  resumenEvaluacion,
  valorInicial,
  versionAnterior,
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
  it("elige cómo mostrar el original", () => {
    expect(vistaOriginal("PDF", "a.pdf")).toBe("pdf");
    expect(vistaOriginal("IMAGE", "a.TIF")).toBe("descarga");
    expect(vistaOriginal("IMAGE", "a.jpg")).toBe("imagen");
  });
  it("decide los botones por estado", () => {
    expect(accionesDocumento({ estado: "Completo", reintentable: false, fuenteDuplicado: null })).toEqual({ editar: true, registrar: true, descartar: true, reintentar: false, reprocesar: false });
    expect(accionesDocumento({ estado: "Duplicado", reintentable: false, fuenteDuplicado: "ARCHIVO" }).editar).toBe(false);
    expect(accionesDocumento({ estado: "Fallido", reintentable: true, fuenteDuplicado: null }).reintentar).toBe(true);
    expect(accionesDocumento({ estado: "Registrado", reintentable: false, fuenteDuplicado: null })).toEqual({ editar: false, registrar: false, descartar: false, reintentar: false, reprocesar: false });
    // HU #13828: se reprocesa un incompleto o un ilegible; un fallido técnico se reintenta.
    expect(accionesDocumento({ estado: "Incompleto", reintentable: false, fuenteDuplicado: null }).reprocesar).toBe(true);
    expect(accionesDocumento({ estado: "Fallido", reintentable: false, fuenteDuplicado: null, tipoFallo: "Ilegible" }).reprocesar).toBe(true);
    expect(accionesDocumento({ estado: "Fallido", reintentable: true, fuenteDuplicado: null, tipoFallo: "Http5xx" }).reprocesar).toBe(false);
  });
});

describe("qué revisar (HU #13830)", () => {
  const campo = (nombreCampo: string, valorFinal: string | null, confianza: number | null = 0.95, valorCorregido: string | null = null) => ({
    nombreCampo,
    valorFinal,
    confianza,
    valorCorregido,
  });

  it("marca por reglas comprobables, no por el 95 % del modelo", () => {
    const marcas = marcasRevision({
      campos: [
        campo("numero_comparendo", "7600100000005652501"),
        campo("valor", null, null),
        campo("placa", "ABC123", 0.95),
        campo("nombre_infractor", "JUAN", 0.5),
        campo("fecha_notificacion", "2026-10-01"),
      ],
      camposFaltantes: ["valor"],
      avisoNumero: "El número tiene 19 dígitos…",
    });
    expect(Object.keys(marcas).sort()).toEqual(["fecha_notificacion", "nombre_infractor", "numero_comparendo", "valor"]);
    expect(marcas.valor).toBe("Falta o no es válido.");
    expect(marcas.numero_comparendo).toMatch(/19 dígitos/);
    expect(marcas.placa).toBeUndefined();
  });

  it("lo que ya corrigió el operador no se vuelve a marcar", () => {
    expect(marcasRevision({ campos: [campo("nombre_infractor", "JUAN", 0.5, "JUAN")], camposFaltantes: [] })).toEqual({});
  });
});

describe("resumen del paquete en Excel (HU #13831)", () => {
  const doc = (estado: string, extra: Record<string, unknown> = {}) => ({
    nombreArchivo: `${estado}.pdf`,
    estado,
    numeroComparendoExtraido: "1",
    camposFaltantes: [] as string[],
    tipoFallo: null,
    mensajeError: null,
    fuenteDuplicado: null,
    comparendoId: null,
    ...extra,
  });

  it("explica cada estado en una frase", () => {
    expect(motivoDocumento(doc("Fallido", { tipoFallo: "Ilegible", mensajeError: "La respuesta del modelo quedó cortada" }))).toBe("La respuesta del modelo quedó cortada");
    expect(motivoDocumento(doc("Duplicado", { fuenteDuplicado: "SIMIT" }))).toBe("SIMIT");
    expect(motivoDocumento(doc("Descartado", { motivoDescarte: "No es un comparendo" }))).toBe("No es un comparendo");
    expect(motivoDocumento(doc("Incompleto", { camposFaltantes: ["valor", "placa"] }))).toBe("Valor, Placa");
  });

  it("un renglón por documento y los totales en la nota", () => {
    const hoja = hojaPaquete({ nombre: "Lote octubre", creadoEn: "2026-10-19T15:30:00Z" }, [
      doc("Registrado", { comparendoId: "c1" }),
      doc("Incompleto", { camposFaltantes: ["valor"], avisoNumero: "Verifica", reprocesos: 2 }),
      doc("Registrado", { comparendoId: "c2" }),
    ]);
    expect(hoja.rows).toHaveLength(3);
    expect(hoja.rows[1]).toEqual(["Incompleto.pdf", "Incompleto", "1", "Verifica", "Valor", 2, "No"]);
    expect(hoja.rows[0][6]).toBe("Sí");
    expect(hoja.notes?.[0]).toBe("Paquete «Lote octubre», creado 19/10/2026 10:30 (hora de Colombia).");
    expect(hoja.notes?.[1]).toBe("3 documentos. Incompleto: 1, Registrado: 2.");
  });
});

describe("evaluación del prompt", () => {
  const base = {
    id: "e", estado: "Terminada" as const, documentos: 10, campos: 90, porcentajeCandidata: 92.2, porcentajeVigente: 91.1, vigenteNumero: 1, aprobada: true,
    mensaje: null, creadoEn: "2026-10-16T15:00:00Z", terminadaEn: "2026-10-16T15:02:00Z", habilita: true,
  };

  it("etiqueta cada estado", () => {
    expect(etiquetaEvaluacion(null).texto).toBe("Sin evaluar");
    expect(etiquetaEvaluacion({ ...base, estado: "EnCurso" }).texto).toBe("Evaluando…");
    expect(etiquetaEvaluacion({ ...base, estado: "Fallida" }).texto).toBe("No se completó");
    expect(etiquetaEvaluacion({ ...base, aprobada: false }).tono).toBe("danger");
    expect(etiquetaEvaluacion(base).texto).toBe("Aprobada");
    expect(etiquetaEvaluacion({ ...base, habilita: false }).texto).toBe("Desactualizada");
  });

  it("resume la comparación", () => {
    expect(resumenEvaluacion(base)).toBe("92,2 % vs. 91,1 % de la v1");
    expect(resumenEvaluacion({ ...base, estado: "EnCurso" })).toBeNull();
  });

  it("la anterior es la última retirada", () => {
    const vs = [
      { id: "3", publicada: true, retiradaEn: null },
      { id: "2", publicada: false, retiradaEn: "2026-10-16T10:00:00Z" },
      { id: "1", publicada: false, retiradaEn: "2026-10-15T10:00:00Z" },
      { id: "4", publicada: false, retiradaEn: null },
    ];
    expect(versionAnterior(vs)?.id).toBe("2");
    expect(versionAnterior([vs[0], vs[3]])).toBeNull();
  });
});

describe("motivo de un documento fallido (HU #13787, B12-7)", () => {
  it("muestra el mensaje real del servidor antes que la etiqueta del tipo", () => {
    expect(motivoFallo("Ilegible", "La respuesta del modelo llegó cortada")).toBe("La respuesta del modelo llegó cortada");
  });

  it("sin mensaje, usa la etiqueta del tipo; sin nada, «Falló»", () => {
    expect(motivoFallo("Timeout", null)).toBe("El proveedor no respondió a tiempo");
    expect(motivoFallo(null, "  ")).toBe("Falló");
  });
});
