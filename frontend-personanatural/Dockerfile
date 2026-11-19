# Imagen del portal Persona Natural: build en tiempo de imagen + next start.
# UNA sola imagen para DEV, QA y PDN: no recibe build-args. Todo lo que cambia por ambiente (CORE_API_ORIGIN) se lee en
# el servidor al atender cada petición (frontend-personanatural/lib/config.server.ts). El compose inyecta PORT.
# Solo usa @flit/auth, para la sesión OIDC de la caja del organismo (/o/{codigo}/caja). El resto del portal no tiene login.

FROM node:22-alpine

ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN corepack enable

WORKDIR /app

# Manifiestos de la app, para aprovechar la caché de capas.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY packages/auth/package.json ./packages/auth/
COPY frontend-personanatural/package.json ./frontend-personanatural/

# Solo las dependencias de la app (incluidas las dev: next build las necesita).
RUN --mount=type=cache,id=pnpm,target=/pnpm/store pnpm install --frozen-lockfile --filter "@flit/frontend-personanatural..."

COPY packages/auth ./packages/auth
COPY frontend-personanatural ./frontend-personanatural
RUN pnpm --filter @flit/frontend-personanatural build

ENV NODE_ENV=production
EXPOSE 4031

# Solo el .next queda del usuario node: next start escribe ahí su caché (imágenes, fetch). Un chown -R de /app
# duplicaba en una capa toda la app con node_modules (~170 s solo en exportar la caché gha del build).
RUN chown -R node:node /app/frontend-personanatural/.next
USER node

CMD ["pnpm", "--filter", "@flit/frontend-personanatural", "start"]
