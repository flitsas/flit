import { describe, expect, it } from "vitest";
import { cssDeMarca, marcaAplicable, nombreSeguro, rutaImagenMarca } from "../marca";

const VALIDA = {
  origen: "organismo",
  nombre: "Movilidad de Bogotá",
  colores: { primario: "#0b3d91", secundario: "#3d63dd", sobrePrimario: "#ffffff" },
  logoUrl: "/api/v1/persona-natural/marca/imagenes/9f1c-77",
  bannerUrl: "/api/v1/persona-natural/marca/imagenes/aa12",
  version: 3,
};

describe("marcaAplicable (HU #14379)", () => {
  it("acepta una marca publicada y normaliza los colores", () => {
    expect(marcaAplicable(VALIDA)).toEqual({
      nombre: "Movilidad de Bogotá",
      colores: { primario: "#0B3D91", secundario: "#3D63DD", sobrePrimario: "#FFFFFF" },
      logoUrl: "/api/v1/persona-natural/marca/imagenes/9f1c-77",
      bannerUrl: "/api/v1/persona-natural/marca/imagenes/aa12",
      version: 3,
    });
  });

  it("origen flit, nulos o formas raras: sin marca (el portal queda como hoy)", () => {
    expect(marcaAplicable({ origen: "flit", nombre: null, colores: null, logoUrl: null, bannerUrl: null, version: 0 })).toBeNull();
    expect(marcaAplicable(null)).toBeNull();
    expect(marcaAplicable("organismo")).toBeNull();
    expect(marcaAplicable({ ...VALIDA, colores: null })).toBeNull();
  });

  it("un color que no es #RRGGBB descarta TODA la marca (no se aplica a medias)", () => {
    for (const malo of ["red", "#FFF", "#GGGGGG", "#0B3D91;}body{display:none", '#0B3D91"', "url(x)", "#0B3D911"]) {
      expect(marcaAplicable({ ...VALIDA, colores: { ...VALIDA.colores, secundario: malo } })).toBeNull();
    }
  });

  it("sin logo o banner propios deja esas imágenes en null (imagen FLIT)", () => {
    const m = marcaAplicable({ ...VALIDA, logoUrl: null, bannerUrl: undefined });
    expect(m?.logoUrl).toBeNull();
    expect(m?.bannerUrl).toBeNull();
  });
});

describe("rutaImagenMarca: solo imágenes del propio BFF", () => {
  it("acepta la ruta con o sin prefijo y siempre devuelve la ruta relativa del BFF", () => {
    expect(rutaImagenMarca("/api/v1/persona-natural/marca/imagenes/abc-1")).toBe("/api/v1/persona-natural/marca/imagenes/abc-1");
    expect(rutaImagenMarca("/marca/imagenes/abc-1")).toBe("/api/v1/persona-natural/marca/imagenes/abc-1");
  });

  it.each([
    "https://evil.example/logo.png",
    "//evil.example/marca/imagenes/x",
    "javascript:alert(1)",
    "data:image/svg+xml;base64,AAAA",
    "/api/v1/persona-natural/marca/imagenes/../../x",
    "/api/v1/persona-natural/marca/imagenes/a?x=1",
    "/api/v1/persona-natural/otra/ruta",
    "/api/v1/persona-natural/marca/imagenes/",
    42,
    null,
  ])("rechaza %s", (valor) => {
    expect(rutaImagenMarca(valor)).toBeNull();
  });
});

describe("nombreSeguro: texto plano", () => {
  it("conserva el texto tal cual (React lo escapa) y limpia controles y espacios", () => {
    expect(nombreSeguro("  Tránsito\u0000   de\nCali  ")).toBe("Tránsito de Cali");
    expect(nombreSeguro('<img src=x onerror="alert(1)">')).toBe('<img src=x onerror="alert(1)">');
  });
  it("vacío o no texto → null, y se acota a 80", () => {
    expect(nombreSeguro("   ")).toBeNull();
    expect(nombreSeguro(7)).toBeNull();
    expect(nombreSeguro("a".repeat(200))).toHaveLength(80);
  });
});

describe("cssDeMarca: sin inyección de CSS", () => {
  it("solo contiene los tres colores validados, nunca el nombre ni las URL", () => {
    const m = marcaAplicable({ ...VALIDA, nombre: "x}body{background:url(//evil)} </style><script>1</script>" })!;
    const css = cssDeMarca(m);
    expect(css).toContain("--primary:#0B3D91");
    expect(css).toContain("--primary-foreground:#FFFFFF");
    expect(css).toContain("--dashboard-action-background:#3D63DD");
    expect(css).not.toMatch(/evil|script|<|>|url\(|marca\/imagenes/);
  });

  it("si por alguna vía llegara un color inválido, no emite nada", () => {
    const m = marcaAplicable(VALIDA)!;
    expect(cssDeMarca({ ...m, colores: { ...m.colores, primario: "red;}x{" } })).toBe("");
  });

  it("con texto oscuro sobre la marca, el texto del botón blanco no usa el primario claro", () => {
    const m = marcaAplicable({ ...VALIDA, colores: { primario: "#FFD34E", secundario: "#F0B400", sobrePrimario: "#162744" } })!;
    expect(cssDeMarca(m)).toContain("--marca-texto-boton:#162744");
  });
});

describe("título de la tarjeta de acción con marca", () => {
  it("usa el texto calculado, no el blanco fijo, para que se lea sobre el color secundario", () => {
    const css = cssDeMarca({ nombre: "X", logoUrl: null, bannerUrl: null, version: 1, colores: { primario: "#123456", secundario: "#5D96C1", sobrePrimario: "#162744" } });
    expect(css).toContain(".dashboard-institutional-card .dashboard-title{color:var(--primary-foreground)}");
  });
});
