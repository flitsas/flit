// HU #12242 (Feature #12236) — el carrusel de bienvenida del gestor deja de anunciar mensajes de
// relleno hardcodeados y pasa a mostrar, después del slide fijo, los banners Activos que configura
// el Administrador (endpoint público `GET /api/v1/public/banners/active`).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Dashboard } from "../Dashboard";

const fetchAnalyticsOverview = vi.fn();
const fetchMonthlyTrend = vi.fn();
const listTenantBiometricValidations = vi.fn();
const getActiveBanners = vi.fn();

vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: (...args: unknown[]) => fetchAnalyticsOverview(...args),
  fetchMonthlyTrend: (...args: unknown[]) => fetchMonthlyTrend(...args),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listTenantBiometricValidations: (...args: unknown[]) => listTenantBiometricValidations(...args),
  },
}));

// El selector de compañía (SuperAdmin) no interviene: sin token, `isSuperAdmin` es false y
// `fetchCompaniesIndex` nunca se llama.
vi.mock("@/lib/api/public-banners", () => ({
  getActiveBanners: (...args: unknown[]) => getActiveBanners(...args),
  bannerImageUrl: (id: string) => `http://api.test/api/v1/public/banners/${id}/image`,
}));

const OVERVIEW = { categories: [] };
const TREND = { items: [] };
const BIOMETRICS = {
  validations: [],
  stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
  page: 1,
  pageSize: 20,
  total: 0,
};

function banner(overrides: Partial<{ id: string; name: string; linkUrl: string | null }> = {}) {
  return { id: "banner-1", name: "Novedad de la plataforma", linkUrl: null, ...overrides };
}

describe("Dashboard — carrusel de bienvenida con banners Activos (HU #12242)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
    fetchMonthlyTrend.mockResolvedValue(TREND);
    listTenantBiometricValidations.mockResolvedValue(BIOMETRICS);
  });

  it("AC1 — sin banners activos, el carrusel solo muestra el slide fijo de bienvenida", async () => {
    getActiveBanners.mockResolvedValue([]);
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() => expect(getActiveBanners).toHaveBeenCalled());
    // Un solo punto de navegación: no hay más slides detrás del fijo.
    expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(1);
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("AC1 — los banners activos se agregan como slides después del fijo", async () => {
    getActiveBanners.mockResolvedValue([
      banner({ id: "b1", name: "Banner uno" }),
      banner({ id: "b2", name: "Banner dos", linkUrl: "https://flit.example/novedad" }),
    ]);
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() => expect(getActiveBanners).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(3),
    );

    // Avanza al primer banner (slide 2 de 3): sin enlace, no hay ningún <a> envolviendo el banner
    // ni título visible — el nombre solo viaja como texto accesible de la imagen (alt).
    await userEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    const primerBannerImg = await screen.findByRole("img", { name: "Banner uno" });
    expect(primerBannerImg).toHaveAttribute(
      "src",
      "http://api.test/api/v1/public/banners/b1/image",
    );
    expect(screen.queryByRole("link", { name: "Banner uno" })).not.toBeInTheDocument();
    // El nombre sigue presente para lectores de pantalla (sr-only), pero no como texto visible.
    expect(screen.getByText("Banner uno")).toHaveClass("sr-only");

    // Avanza al segundo banner, que sí tiene enlace: el enlace cubre todo el banner (clic en
    // cualquier punto navega), sin ningún título visible — el nombre es el nombre accesible
    // (aria-label) del propio <a>, no texto en pantalla.
    await userEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    expect(await screen.findByRole("img", { name: "Banner dos" })).toBeInTheDocument();
    const enlace = screen.getByRole("link", { name: "Banner dos" });
    expect(enlace).toHaveAttribute("href", "https://flit.example/novedad");
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace).toHaveAttribute("rel", "noopener noreferrer");
    expect(enlace.textContent).toBe("");
  });

  it("AC3 — un fallo al consultar banners degrada al slide fijo, sin romper el dashboard", async () => {
    getActiveBanners.mockRejectedValue(new Error("network error"));
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() => expect(getActiveBanners).toHaveBeenCalled());
    expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(1);
    // El resto del dashboard (KPIs) se sigue viendo con normalidad.
    expect(await screen.findByText("Total Trámites")).toBeInTheDocument();
  });

  it("AC3 — si la imagen de un banner no carga, ese slide se retira sin romper el carrusel", async () => {
    getActiveBanners.mockResolvedValue([banner({ id: "roto", name: "Banner roto" })]);
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() =>
      expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(2),
    );
    await userEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    const img = await screen.findByRole("img", { name: "Banner roto" });

    img.dispatchEvent(new Event("error"));

    await waitFor(() =>
      expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(1),
    );
    expect(screen.queryByRole("img", { name: "Banner roto" })).not.toBeInTheDocument();
  });
});
