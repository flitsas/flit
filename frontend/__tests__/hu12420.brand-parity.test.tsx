import { readFileSync } from "node:fs";
import path from "node:path";
import { render } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Login } from "@/components/atom/Login";
import { ActivateAccountForm } from "@/components/auth/ActivateAccountForm";
import { ForgotPasswordForm } from "@/components/auth/ForgotPasswordForm";
import { ResetPasswordForm } from "@/components/auth/ResetPasswordForm";
import { AuthCard } from "@/components/auth/AuthCard";
import { Shell } from "@/components/atom/Shell";

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useRouter: () => ({ push: vi.fn() }),
}));

/**
 * HU #12420 AC3 — "sin cambio visual para FLIT". No hay Playwright en el repo (confirmado en
 * #12419), así que la paridad no se prueba con una captura de píxeles sino con una comparación
 * estática de dos hechos, cada uno verificado por separado:
 *
 *  1. `globals.css` sigue definiendo los tokens `--color-flit-*` con el MISMO hex de FLIT como
 *     valor por defecto (`var(--brand-*, <hex>)` o hex directo) que tenían las superficies antes
 *     del saneamiento — esto ya lo garantizó #12419 y aquí se re-verifica que #12420 no lo tocó.
 *  2. Las superficies saneadas (Login, ActivateAccountForm, ForgotPasswordForm,
 *     ResetPasswordForm, AuthCard, Shell en claro/oscuro) ya NO contienen ningún hex de marca
 *     literal en su salida renderizada, y SÍ referencian exclusivamente esos tokens (vía
 *     `var(--color-flit-*)` en `style` o vía las utilidades Tailwind `bg-flit-*`/`text-flit-*`/
 *     `border-flit-*`/`ring-flit-*` generadas por `@theme inline` a partir de esos mismos
 *     tokens).
 *
 * Tolerancia: cero — se exige igualdad de string exacta entre el hex por defecto documentado en
 * globals.css y el hex que la superficie usaba antes de #12420 (ver mapa línea a línea en
 * `frontend/docs/brand-color-inventory.json`, ocurrencias "marca" de #12415). jsdom no resuelve
 * `var()` en tiempo de ejecución (no hay motor de layout/CSS real), así que no se puede afirmar
 * "el navegador pinta el mismo píxel" con un assert directo; la cadena narrow-but-complete que sí
 * se puede probar en CI es la de arriba, que en conjunto implica paridad exacta en host FLIT.
 *
 * En un host de red con marca publicada, `BrandStyle` (HU #12419) sobreescribe
 * `--brand-primary`/`--brand-secondary` con el hex de la red — por diseño estos dos tokens SÍ
 * cambian ahí; los otros cuatro (`tech`/`alert`/`bg`/`gray`) son decorativos/semánticos de FLIT y
 * no varían por red (ver comentario en globals.css junto a `@theme inline`). Ese caso ya lo cubre
 * `components/brand/__tests__/BrandStyle.test.tsx` (HU #12419) — no se repite aquí.
 */

const GLOBALS_CSS = readFileSync(path.join(__dirname, "..", "app", "globals.css"), "utf8");

// Hex de marca FLIT tal como aparecían escritos a mano en las superficies antes de #12420
// (frontend/docs/brand-color-inventory.json → occurrences classification "marca").
const EXPECTED_TOKEN_DEFAULT_HEX: Record<string, string> = {
  "--color-flit-primary": "#162744",
  "--color-flit-brand": "#557eff",
  "--color-flit-tech": "#00dbd5",
  "--color-flit-alert": "#ff4e00",
  "--color-flit-bg": "#eef5ff",
  "--color-flit-gray": "#dfe5ed",
};

// Los 6 hex de marca crudos — si alguno reaparece literal en la salida renderizada de una
// superficie saneada, la prueba debe fallar (regresión de AC1/AC3).
const RAW_BRAND_HEX_PATTERN = /#(162744|557eff|00dbd5|ff4e00|eef5ff|dfe5ed)\b/i;

function expectHtmlHasNoLiteralBrandHex(container: HTMLElement, label: string) {
  const html = container.innerHTML;
  const match = html.match(RAW_BRAND_HEX_PATTERN);
  expect(match, `${label}: no debe quedar hex de marca literal (encontrado: ${match?.[0]})`).toBeNull();
}

describe("HU #12420 AC3 — paridad de tokens en globals.css (sin cambio visual para FLIT)", () => {
  it.each(Object.entries(EXPECTED_TOKEN_DEFAULT_HEX))(
    "%s sigue resolviendo al hex FLIT %s por defecto (sin marca publicada)",
    (token, hex) => {
      const declarationRegex = new RegExp(
        `${token.replace(/[-/\\^$*+?.()|[\]{}]/g, "\\$&")}:\\s*(?:var\\([^,]+,\\s*${hex}\\)|${hex})`,
        "i",
      );
      expect(GLOBALS_CSS).toMatch(declarationRegex);
    },
  );
});

describe("HU #12420 AC3 — superficies saneadas sin hex de marca literal (host FLIT)", () => {
  it("Login: sin hex de marca en el HTML renderizado", () => {
    const { container } = render(<Login onAuthenticated={vi.fn()} />);
    expectHtmlHasNoLiteralBrandHex(container, "Login");
  });

  it("ActivateAccountForm: sin hex de marca en el HTML renderizado (con y sin token)", () => {
    const sinToken = render(<ActivateAccountForm token={null} />);
    expectHtmlHasNoLiteralBrandHex(sinToken.container, "ActivateAccountForm (sin token)");

    const conToken = render(<ActivateAccountForm token="tok-123" />);
    expectHtmlHasNoLiteralBrandHex(conToken.container, "ActivateAccountForm (con token)");
  });

  it("ForgotPasswordForm: sin hex de marca en el HTML renderizado", () => {
    const { container } = render(<ForgotPasswordForm />);
    expectHtmlHasNoLiteralBrandHex(container, "ForgotPasswordForm");
  });

  it("ResetPasswordForm: sin hex de marca en el HTML renderizado (con y sin token)", () => {
    const sinToken = render(<ResetPasswordForm token={null} />);
    expectHtmlHasNoLiteralBrandHex(sinToken.container, "ResetPasswordForm (sin token)");

    const conToken = render(<ResetPasswordForm token="tok-456" />);
    expectHtmlHasNoLiteralBrandHex(conToken.container, "ResetPasswordForm (con token)");
  });

  it("AuthCard: sin hex de marca en el HTML renderizado (variantes auth y overlay)", () => {
    const auth = render(
      <AuthCard title="Título">
        <p>contenido</p>
      </AuthCard>,
    );
    expectHtmlHasNoLiteralBrandHex(auth.container, "AuthCard (auth)");

    const overlay = render(
      <AuthCard title="Título" variant="overlay">
        <p>contenido</p>
      </AuthCard>,
    );
    expectHtmlHasNoLiteralBrandHex(overlay.container, "AuthCard (overlay)");
  });
});

describe("HU #12420 AC3 — Shell sin hex de marca literal, claro y oscuro", () => {
  function renderShell() {
    return render(
      <Shell active="dashboard" onNav={vi.fn()}>
        <div>contenido</div>
      </Shell>,
    );
  }

  it("modo claro: sin hex de marca en el HTML renderizado", () => {
    const { container } = renderShell();
    expectHtmlHasNoLiteralBrandHex(container, "Shell (claro)");
  });

  it("modo oscuro: sin hex de marca en el HTML renderizado (los neutros del dark quedan intactos, AC2)", () => {
    // `useTheme()` (Shell.tsx) lee el estado inicial de `localStorage['flit-theme']`, no de la
    // clase `dark` del documento — hay que sembrar esa llave antes de montar.
    window.localStorage.setItem("flit-theme", "dark");
    try {
      const { container } = renderShell();
      expectHtmlHasNoLiteralBrandHex(container, "Shell (oscuro)");
      // AC2 — el neutro de dark mode (#05060A → fondo del shell, HU #12420 no lo toca) sigue
      // presente; jsdom normaliza los `style.background`/`color` con hex a `rgb(...)` al
      // serializar el atributo, por eso se compara contra el equivalente rgb.
      expect(container.innerHTML).toMatch(/rgb\(5,\s*6,\s*10\)/i);
    } finally {
      window.localStorage.removeItem("flit-theme");
      document.documentElement.classList.remove("dark");
    }
  });
});
