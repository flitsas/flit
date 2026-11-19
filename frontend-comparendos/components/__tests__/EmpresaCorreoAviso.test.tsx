import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import Pagina from "@/app/(app)/configuracion/empresa/page";
import { ToastProvider } from "@/components/Toast";

// Rediseño S3: «Correo para respuestas» va dentro del DP y de la solicitud de cambio de contraventor; vacío, se usa el
// del representante legal y, sin ninguno, ServicioDp responde SIN_CORREO_RESPUESTA. La pantalla lo avisa y, a quien
// puede verlos, le ofrece revisar los representantes. El SuperAdmin no ve el enlace: esa pantalla no recibe la empresa.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));

let sesion: { usuario: { esSuperAdmin: boolean }; permisos: string[] } = { usuario: { esSuperAdmin: false }, permisos: [] };
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion, cargando: false, error: null }),
}));
// El SuperAdmin elige empresa en el selector; aquí se elige sola al montarse.
vi.mock("@/components/SelectorEmpresa", async () => {
  const { useEffect } = await import("react");
  return {
    SelectorEmpresa: ({ onCambio }: { onCambio: (id: string) => void }) => {
      useEffect(() => onCambio("e1"), [onCambio]);
      return null;
    },
  };
});

const CONFIG = {
  empresaId: "e1",
  empresaNombre: "Empresa Uno",
  hasRightPetitionModule: true,
  allowsContravenerConsultation: true,
  allowsContravenerChange: true,
  hasOcrAccess: false,
  hasReportesModule: true,
  notificationChannel: "Email",
  canalCorreo: "Flit",
  remitenteNombre: null,
  cc: [],
  bcc: [],
  tipoIntegracionConductor: "Manual",
  conjuntoPlantillas: "Flit",
  requiereRepresentanteLegal: true,
  destinatariosReportes: [],
  correosAlertaOperativa: [],
  empresaNit: null,
};

const VER = "comparendos.configuracion.ver";
const REPRESENTANTES = "comparendos.representantes.ver";

function abrir(correoRespuesta: string | null, permisos: string[], esSuperAdmin = false, fallaConfiguracion = false) {
  sesion = { usuario: { esSuperAdmin }, permisos };
  apiFetch.mockImplementation(async (ruta: string) => {
    if (ruta === "/configuracion" || ruta === "/admin/empresas/e1/configuracion") {
      if (fallaConfiguracion) throw new Error("No se pudo cargar la configuración.");
      return { ...CONFIG, correoRespuesta };
    }
    if (ruta === "/configuracion/consultas") return { estado: "Activa", canales: [] };
    if (ruta === "/tipologias") return [{ id: "t1", codigo: "DA", nombre: "Solicitud de audiencia", estadosLegalesPermitidos: [], plazoRespuestaOtDiasHabiles: 15, permiteMasivo: false }];
    throw new Error(`ruta sin simular: ${ruta}`);
  });
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
}

const aviso = () => screen.queryByText(/Sin este correo usamos el del representante legal/);
const enlace = () => screen.queryByRole("link", { name: "Revisar representantes legales" });

describe("aviso del correo para respuestas (rediseño S3)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });
  afterEach(cleanup);

  it("vacío: avisa y ofrece revisar los representantes a quien puede verlos", async () => {
    abrir(null, [VER, REPRESENTANTES]);
    await screen.findByText("Cómo salen tus correos");
    expect(aviso()).not.toBeNull();
    expect(enlace()).toHaveAttribute("href", "/configuracion/representantes");
  });

  it("vacío y sin permiso de representantes: avisa, pero sin enlace", async () => {
    abrir("", [VER]);
    await screen.findByText("Cómo salen tus correos");
    expect(aviso()).not.toBeNull();
    expect(enlace()).toBeNull();
  });

  it("SuperAdmin: avisa, pero sin enlace (representantes no recibe la empresa elegida)", async () => {
    abrir(null, [], true);
    await screen.findByText("Cómo salen tus correos");
    expect(aviso()).not.toBeNull();
    expect(enlace()).toBeNull();
  });

  it("con correo: no avisa", async () => {
    abrir("juridico@empresa.com", [VER, REPRESENTANTES]);
    await screen.findByDisplayValue("juridico@empresa.com");
    expect(aviso()).toBeNull();
  });

  it("si falla GET /configuracion, las consultas y las tipologías siguen visibles", async () => {
    abrir(null, [VER], false, true);
    expect(await screen.findByText("No se pudo cargar la configuración.")).toBeInTheDocument();
    expect(await screen.findByText("Consultas automáticas de comparendos")).toBeInTheDocument();
    expect(await screen.findByText("Solicitud de audiencia")).toBeInTheDocument();
  });
});
