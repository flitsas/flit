import { describe, expect, it } from "vitest";
import { LIMITES_TEXTO, validarTextoLibre } from "@/lib/saneamiento/textoLibre";

// Uso de ejemplo: validarTextoLibre(motivo, LIMITES_TEXTO.motivo) → null si sirve, o el mensaje fijo del error.
// SP-13 con la decisión del usuario (§E3): largo en puntos de código y sin caracteres de control ni de dirección
// bidireccional; los dígitos seguidos se permiten. El servidor sigue mandando; el mensaje nunca repite lo digitado.

describe("validarTextoLibre (SP-13)", () => {
  it("límites del contrato: nota 0..500, observación 0..1000, motivo 10..500, descripción 3..200", () => {
    expect(LIMITES_TEXTO).toEqual({
      nota: { min: 0, max: 500 },
      observacion: { min: 0, max: 1000 },
      motivo: { min: 10, max: 500 },
      descripcion: { min: 3, max: 200 },
    });
  });

  it("acepta un texto dentro del rango", () => {
    expect(validarTextoLibre("Pagó el comparendo en ventanilla", LIMITES_TEXTO.motivo)).toBeNull();
  });

  it("cuenta puntos de código: un emoji es un carácter", () => {
    expect(validarTextoLibre("😀😀😀", LIMITES_TEXTO.descripcion)).toBeNull();
    expect(validarTextoLibre("😀".repeat(200), LIMITES_TEXTO.descripcion)).toBeNull();
    expect(validarTextoLibre("😀".repeat(201), LIMITES_TEXTO.descripcion)).toBe("Admite máximo 200 caracteres.");
  });

  it("recorta antes de medir y exige el mínimo", () => {
    expect(validarTextoLibre("   corto   ", LIMITES_TEXTO.motivo)).toBe("Escribe al menos 10 caracteres.");
    expect(validarTextoLibre("   ", LIMITES_TEXTO.descripcion)).toBe("Escribe al menos 3 caracteres.");
    // Nota y observación son opcionales: vacías se mandan como null.
    expect(validarTextoLibre("   ", LIMITES_TEXTO.nota)).toBeNull();
  });

  it("decisión del usuario (§E3): los dígitos seguidos se permiten; solo cuentan el largo y los caracteres", () => {
    expect(validarTextoLibre("Pagado con el recibo 1234567890", LIMITES_TEXTO.motivo)).toBeNull();
  });

  it("el mensaje de error nunca repite lo digitado", () => {
    const error = validarTextoLibre("x".repeat(501) + "secreto", LIMITES_TEXTO.motivo);
    expect(error).toBe("Admite máximo 500 caracteres.");
    expect(error).not.toContain("secreto");
  });

  it("rechaza caracteres de control y de dirección bidireccional", () => {
    expect(validarTextoLibre("Texto con \u0007 campana", LIMITES_TEXTO.motivo)).toBe("El texto tiene caracteres no permitidos.");
    expect(validarTextoLibre("Texto con \u202E invertido", LIMITES_TEXTO.motivo)).toBe("El texto tiene caracteres no permitidos.");
    expect(validarTextoLibre("Texto con \u2066 aislado", LIMITES_TEXTO.motivo)).toBe("El texto tiene caracteres no permitidos.");
  });

  it("los saltos de línea dentro del texto se permiten (nota de varias líneas)", () => {
    expect(validarTextoLibre("Primera línea\nsegunda línea", LIMITES_TEXTO.nota)).toBeNull();
  });
});
