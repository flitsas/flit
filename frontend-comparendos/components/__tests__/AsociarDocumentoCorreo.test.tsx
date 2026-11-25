import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ModalAsociar, ModalComparar } from "@/components/ocr/AsociarDocumento";

const comparendo = { id: "c1", numero: "1100100000123", fuente: "SIMIT", activo: true, pagado: false };
const documento = { nombreArchivo: "escaneo.pdf", numeroComparendoExtraido: "1100100000123" };
const actual = { archivoId: "a1", nombre: "anterior.pdf", contentType: "application/pdf", tamanoBytes: 1024, cargadaEn: "2026-11-20T10:00:00Z" };
const entrante = { archivoId: "a2", nombreArchivo: "nuevo.pdf", tipoArchivo: "PDF", numeroComparendoExtraido: "1100100000123" };
const noop = vi.fn();

describe("modales de asociar: qué pasa con el correo", () => {
  it("asociar: dice que se enviará ahora cuando el servidor lo confirma", () => {
    render(<ModalAsociar comparendo={comparendo} documento={documento} correoAlAsociar={{ seEnviara: true, motivoNo: null }} ocupado={false} onConfirmar={noop} onClose={noop} />);
    expect(screen.getByText(/Se enviará ahora al conductor un correo con este documento/)).toBeTruthy();
  });

  it("asociar: dice el motivo cuando no se enviará, y no promete nada sin dato", () => {
    const { unmount } = render(
      <ModalAsociar comparendo={comparendo} documento={documento} correoAlAsociar={{ seEnviara: false, motivoNo: "El conductor no tiene correo registrado." }} ocupado={false} onConfirmar={noop} onClose={noop} />,
    );
    expect(screen.getByText("No se enviará correo al conductor. Motivo: El conductor no tiene correo registrado.")).toBeTruthy();
    unmount();
    render(<ModalAsociar comparendo={comparendo} documento={documento} ocupado={false} onConfirmar={noop} onClose={noop} />);
    expect(screen.queryByText(/correo/i)).toBeNull();
  });

  it("comparar: en reemplazo el correo lleva el documento nuevo", () => {
    render(<ModalComparar comparendo={comparendo} entrante={entrante} actual={actual} correoAlAsociar={{ seEnviara: true, motivoNo: null }} ocupado={false} onReemplazar={noop} onDescartar={noop} onClose={noop} />);
    expect(screen.getByText(/correo con el documento nuevo/)).toBeTruthy();
  });
});

describe("pagado o inactivo: una sola nota", () => {
  const sinCorreo = { seEnviara: false, motivoNo: "El comparendo ya está pagado" };
  const NOTA_CORREO = /No se enviará correo al conductor/;
  const NOTA_AVISOS = /ya no recibe avisos ni correos/;

  it("pagado o inactivo: solo la nota de «ni correos»; la del correo no se repite (asociar y comparar)", () => {
    for (const c of [{ ...comparendo, pagado: true }, { ...comparendo, activo: false }]) {
      const { unmount } = render(<ModalAsociar comparendo={c} documento={documento} correoAlAsociar={sinCorreo} ocupado={false} onConfirmar={noop} onClose={noop} />);
      expect(screen.getByText(NOTA_AVISOS)).toBeTruthy();
      expect(screen.queryByText(NOTA_CORREO)).toBeNull();
      unmount();
      const r = render(<ModalComparar comparendo={c} entrante={entrante} actual={actual} correoAlAsociar={sinCorreo} ocupado={false} onReemplazar={noop} onDescartar={noop} onClose={noop} />);
      expect(screen.getByText(NOTA_AVISOS)).toBeTruthy();
      expect(screen.queryByText(NOTA_CORREO)).toBeNull();
      r.unmount();
    }
  });

  it("otros motivos con el comparendo activo y sin pagar conservan la nota del correo", () => {
    render(<ModalAsociar comparendo={comparendo} documento={documento} correoAlAsociar={{ seEnviara: false, motivoNo: "No hay un conductor asignado" }} ocupado={false} onConfirmar={noop} onClose={noop} />);
    expect(screen.getByText("No se enviará correo al conductor. Motivo: No hay un conductor asignado.")).toBeTruthy();
    expect(screen.queryByText(NOTA_AVISOS)).toBeNull();
  });

  it("pagado pero con correo previsto (no debería pasar): la nota del correo se conserva", () => {
    render(<ModalAsociar comparendo={{ ...comparendo, pagado: true }} documento={documento} correoAlAsociar={{ seEnviara: true, motivoNo: null }} ocupado={false} onConfirmar={noop} onClose={noop} />);
    expect(screen.getByText(/Se enviará ahora al conductor/)).toBeTruthy();
  });
});
