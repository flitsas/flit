// HU #12414 AC2 — validación en cliente: formato, peso y dimensiones (lee la imagen en el navegador).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  MAX_LOGO_BYTES,
  MAX_LOGO_WIDTH,
  MIN_LOGO_HEIGHT,
  MIN_LOGO_WIDTH,
  logoDimensionsHint,
  validateLogoFile,
} from "../validate-logo";

class FakeImage {
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  naturalWidth = 0;
  naturalHeight = 0;
  private _src = "";
  set src(value: string) {
    this._src = value;
    queueMicrotask(() => {
      if (FakeImage.shouldFail) {
        this.onerror?.();
        return;
      }
      this.naturalWidth = FakeImage.nextWidth;
      this.naturalHeight = FakeImage.nextHeight;
      this.onload?.();
    });
  }
  get src() {
    return this._src;
  }
  static nextWidth = 300;
  static nextHeight = 100;
  static shouldFail = false;
}

function pngFile(sizeBytes: number, type = "image/png"): File {
  const bytes = new Uint8Array(sizeBytes);
  return new File([bytes], "logo.png", { type });
}

beforeEach(() => {
  FakeImage.nextWidth = 300;
  FakeImage.nextHeight = 100;
  FakeImage.shouldFail = false;
  vi.stubGlobal("Image", FakeImage as unknown as typeof Image);
  vi.stubGlobal("URL", { ...URL, createObjectURL: vi.fn(() => "blob:mock"), revokeObjectURL: vi.fn() });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("validateLogoFile", () => {
  it("acepta un PNG dentro de peso y dimensiones válidas (happy path)", async () => {
    const result = await validateLogoFile(pngFile(1024));
    expect(result).toEqual({ ok: true, width: 300, height: 100 });
  });

  it("rechaza un formato fuera de PNG/JPEG/WebP con BRANDING_LOGO_FORMAT", async () => {
    const result = await validateLogoFile(pngFile(1024, "image/svg+xml"));
    expect(result).toEqual({ ok: false, code: "BRANDING_LOGO_FORMAT" });
  });

  it("rechaza un archivo de más de 512 KB con BRANDING_LOGO_TOO_LARGE (edge case)", async () => {
    const result = await validateLogoFile(pngFile(MAX_LOGO_BYTES + 1));
    expect(result).toEqual({ ok: false, code: "BRANDING_LOGO_TOO_LARGE" });
  });

  it("rechaza dimensiones por debajo del mínimo con BRANDING_LOGO_DIMENSIONS", async () => {
    FakeImage.nextWidth = MIN_LOGO_WIDTH - 1;
    FakeImage.nextHeight = MIN_LOGO_HEIGHT;
    const result = await validateLogoFile(pngFile(1024));
    expect(result).toEqual({ ok: false, code: "BRANDING_LOGO_DIMENSIONS" });
  });

  it("rechaza dimensiones por encima del máximo con BRANDING_LOGO_DIMENSIONS", async () => {
    FakeImage.nextWidth = MAX_LOGO_WIDTH + 1;
    FakeImage.nextHeight = MAX_LOGO_WIDTH + 1;
    const result = await validateLogoFile(pngFile(1024));
    expect(result).toEqual({ ok: false, code: "BRANDING_LOGO_DIMENSIONS" });
  });

  it("trata un archivo ilegible como imagen como BRANDING_LOGO_FORMAT (contrato)", async () => {
    FakeImage.shouldFail = true;
    const result = await validateLogoFile(pngFile(1024));
    expect(result).toEqual({ ok: false, code: "BRANDING_LOGO_FORMAT" });
  });
});

describe("logoDimensionsHint", () => {
  it("describe formato, peso y rango de dimensiones", () => {
    expect(logoDimensionsHint()).toContain("512 KB");
    expect(logoDimensionsHint()).toContain("120x40");
    expect(logoDimensionsHint()).toContain("2000x2000");
  });
});
