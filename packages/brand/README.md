# @flit/brand

Marca por host de la FLIT Suite (Marca Blanca, ADR-0060 §D5). Salió de `frontend/lib/brand` y
`frontend/components/brand` en la tarea **B-09** para que la usen Trámites y el hub; allí quedan
archivos que solo reexportan desde aquí, así que ningún consumidor de Trámites cambia.

- El código se publica como TypeScript: cada app lo compila con `transpilePackages: ["@flit/brand"]`.
- **Hosts FLIT:** `isFlitHost(host, lista)`. Trámites usa `NEXT_PUBLIC_FLIT_HOSTS` (horneada en el build);
  el hub define `FLIT_HOSTS`, que el servidor lee en runtime, para usar la misma imagen en los tres ambientes.
- `resolveBrand()` solo corre en el servidor. En host FLIT no llama a nadie; en un dominio de red pide
  `GET /api/v1/public/branding` al gateway (`BRANDING_INTERNAL_API_URL`, `FLIT_INTERNAL_API_KEY`).
- `BrandLogo` usa `/assets/logo-flit-{white,dark}.svg` en host FLIT: cada app los sirve desde su `public/`.

| Exporta | Qué es |
|---|---|
| `types` | `Brand`, `FLIT_BRAND`, `isFlitBrand`, `brandDisplayName` |
| `hosts` | `isFlitHost` |
| `resolve-brand.server` | `resolveBrand()` |
| `BrandProvider`, `BrandStyle`, `BrandLogo` | Contexto, variables CSS antes de la primera pintura y logo |
| `contrast`, `derive-dark` | Contraste WCAG y tonos de modo oscuro |
