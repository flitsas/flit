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

# Variables publicas del cliente (las inyecta el CD por ambiente). Deben existir como
# ENV durante `next build` para quedar embebidas en el bundle. Si no se pasan, quedan
# vacias (el front actual no las usa todavia).
ARG NEXT_PUBLIC_API_BASE_URL
ARG NEXT_PUBLIC_TRASPASOS_API_BASE_URL
ARG NEXT_PUBLIC_DEV_USER_ID
ENV NEXT_PUBLIC_API_BASE_URL=$NEXT_PUBLIC_API_BASE_URL
ENV NEXT_PUBLIC_TRASPASOS_API_BASE_URL=$NEXT_PUBLIC_TRASPASOS_API_BASE_URL
ENV NEXT_PUBLIC_DEV_USER_ID=$NEXT_PUBLIC_DEV_USER_ID

# Codigo y build de produccion (.next compilado, baked en la imagen).
COPY . .
RUN pnpm --filter @flit/frontend build

ENV NODE_ENV=production
EXPOSE 4001

RUN chown -R node:node /app
USER node

# next start sirve el build de produccion (sin HMR). Lee PORT/HOSTNAME del entorno.
CMD ["pnpm", "--filter", "@flit/frontend", "start"]
