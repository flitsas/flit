/**
 * HU #10703 — DocumentPreviewModal: estados previsable (PDF, imagen) y fallback.
 */
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";

const BASE_PROPS = {
  open: true,
  onClose: vi.fn(),
  title: "Factura.pdf",
  onDownload: vi.fn(),
};

describe("DocumentPreviewModal — estado cargando", () => {
  it("muestra skeleton con aria-busy mientras loading=true", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading
        mimetype="application/pdf"
        url={null}
        error={null}
      />,
    );
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByTestId("preview-iframe")).not.toBeInTheDocument();
    expect(screen.queryByTestId("preview-image")).not.toBeInTheDocument();
  });
});

describe("DocumentPreviewModal — PDF previsualizable", () => {
  it("renderiza dialog con role=dialog y aria-modal", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading={false}
        mimetype="application/pdf"
        url="https://s3.example.com/signed/doc.pdf"
        error={null}
      />,
    );
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
  });

  it("renderiza iframe para PDFs", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading={false}
        mimetype="application/pdf"
        url="https://s3.example.com/signed/doc.pdf"
        error={null}
      />,
    );
    const iframe = screen.getByTestId("preview-iframe");
    expect(iframe).toBeInTheDocument();
    expect(iframe).toHaveAttribute("src", "https://s3.example.com/signed/doc.pdf");
  });

  it("incluye botón de descarga cuando onDownload se provee", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading={false}
        mimetype="application/pdf"
        url="https://s3.example.com/signed/doc.pdf"
        error={null}
      />,
    );
    expect(screen.getByRole("button", { name: "Descargar documento" })).toBeInTheDocument();
  });
});

describe("DocumentPreviewModal — imagen previsualizable", () => {
  it.each(["image/jpeg", "image/png", "image/webp"] as const)(
    "renderiza img para %s",
    (mime) => {
      render(
        <DocumentPreviewModal
          {...BASE_PROPS}
          loading={false}
          mimetype={mime}
          url="https://s3.example.com/signed/photo.jpg"
          error={null}
        />,
      );
      const img = screen.getByTestId("preview-image");
      expect(img).toBeInTheDocument();
      expect(img).toHaveAttribute("src", "https://s3.example.com/signed/photo.jpg");
      expect(img).toHaveAttribute("alt", "Factura.pdf");
    },
  );
});

describe("DocumentPreviewModal — fallback para tipos no previsables", () => {
  it("muestra mensaje y botón de descarga para application/zip", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading={false}
        mimetype="application/zip"
        url="https://s3.example.com/signed/archive.zip"
        error={null}
      />,
    );
    expect(screen.getByTestId("preview-fallback")).toBeInTheDocument();
    expect(screen.getByText(/no se puede previsualizar/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Descargar documento" })).toBeInTheDocument();
  });

  it("dispara onDownload al pulsar el botón de descarga", () => {
    const onDownload = vi.fn();
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        onDownload={onDownload}
        loading={false}
        mimetype="application/zip"
        url="https://s3.example.com/signed/archive.zip"
        error={null}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Descargar documento" }));
    expect(onDownload).toHaveBeenCalledTimes(1);
  });
});

describe("DocumentPreviewModal — estado error", () => {
  it("muestra mensaje de error y botón de descarga fallback", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading={false}
        mimetype="application/pdf"
        url={null}
        error="No se pudo obtener la URL de previsualización."
      />,
    );
    expect(screen.getByTestId("preview-error")).toBeInTheDocument();
    expect(
      screen.getByText("No se pudo obtener la URL de previsualización."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Descargar documento" })).toBeInTheDocument();
  });
});

describe("DocumentPreviewModal — estado idle (sin URL)", () => {
  it("muestra estado vacío cuando url=null y no hay error ni carga", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading={false}
        mimetype="application/pdf"
        url={null}
        error={null}
      />,
    );
    expect(screen.getByTestId("preview-idle")).toBeInTheDocument();
    expect(screen.getByText(/no hay previsualización disponible/i)).toBeInTheDocument();
  });
});

describe("DocumentPreviewModal — accesibilidad", () => {
  it("Escape llama a onClose", () => {
    const onClose = vi.fn();
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        onClose={onClose}
        loading={false}
        mimetype="application/pdf"
        url="https://s3.example.com/signed/doc.pdf"
        error={null}
      />,
    );
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("open=false no renderiza el dialog", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        open={false}
        loading={false}
        mimetype="application/pdf"
        url="https://s3.example.com/signed/doc.pdf"
        error={null}
      />,
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("botones de descarga tienen aria-label descriptivo", () => {
    render(
      <DocumentPreviewModal
        {...BASE_PROPS}
        loading={false}
        mimetype="application/zip"
        url="https://s3.example.com/signed/archive.zip"
        error={null}
      />,
    );
    const btn = screen.getByRole("button", { name: "Descargar documento" });
    expect(btn).toHaveAttribute("aria-label", "Descargar documento");
  });
});
