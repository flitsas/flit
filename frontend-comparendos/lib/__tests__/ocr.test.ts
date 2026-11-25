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
  registroEnLote,
  MAXIMO_BYTES,
  MAXIMO_DOCUMENTOS,
  motivoDocumento,
  motivoFallo,
  progreso,
  resumenEvaluacion,
  numeroBase,
  numerosDifieren,
  tamanoLegible,
  valorInicial,
  versionAnterior,
  vistaDuplicado,
  textoCorreoAlAsociar,
  avisosAlAsociar,
  avisoLoteEncolado,
  avisoSinAvanceLote,
  cerrarResumenLote,
  colaReemplazos,
  type LoteEnCurso,
  resumenLoteCerrado,
  textoAvanceLote,
  textosResumenLote,
  etiquetaAsociarLote,
  leerCola,
  resumenCola,
  siguienteEnCola,
  textosVistaPreviaLote,
  urlCola,
  esEstadoFinalDocumento,
  vistaPorContentType,
  ESTADO_DOCUMENTO,
  vistaOriginal,
} from "@/lib/ocr";

const archivo = (name: string, size = 100) => new File([new Uint8Array(size)], name);

describe("errorArchivoOcr", () => {
  it("HU #14370 / GDC ID10: límites FE alineados a 20 MB / 200 archivos", () => {
    expect(MAXIMO_BYTES).toBe(20 * 1024 * 1024);
    expect(MAXIMO_DOCUMENTOS).toBe(200);
  });
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
    expect(accionesDocumento({ estado: "Completo", reintentable: false, fuenteDuplicado: null })).toEqual({ editar: true, registrar: true, descartar: true, asociar: false, reintentar: false, reprocesar: false });
    expect(accionesDocumento({ estado: "Duplicado", reintentable: false, fuenteDuplicado: "ARCHIVO" }).editar).toBe(false);
    expect(accionesDocumento({ estado: "Fallido", reintentable: true, fuenteDuplicado: null }).reintentar).toBe(true);
    expect(accionesDocumento({ estado: "Registrado", reintentable: false, fuenteDuplicado: null })).toEqual({ editar: false, registrar: false, descartar: false, asociar: false, reintentar: false, reprocesar: false });
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
    expect(motivoFallo("Timeout", null)).toBe("La lectura tardó demasiado");
    expect(motivoFallo(null, "  ")).toBe("Falló");
  });
});

describe("número por verificar (GDC punto 14)", () => {
  it("el registro en lote no cuenta los completos con el número por confirmar y dice qué hacer", () => {
    expect(registroEnLote(3, 0)).toEqual({ registrables: 3, aviso: null });
    const r = registroEnLote(3, 2);
    expect(r.registrables).toBe(1);
    expect(r.aviso).toContain("2 documentos completos tienen el número por verificar");
    expect(r.aviso).toContain("corrígelo o confirma");
    expect(registroEnLote(1, 1)).toMatchObject({ registrables: 0 });
    expect(registroEnLote(1, 1).aviso).toContain("1 documento completo tiene");
  });

  it("el número confirmado deja de marcarse", () => {
    const campos = [{ nombreCampo: "numero_comparendo", confianza: 0.98, valorFinal: "D056150000000055727159", valorCorregido: null }];
    const aviso = "El número tiene 21 dígitos…";
    expect(marcasRevision({ campos, camposFaltantes: [], avisoNumero: aviso, numeroPorConfirmar: true })).toEqual({ numero_comparendo: aviso });
    expect(
      marcasRevision({ campos: [{ ...campos[0], valorCorregido: campos[0].valorFinal }], camposFaltantes: [], avisoNumero: aviso, numeroPorConfirmar: false }),
    ).toEqual({});
  });
});

describe("asociar un documento a un comparendo existente", () => {
  const evidencia = { archivoId: "a1", nombre: "x.pdf", contentType: "application/pdf", tamanoBytes: 10, cargadaEn: "2026-11-01T10:00:00Z" };
  const dup = (extra: Record<string, unknown> = {}) => ({ tipo: "comparendo" as const, asociable: true, evidenciaActual: null, ...extra });

  it("sin documento previo se ofrece asociar; con documento, comparar", () => {
    expect(vistaDuplicado({ estado: "Duplicado", duplicado: dup() })).toBe("asociar");
    expect(vistaDuplicado({ estado: "Duplicado", duplicado: dup({ evidenciaActual: evidencia }) })).toBe("comparar");
  });

  it("no asociable (o la API no lo dice) solo explica; documento contra documento y otros estados no ofrecen nada", () => {
    expect(vistaDuplicado({ estado: "Duplicado", duplicado: dup({ asociable: false }) })).toBe("no-asociable");
    expect(vistaDuplicado({ estado: "Duplicado", duplicado: { tipo: "comparendo" } })).toBe("no-asociable");
    expect(vistaDuplicado({ estado: "Duplicado", duplicado: { tipo: "documento" } })).toBeNull();
    expect(vistaDuplicado({ estado: "Completo", duplicado: dup() })).toBeNull();
    expect(vistaDuplicado({ estado: "Duplicado", duplicado: null })).toBeNull();
  });

  it("estado Asociado: etiqueta, es final y se explica", () => {
    expect(ESTADO_DOCUMENTO.Asociado.texto).toBe("Asociado");
    expect(accionesDocumento({ estado: "Asociado", reintentable: false, fuenteDuplicado: null })).toEqual({ editar: false, registrar: false, descartar: false, asociar: false, reintentar: false, reprocesar: false });
    expect(accionesDocumento({ estado: "Duplicado", reintentable: false, fuenteDuplicado: "SIMIT" }).asociar).toBe(true);
    expect(accionesDocumento({ estado: "Duplicado", reintentable: false, fuenteDuplicado: "ARCHIVO" }).asociar).toBe(false);
    expect(motivoDocumento({ nombreArchivo: "a.pdf", estado: "Asociado", numeroComparendoExtraido: "1", camposFaltantes: [], tipoFallo: null, mensajeError: null, fuenteDuplicado: null, comparendoId: "c" })).toMatch(/agregado/);
  });

  it("vista previa por tipo y tamaño legible", () => {
    expect(vistaPorContentType("application/pdf", "a")).toBe("pdf");
    expect(vistaPorContentType("image/tiff", "a.tif")).toBe("descarga");
    expect(vistaPorContentType("image/png", "a.png")).toBe("imagen");
    expect(vistaPorContentType("", "a.pdf")).toBe("pdf");
    expect(tamanoLegible(500)).toBe("500 B");
    expect(tamanoLegible(2048)).toBe("2 KB");
  });
});

describe("comparar el número del documento con el del sistema", () => {
  it("la letra inicial, los espacios y los guiones no cuentan", () => {
    expect(numeroBase("D-1100 1000-0000")).toBe("110010000000");
    expect(numerosDifieren("D11001000000012345678", "11001000000012345678")).toBe(false);
    expect(numerosDifieren("A 11001-000000012345678", "11001000000012345678")).toBe(false);
  });
  it("otro dígito sí cuenta; sin uno de los dos no se avisa", () => {
    expect(numerosDifieren("11001000000012345678", "11001000000012345679")).toBe(true);
    expect(numerosDifieren(null, "11001000000012345678")).toBe(false);
    expect(numerosDifieren("11001000000012345678", "")).toBe(false);
  });
});

describe("correo de evidencia al asociar", () => {
  it("antes: dice que se enviará, con el documento nuevo si es reemplazo", () => {
    expect(textoCorreoAlAsociar({ seEnviara: true, motivoNo: null }, false)).toBe("Se enviará ahora al conductor un correo con este documento.");
    expect(textoCorreoAlAsociar({ seEnviara: true, motivoNo: null }, true)).toContain("documento nuevo");
  });
  it("antes: si no sale, muestra el motivo literal (solo agrega el punto si falta); sin dato no promete nada", () => {
    expect(textoCorreoAlAsociar({ seEnviara: false, motivoNo: "El conductor no tiene correo registrado" }, false)).toBe("No se enviará correo al conductor. Motivo: El conductor no tiene correo registrado.");
    expect(textoCorreoAlAsociar({ seEnviara: false, motivoNo: "FLIT SAS no usa correo." }, false)).toBe("No se enviará correo al conductor. Motivo: FLIT SAS no usa correo.");
    expect(textoCorreoAlAsociar({ seEnviara: false, motivoNo: null }, false)).toBe("No se enviará correo al conductor.");
    expect(textoCorreoAlAsociar(undefined, false)).toBeNull();
    expect(textoCorreoAlAsociar(null, true)).toBeNull();
  });
  it("después: enviado o en cola = un solo aviso; en reemplazo dice reemplazado", () => {
    expect(avisosAlAsociar("123", false, { estado: "Enviado", motivo: null })).toEqual([{ mensaje: "Documento asociado al comparendo 123 y enviado al conductor.", tono: "success" }]);
    expect(avisosAlAsociar("123", true, { estado: "Enviado", motivo: null })).toEqual([{ mensaje: "Documento reemplazado en el comparendo 123 y enviado al conductor.", tono: "success" }]);
    expect(avisosAlAsociar("123", false, { estado: "Pendiente", motivo: null })).toHaveLength(1);
  });
  it("después: omitido = éxito más aviso con el motivo literal", () => {
    expect(avisosAlAsociar("123", false, { estado: "Omitido", motivo: "El comparendo ya está pagado" })).toEqual([
      { mensaje: "Documento asociado al comparendo 123.", tono: "success" },
      { mensaje: "No se envió correo al conductor. Motivo: El comparendo ya está pagado.", tono: "warning" },
    ]);
    expect(avisosAlAsociar("123", false, { estado: "Omitido", motivo: null })[1].tono).toBe("warning");
  });
  it("después: un estado desconocido cae en aviso, sin lanzar; sin correo solo el éxito de siempre", () => {
    const raro = avisosAlAsociar("123", false, { estado: "Reintentando" as never, motivo: "Algo nuevo" });
    expect(raro.map((a) => a.tono)).toEqual(["success", "warning"]);
    expect(avisosAlAsociar("123", false, undefined)).toEqual([{ mensaje: "Documento asociado al comparendo 123.", tono: "success" }]);
    expect(avisosAlAsociar("123", true, null)).toEqual([{ mensaje: "Documento reemplazado en el comparendo 123.", tono: "success" }]);
  });
});

describe("estados finales del documento", () => {
  it("Asociado, Registrado y Descartado son de solo lectura; el resto sigue en revisión", () => {
    for (const e of ["Asociado", "Registrado", "Descartado"]) expect(esEstadoFinalDocumento(e)).toBe(true);
    for (const e of ["Completo", "Incompleto", "Duplicado", "Fallido", "EnCola"]) expect(esEstadoFinalDocumento(e)).toBe(false);
  });
});

describe("asociar en lote (GDC-EVID §9)", () => {
  const vista = (o: Partial<import("@/lib/ocr").VistaPreviaLote> = {}) => ({
    asociables: 12,
    correos: { saldran: 10, conductores: 7, omitidos: [{ motivo: "El conductor no tiene correo registrado", cantidad: 2 }] },
    porRevisar: { reemplazos: 3, numeroDistinto: 1 },
    ...o,
  });

  it("botón y textos de la vista previa, con singulares", () => {
    expect(etiquetaAsociarLote(12)).toBe("Asociar los 12");
    expect(etiquetaAsociarLote(1)).toBe("Asociar el documento");
    const t = textosVistaPreviaLote(vista());
    expect(t.documentos).toBe("Se asociarán 12 documentos a sus comparendos.");
    expect(t.correos).toBe("Saldrán 10 correos a 7 conductores (cada conductor recibe uno por comparendo).");
    expect(t.porRevisar).toEqual([
      "Quedan 3 reemplazos para revisar uno por uno.",
      "1 documento tiene el número distinto al del sistema: se revisa uno por uno.",
    ]);
    const uno = textosVistaPreviaLote(vista({ asociables: 1, correos: { saldran: 1, conductores: 1, omitidos: [] }, porRevisar: { reemplazos: 1, numeroDistinto: 0 } }));
    expect(uno.documentos).toBe("Se asociará 1 documento a su comparendo.");
    expect(uno.correos).toBe("Saldrá 1 correo a 1 conductor (cada conductor recibe uno por comparendo).");
    expect(uno.porRevisar).toEqual(["Queda 1 reemplazo para revisar uno por uno."]);
    expect(textosVistaPreviaLote(vista({ correos: { saldran: 0, conductores: 0, omitidos: [] }, porRevisar: { reemplazos: 0, numeroDistinto: 0 } }))).toMatchObject({
      correos: "No saldrá ningún correo al conductor.",
      porRevisar: [],
    });
  });

});

describe("lote en segundo plano (GDC-EVID §10)", () => {
  const lote = (o: Partial<LoteEnCurso> = {}): LoteEnCurso => ({
    loteId: "l1",
    porAsociar: 36,
    total: 48,
    asociados: 12,
    noAsociados: [],
    correosEnCamino: 11,
    sinCorreo: [],
    solicitadoPor: "admin@empresa.local",
    solicitadoEn: "2026-11-25T15:00:00Z",
    terminadoEn: null,
    ...o,
  });

  it("aviso al encolar y avance de la franja", () => {
    expect(avisoLoteEncolado(48)).toBe("Asociando 48 documentos en segundo plano; puedes seguir trabajando.");
    expect(avisoLoteEncolado(1)).toBe("Asociando 1 documento en segundo plano; puedes seguir trabajando.");
    expect(textoAvanceLote(lote())).toBe("Asociando 12 de 48…");
    expect(textoAvanceLote(lote({ porAsociar: 60 }))).toBe("Asociando 0 de 48…");
  });

  it("resumen: asociados y correos en camino, con singulares; sin correos no hay línea", () => {
    expect(textosResumenLote(lote({ porAsociar: 0, asociados: 47, correosEnCamino: 45 }))).toEqual({
      titulo: "Se asociaron 47 documentos a sus comparendos.",
      correos: "45 correos con el documento van en camino a los conductores.",
    });
    expect(textosResumenLote(lote({ porAsociar: 0, asociados: 1, correosEnCamino: 1 }))).toEqual({
      titulo: "Se asoció 1 documento a su comparendo.",
      correos: "1 correo con el documento va en camino al conductor.",
    });
    expect(textosResumenLote(lote({ porAsociar: 0, correosEnCamino: 0 })).correos).toBeNull();
  });

  it("el resumen cerrado se recuerda por lote; si el almacenamiento falla, se muestra igual y no rompe", () => {
    window.localStorage.clear();
    expect(resumenLoteCerrado("l1")).toBe(false);
    cerrarResumenLote("l1");
    expect(resumenLoteCerrado("l1")).toBe(true);
    expect(resumenLoteCerrado("l2")).toBe(false);
    // Navegador con el almacenamiento bloqueado: acceder a localStorage lanza.
    const original = Object.getOwnPropertyDescriptor(window, "localStorage")!;
    Object.defineProperty(window, "localStorage", {
      configurable: true,
      get() {
        throw new Error("bloqueado");
      },
    });
    try {
      expect(resumenLoteCerrado("l1")).toBe(false);
      expect(() => cerrarResumenLote("l3")).not.toThrow();
    } finally {
      Object.defineProperty(window, "localStorage", original);
    }
    expect(resumenLoteCerrado("l1")).toBe(true);
  });
});

describe("cola de reemplazos (GDC-EVID §9)", () => {
  const docs = [
    { id: "a", estado: "Duplicado", revision: "reemplazo" as const },
    { id: "b", estado: "Duplicado", revision: null },
    { id: "c", estado: "Asociado", revision: "reemplazo" as const },
    { id: "d", estado: "Duplicado", revision: "numeroDistinto" as const },
    { id: "e", estado: "Duplicado", revision: "reemplazo" as const },
  ];

  it("solo los reemplazos pendientes, en el orden del listado", () => {
    expect(colaReemplazos(docs)).toEqual(["a", "e"]);
  });

  it("la URL lleva el avance y se lee de vuelta", () => {
    const paso = { paquete: "p1", hechos: 1, total: 7, reemplazados: 1, descartados: 0 };
    const url = urlCola("e", paso);
    expect(url.startsWith("/ocr/documentos/e?")).toBe(true);
    expect(leerCola(new URLSearchParams(url.split("?")[1]))).toEqual(paso);
    expect(leerCola(new URLSearchParams("paquete=p1"))).toBeNull();
    expect(leerCola(new URLSearchParams("cola=reemplazos"))).toBeNull();
    expect(leerCola(new URLSearchParams("cola=reemplazos&paquete=p1&hechos=-3&total=x"))).toEqual({ paquete: "p1", hechos: 0, total: 1, reemplazados: 0, descartados: 0 });
  });

  it("al decidir pasa al siguiente (total recalculado) y al final vuelve al paquete con el resumen", () => {
    const paso = { paquete: "p1", hechos: 0, total: 2, reemplazados: 0, descartados: 0 };
    const sig = siguienteEnCola(["a", "e"], "a", paso, "reemplazado");
    expect(leerCola(new URLSearchParams(sig.split("?")[1]))).toEqual({ paquete: "p1", hechos: 1, total: 2, reemplazados: 1, descartados: 0 });
    expect(sig.startsWith("/ocr/documentos/e?")).toBe(true);
    const fin = siguienteEnCola(["e"], "e", { ...paso, hechos: 1, reemplazados: 1 }, "descartado");
    expect(fin).toBe("/ocr/paquetes/p1?cola=terminada&reemplazados=1&descartados=1");
    expect(resumenCola(new URLSearchParams(fin.split("?")[1]))).toBe("Terminaste la revisión de reemplazos: 1 reemplazado y 1 descartado.");
    expect(resumenCola(new URLSearchParams("cola=terminada&reemplazados=5&descartados=0"))).toBe("Terminaste la revisión de reemplazos: 5 reemplazados.");
    expect(resumenCola(new URLSearchParams(""))).toBeNull();
  });
});

describe("lote estancado (GDC-EVID §10)", () => {
  const base = {
    loteId: "l1", porAsociar: 10, total: 20, asociados: 10, noAsociados: [], correosEnCamino: 10, sinCorreo: [],
    solicitadoPor: null, solicitadoEn: "2026-11-25T15:00:00Z", terminadoEn: null,
  };
  const ahora = Date.parse("2026-11-25T15:30:00Z");
  const hace = (min: number) => new Date(ahora - min * 60000).toISOString();

  it("más de 5 minutos en curso: avisa con los minutos; 5 o menos, sin campo, fecha inválida o terminado: null", () => {
    expect(avisoSinAvanceLote({ ...base, ultimoAvanceEn: hace(6) }, ahora)).toBe("Sin avance desde hace 6 minutos; se retomará automáticamente.");
    expect(avisoSinAvanceLote({ ...base, ultimoAvanceEn: hace(5) }, ahora)).toBeNull();
    expect(avisoSinAvanceLote({ ...base, ultimoAvanceEn: hace(4) }, ahora)).toBeNull();
    expect(avisoSinAvanceLote(base, ahora)).toBeNull();
    expect(avisoSinAvanceLote({ ...base, ultimoAvanceEn: "no es fecha" }, ahora)).toBeNull();
    expect(avisoSinAvanceLote({ ...base, porAsociar: 0, ultimoAvanceEn: hace(30) }, ahora)).toBeNull();
  });
});

// MISMA tabla que la prueba de NumeroComparendo.DifiereALaVista en core-comparendos (services/core-comparendos): las dos copias
// de la regla «el número difiere más allá de la letra o el formato» deben dar lo mismo. Si cambias una copia o esta tabla,
// cambia también la otra; si una fila falla aquí, es una diferencia real entre front y back (no se ajusta la tabla).
describe("numerosDifieren: tabla compartida con el backend (DifiereALaVista)", () => {
  it.each<[string | null, string | null, boolean]>([
    ["D-11001000000000012345", "11001000000000012345", false],
    ["A-98765", "98765", false],
    ["A98765", "98765", false],
    ["11001 0000 0000 0012345", "11001000000000012345", false],
    ["11001.000000000.012345", "11001000000000012345", false],
    ["11001-000000000-012345", "11001000000000012345", false],
    ["  d - 11001000000000012345", "11001000000000012345", false],
    ["11001/000000000012345", "11001000000000012345", true],
    ["11001_000000000012345", "11001000000000012345", true],
    ["11001,000000000012345", "11001000000000012345", true],
    ["11001:000000000012345", "11001000000000012345", true],
    ["#11001000000000012345", "11001000000000012345", true],
    ["11001/00000999/0308", "11001000009990308", true],
    ["11001000000000012346", "11001000000000012345", true],
    ["AB12C34", "AB12C35", true],
    ["11001a00000000012345", "11001A00000000012345", false],
    ["   ", "11001000000000012345", false],
    [null, null, false],
    [null, "11001000000000012345", false],
    ["11001000000000012345", "", false],
  ])("%j vs %j → %s", (leido, sistema, esperado) => {
    expect(numerosDifieren(leido, sistema)).toBe(esperado);
  });
});
