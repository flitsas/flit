# Imagen del portal Persona Natural: build en tiempo de imagen + next start.
# UNA sola imagen para DEV, QA y PDN: no recibe build-args. Todo lo que cambia por ambiente (CORE_API_ORIGIN) se lee en
# el servidor al atender cada petición (frontend-personanatural/lib/config.server.ts). El compose inyecta PORT.
# No usa los paquetes @flit/*: el portal no tiene login del hub ni barra de la suite.

FROM node:22-alpine

ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN corepack enable

WORKDIR /app

# Manifiestos de la app, para aprovechar la caché de capas.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY frontend-personanatural/package.json ./frontend-personanatural/

# Solo las dependencias de la app (incluidas las dev: next build las necesita).
RUN --mount=type=cache,id=pnpm,target=/pnpm/store pnpm install --frozen-lockfile --filter "@flit/frontend-personanatural..."

COPY frontend-personanatural ./frontend-personanatural
RUN pnpm --filter @flit/frontend-personanatural build

ENV NODE_ENV=production
EXPOSE 4031

RUN chown -R node:node /app
USER node

CMD ["pnpm", "--filter", "@flit/frontend-personanatural", "start"]
