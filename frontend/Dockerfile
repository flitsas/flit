# Imagen de PRODUCCION del frontend (Next.js): build en tiempo de imagen + next start.
# El compose inyecta PORT (4001/5001/6001 segun ambiente) y HOSTNAME=0.0.0.0;
# `next start` los respeta, asi que el contenedor escucha en ese puerto interno.

FROM node:22-alpine

# pnpm via corepack
ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN corepack enable

WORKDIR /app

# Manifiestos primero para aprovechar la cache de capas en la instalacion.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY frontend/package.json ./frontend/

# Instala TODAS las deps (incluidas dev: typescript, tailwind, eslint) — necesarias
# para `next build`. No fijamos NODE_ENV=production aqui para no omitir devDeps.
RUN --mount=type=cache,id=pnpm,target=/pnpm/store pnpm install --frozen-lockfile

# Codigo y build de produccion (.next compilado, baked en la imagen).
COPY . .
RUN pnpm --filter @flit/frontend build

ENV NODE_ENV=production
EXPOSE 4001

# next start sirve el build de produccion (sin HMR). Lee PORT/HOSTNAME del entorno.
CMD ["pnpm", "--filter", "@flit/frontend", "start"]
