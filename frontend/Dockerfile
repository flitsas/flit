FROM node:22-alpine

# Install pnpm
ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN corepack enable

WORKDIR /app

# Copy package lock and workspace configuration
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY frontend/package.json ./frontend/

# Install dependencies using pnpm cache
RUN --mount=type=cache,id=pnpm,target=/pnpm/store pnpm install

# Copy code
COPY . .

EXPOSE 3000

# Default command for development mode
CMD ["pnpm", "--filter", "@flit/frontend", "dev"]


# FROM node:lts-alpine

# FROM nginx:1.27-alpine AS runtime
# COPY --from=builder /app/frontend/dist /usr/share/nginx/html
# COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
# EXPOSE 80
# HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
#     CMD wget -qO- http://localhost/ || exit 1