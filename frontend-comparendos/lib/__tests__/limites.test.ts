import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api";
import { errorTamanoPaquete, LIMITE_PAQUETE_BYTES, mensajeErrorCarga, textoTotal, totalPaquete } from "@/lib/limites";

const MB = 1024 * 1024;
const elegido = (size: number, error: string | null = null) => ({ archivo: { size }, error });

describe("tope de tamaño del paquete OCR", () => {
  it("suma solo los archivos válidos: los rechazados no se envían", () => {
    expect(totalPaquete([elegido(2 * MB), elegido(3 * MB), elegido(25 * MB, "Supera 20 MB.")])).toBe(5 * MB);
    expect(totalPaquete([])).toBe(0);
  });

  it("muestra el total contra el tope con coma decimal", () => {
    expect(textoTotal(0)).toBe("0,0 de 100 MB");
    expect(textoTotal(1.5 * MB)).toBe("1,5 de 100 MB");
  });

  it("deja pasar hasta 100 MB justos y bloquea por encima diciendo qué hacer", () => {
    expect(errorTamanoPaquete(LIMITE_PAQUETE_BYTES)).toBeNull();
    // 15 PDF de 2 MB: lo que antes cortaba el servidor (30 MB) ahora cabe.
    expect(errorTamanoPaquete(15 * 2 * MB)).toBeNull();
    const error = errorTamanoPaquete(LIMITE_PAQUETE_BYTES + 1);
    expect(error).toContain("100 MB");
    expect(error).toContain("dos paquetes");
  });

  it("un 413 sin cuerpo se explica como el tope; los demás errores conservan su mensaje", () => {
    expect(mensajeErrorCarga(new ApiError(413, "No se pudo completar la acción."))).toContain("dos paquetes");
    expect(mensajeErrorCarga(new ApiError(422, "El paquete necesita un nombre."))).toBe("El paquete necesita un nombre.");
    expect(mensajeErrorCarga("raro")).toBe("No se pudo crear el paquete.");
  });
});
