// Combinaciones de partida del configurador de marca: quien configura elige una con un clic y, si
// quiere, la ajusta. Todas se leen bien con texto blanco (lo verifica `derive-tokens.test.ts`).
// `secondary` es el color de acción (botones, menú, enlaces); `primary`, el de títulos.
export interface BrandColorPreset {
  name: string;
  primary: string;
  secondary: string;
}

export const BRAND_COLOR_PRESETS: BrandColorPreset[] = [
  { name: "Azul", primary: "#1E3A8A", secondary: "#2563EB" },
  { name: "Verde", primary: "#064E3B", secondary: "#047857" },
  { name: "Turquesa", primary: "#134E4A", secondary: "#0F766E" },
  { name: "Rojo", primary: "#7F1D1D", secondary: "#B91C1C" },
  { name: "Morado", primary: "#3B0764", secondary: "#7C3AED" },
  { name: "Naranja", primary: "#7C2D12", secondary: "#C2410C" },
  { name: "Grafito", primary: "#0F172A", secondary: "#334155" },
];
