import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";
import noLiteralBrandColor from "./eslint-rules/no-literal-brand-color.mjs";

// Superficies del alcance cerrado de Marca Blanca (HU #12415/#12419/#12420):
// acceso, activación de cuenta, recuperación de contraseña, cabecera/menú e
// icono/título de la pestaña. Mantener sincronizado con SURFACES de
// scripts/brand-color-inventory.mjs — es la misma lista de archivos.
const MARCA_BLANCA_SURFACE_GLOBS = [
  "components/atom/Login.tsx",
  "app/login/page.tsx",
  "app/invite/activate/page.tsx",
  "components/auth/ActivateAccountForm.tsx",
  "app/auth/forgot-password/page.tsx",
  "app/auth/reset-password/page.tsx",
  "components/auth/ForgotPasswordForm.tsx",
  "components/auth/ResetPasswordForm.tsx",
  "components/auth/AuthCard.tsx",
  "components/atom/Shell.tsx",
  "components/atom/dock/DockDesktop.tsx",
  "app/layout.tsx",
];

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    // Regla acotada del inventario (HU #12415, AC3). No aplica al resto del
    // repositorio: fuera de estos 12 archivos, ESLint sigue exactamente igual.
    files: MARCA_BLANCA_SURFACE_GLOBS,
    plugins: {
      "brand-color": { rules: { "no-literal-brand-color": noLiteralBrandColor } },
    },
    rules: {
      "brand-color/no-literal-brand-color": "error",
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
]);

export default eslintConfig;
