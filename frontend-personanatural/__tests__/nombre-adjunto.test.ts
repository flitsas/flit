import { describe, expect, it } from "vitest";
import { ARCHIVO_CARGADO, nombreVisibleDeAdjunto, recordarArchivo } from "@/lib/tramite/presentacion";

describe("nombre del archivo en la tarjeta del documento", () => {
  it("muestra el nombre del archivo cuando llega en el adjunto", () => {
    expect(nombreVisibleDeAdjunto({ id: "a1", tipo: "prenda_levantamiento", nombreArchivo: "paz y salvo banco.pdf" })).toBe("paz y salvo banco.pdf");
  });

  it("si el servicio solo devuelve el código del tipo, no lo muestra como si fuera el archivo", () => {
    expect(nombreVisibleDeAdjunto({ id: "a1", tipo: "prenda_levantamiento", nombreArchivo: "prenda_levantamiento" })).toBe(ARCHIVO_CARGADO);
    expect(nombreVisibleDeAdjunto({ id: "a1", tipo: "prenda_levantamiento", nombreArchivo: "  " })).toBe(ARCHIVO_CARGADO);
  });

  it("usa el nombre que esta sesión ya vio cuando el servicio solo trae el código", () => {
    recordarArchivo("s-9", "a7", "contrato.pdf", "prenda_registro");
    expect(nombreVisibleDeAdjunto({ id: "a7", tipo: "prenda_registro", nombreArchivo: "prenda_registro" }, "s-9")).toBe("contrato.pdf");
  });
});
