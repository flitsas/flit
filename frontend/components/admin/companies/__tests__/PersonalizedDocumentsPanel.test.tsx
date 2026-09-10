// HU #11315 (Feature #11309, ADR-0042) — panel de documentos personalizados por compañía. Cubre:
// (a) visibilidad condicionada al canal (FLIT_SMTP no renderiza nada; TENANT_API sí); (b) la
// advertencia de activación bloquea el envío hasta que el usuario confirma explícitamente; (c) el
// historial pinta autor, fecha y vigencia; (d) un rechazo de validación (422) se muestra con su
// motivo concreto, sin tocar el historial. API y toast mockeados.
//
// Uso de ejemplo:
//   render(<PersonalizedDocumentsPanel tenantId={TENANT} enrutamientoSMTP="TENANT_API" />);
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PersonalizedDocumentsPanel } from "../PersonalizedDocumentsPanel";
import { ApiValidationError } from "@/lib/api/types";
import type { PersonalizedDocumentGroup } from "@/lib/api/admin-personalized-documents";

vi.mock("@/lib/api/admin-personalized-documents", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-personalized-documents")>(
    "@/lib/api/admin-personalized-documents",
  );
  return {
    ...actual,
    fetchPersonalizedDocuments: vi.fn(),
    uploadAndConfirmPersonalizedDocument: vi.fn(),
    activatePersonalizedDocumentVersion: vi.fn(),
    deactivatePersonalizedDocument: vi.fn(),
    getPersonalizedDocumentView: vi.fn(),
  };
});

const show = vi.fn();
vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show }),
}));

import {
  fetchPersonalizedDocuments,
  getPersonalizedDocumentView,
  uploadAndConfirmPersonalizedDocument,
} from "@/lib/api/admin-personalized-documents";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";

function emptyGroups(): PersonalizedDocumentGroup[] {
  return [
    { documentType: "mandato", active: null, history: [] },
    { documentType: "tramite_virtual", active: null, history: [] },
  ];
}

function pdfFile(name = "mandato-compania.pdf"): File {
  return new File(["%PDF-1.4 contenido"], name, { type: "application/pdf" });
}

describe("PersonalizedDocumentsPanel (HU #11315)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("(a) no se renderiza NADA con el canal FLIT_SMTP", () => {
    const { container } = render(
      <PersonalizedDocumentsPanel tenantId={TENANT} enrutamientoSMTP="FLIT_SMTP" />,
    );
    expect(container).toBeEmptyDOMElement();
    expect(fetchPersonalizedDocuments).not.toHaveBeenCalled();
  });

  it("(a) se renderiza con el canal TENANT_API", async () => {
    vi.mocked(fetchPersonalizedDocuments).mockResolvedValue(emptyGroups());
    render(<PersonalizedDocumentsPanel tenantId={TENANT} enrutamientoSMTP="TENANT_API" />);

    expect(
      await screen.findByRole("heading", { name: /documentos personalizados/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /^mandato$/i, level: 3 })).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: /^solicitud de trámite virtual$/i, level: 3 }),
    ).toBeInTheDocument();
  });

  it("(b) la advertencia bloquea la activación hasta que se confirma explícitamente", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchPersonalizedDocuments).mockResolvedValue(emptyGroups());
    vi.mocked(uploadAndConfirmPersonalizedDocument).mockResolvedValue({
      id: "v1",
      version: 1,
      status: "activo",
      sha256: "abc",
      pageCount: 3,
    });
    render(<PersonalizedDocumentsPanel tenantId={TENANT} enrutamientoSMTP="TENANT_API" />);

    await screen.findByRole("heading", { name: /documentos personalizados/i });

    const [mandatoFileInput] = screen.getAllByLabelText(/selecciona el pdf del documento personalizado/i);
    await user.upload(mandatoFileInput, pdfFile());

    const [cargarButton] = screen.getAllByRole("button", { name: /cargar y activar/i });
    await user.click(cargarButton);

    const dialog = await screen.findByRole("dialog", { name: /activar mandato/i });
    expect(dialog).toHaveTextContent(/no incluirá los bloques de firma del mandatario/i);

    const confirmButton = screen.getByRole("button", { name: /confirmar activación/i });
    expect(confirmButton).toBeDisabled();
    expect(uploadAndConfirmPersonalizedDocument).not.toHaveBeenCalled();

    const checkbox = screen.getByRole("checkbox", { name: /entiendo la advertencia/i });
    await user.click(checkbox);
    expect(confirmButton).not.toBeDisabled();

    await user.click(confirmButton);
    await waitFor(() =>
      expect(uploadAndConfirmPersonalizedDocument).toHaveBeenCalledWith(TENANT, "mandato", expect.any(File)),
    );
  });

  it("(c) el historial pinta autor, fecha y vigencia", async () => {
    const groups: PersonalizedDocumentGroup[] = [
      {
        documentType: "mandato",
        active: {
          id: "v2",
          version: 2,
          status: "activo",
          isActive: true,
          filename: "mandato-v2.pdf",
          sha256: "hash2",
          pageCount: 4,
          createdAt: "2026-08-01T10:00:00Z",
          createdBy: "11111111-2222-3333-4444-555555555555",
          activatedAt: "2026-08-02T10:00:00Z",
          activatedBy: "11111111-2222-3333-4444-555555555555",
          deactivatedAt: null,
          deactivatedBy: null,
        },
        history: [
          {
            id: "v2",
            version: 2,
            status: "activo",
            isActive: true,
            filename: "mandato-v2.pdf",
            sha256: "hash2",
            pageCount: 4,
            createdAt: "2026-08-01T10:00:00Z",
            createdBy: "11111111-2222-3333-4444-555555555555",
            activatedAt: "2026-08-02T10:00:00Z",
            activatedBy: "11111111-2222-3333-4444-555555555555",
            deactivatedAt: null,
            deactivatedBy: null,
          },
          {
            id: "v1",
            version: 1,
            status: "historico",
            isActive: false,
            filename: "mandato-v1.pdf",
            sha256: "hash1",
            pageCount: 2,
            createdAt: "2026-07-01T10:00:00Z",
            createdBy: "99999999-8888-7777-6666-555555555555",
            activatedAt: "2026-07-01T10:00:00Z",
            activatedBy: "99999999-8888-7777-6666-555555555555",
            deactivatedAt: "2026-08-02T10:00:00Z",
            deactivatedBy: "11111111-2222-3333-4444-555555555555",
          },
        ],
      },
      { documentType: "tramite_virtual", active: null, history: [] },
    ];
    vi.mocked(fetchPersonalizedDocuments).mockResolvedValue(groups);
    render(<PersonalizedDocumentsPanel tenantId={TENANT} enrutamientoSMTP="TENANT_API" />);

    const table = await screen.findByRole("table", { name: /historial de mandato/i });
    expect(table).toHaveTextContent("11111111"); // autor (id corto) de la vigente
    expect(table).toHaveTextContent("99999999"); // autor de la histórica
    expect(table).toHaveTextContent("01/07/2026"); // fecha de creación de v1
    expect(table).toHaveTextContent("01/08/2026"); // fecha de creación de v2
    expect(table).toHaveTextContent("Vigente");
    expect(table).toHaveTextContent("Histórico");

    // Solo la histórica ofrece reactivar; la vigente no.
    expect(screen.getByRole("button", { name: /reactivar/i })).toBeInTheDocument();
  });

  it("(regresión) la vista previa se pinta en el modal y NO dispara descarga del archivo", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchPersonalizedDocuments).mockResolvedValue([
      {
        documentType: "mandato",
        active: {
          id: "v1",
          version: 1,
          status: "activo",
          isActive: true,
          filename: "mandato-v1.pdf",
          sha256: "hash1",
          pageCount: 2,
          createdAt: "2026-08-01T10:00:00Z",
          createdBy: "11111111-2222-3333-4444-555555555555",
          activatedAt: "2026-08-01T10:00:00Z",
          activatedBy: "11111111-2222-3333-4444-555555555555",
          deactivatedAt: null,
          deactivatedBy: null,
        },
        history: [],
      },
      { documentType: "tramite_virtual", active: null, history: [] },
    ]);
    const presignedUrl = "https://s3.example.com/objeto?firma=abc";
    vi.mocked(getPersonalizedDocumentView).mockResolvedValue({
      url: presignedUrl,
      expiresAt: "2026-08-10T18:00:00Z",
    });

    // El file-manager sirve el objeto como binary/octet-stream y sin Content-Disposition: si el
    // iframe apuntara a la URL presignada, el navegador DESCARGARÍA el PDF en vez de pintarlo. La
    // corrección descarga los bytes y los re-empaqueta como Blob con el mimetype real.
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response(new Blob(["%PDF-1.4"]), { status: 200 }));
    const createObjectURL = vi.fn(() => "blob:objeto-tipado");
    const revokeObjectURL = vi.fn();
    vi.stubGlobal("URL", { ...URL, createObjectURL, revokeObjectURL });

    try {
      render(<PersonalizedDocumentsPanel tenantId={TENANT} enrutamientoSMTP="TENANT_API" />);
      await screen.findByRole("heading", { name: /documentos personalizados/i });

      await user.click(screen.getByRole("button", { name: /vista previa/i }));

      const iframe = await waitFor(() => {
        const el = document.querySelector("iframe");
        if (!el) throw new Error("sin iframe");
        return el;
      });

      // Se descargaron los bytes de la URL presignada...
      expect(fetchSpy).toHaveBeenCalledWith(presignedUrl);
      // ...y se re-empaquetaron con el mimetype real.
      expect(createObjectURL).toHaveBeenCalledWith(expect.objectContaining({ type: "application/pdf" }));
      // El iframe apunta al blob tipado, NUNCA a la URL presignada (que fuerza descarga).
      expect(iframe.getAttribute("src")).toBe("blob:objeto-tipado");
      expect(iframe.getAttribute("src")).not.toBe(presignedUrl);

      // Al cerrar se libera el object URL: sin esto cada vista previa filtra memoria.
      await user.click(screen.getByRole("button", { name: /cerrar/i }));
      await waitFor(() => expect(revokeObjectURL).toHaveBeenCalledWith("blob:objeto-tipado"));
    } finally {
      fetchSpy.mockRestore();
      vi.unstubAllGlobals();
    }
  });

  it("(d) el error de validación (422) se muestra con su motivo concreto", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchPersonalizedDocuments).mockResolvedValue(emptyGroups());
    vi.mocked(uploadAndConfirmPersonalizedDocument).mockRejectedValue(
      new ApiValidationError(
        [
          {
            field: "file",
            code: "excede_paginas",
            message: "El PDF excede el máximo de 30 páginas.",
          } as never,
        ],
        422,
      ),
    );
    render(<PersonalizedDocumentsPanel tenantId={TENANT} enrutamientoSMTP="TENANT_API" />);

    await screen.findByRole("heading", { name: /documentos personalizados/i });
    const [mandatoFileInput] = screen.getAllByLabelText(/selecciona el pdf del documento personalizado/i);
    await user.upload(mandatoFileInput, pdfFile());

    const [cargarButton] = screen.getAllByRole("button", { name: /cargar y activar/i });
    await user.click(cargarButton);

    await user.click(await screen.findByRole("checkbox", { name: /entiendo la advertencia/i }));
    await user.click(screen.getByRole("button", { name: /confirmar activación/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/excede el máximo de 30 páginas/i);
    // El diálogo se cierra; el archivo rechazado no queda en un historial.
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
