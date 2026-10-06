# Imagen del hub de la FLIT Suite (B-09): build en tiempo de imagen + next start.
# UNA sola imagen para DEV, QA y PDN: no recibe build-args. Todo lo que cambia por ambiente
# (CORE_API_ORIGIN, FLIT_HOSTS, FLIT_HUB_URL, FLIT_SESSION_SECRET, FLIT_INTERNAL_API_KEY...) se lee en el servidor
# al atender cada petición (frontend-hub/lib/config.server.ts). El compose inyecta PORT.

FROM node:22-alpine

ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN corepack enable

WORKDIR /app

# Manifiestos del hub y de los paquetes que usa, para aprovechar la caché de capas.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY frontend-hub/package.json ./frontend-hub/
COPY packages/ui/package.json ./packages/ui/
COPY packages/brand/package.json ./packages/brand/
COPY packages/auth/package.json ./packages/auth/
COPY packages/shell/package.json ./packages/shell/

# Solo las dependencias del hub y de sus paquetes del workspace (incluidas las dev: next build las necesita).
RUN --mount=type=cache,id=pnpm,target=/pnpm/store pnpm install --frozen-lockfile --filter "@flit/frontend-hub..."

COPY frontend-hub ./frontend-hub
COPY packages/ui ./packages/ui
COPY packages/brand ./packages/brand
COPY packages/auth ./packages/auth
COPY packages/shell ./packages/shell
RUN pnpm --filter @flit/frontend-hub build

ENV NODE_ENV=production
EXPOSE 4022

RUN chown -R node:node /app
USER node

CMD ["pnpm", "--filter", "@flit/frontend-hub", "start"]
