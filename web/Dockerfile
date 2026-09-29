FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# requires next.config.js: { output: 'standalone' }
RUN npm run build

FROM node:22-alpine AS runtime
RUN addgroup -g 10001 appgroup && adduser -D -u 10001 -G appgroup appuser
WORKDIR /app
COPY --from=build --chown=appuser:appgroup /app/.next/standalone ./
COPY --from=build --chown=appuser:appgroup /app/.next/static ./.next/static
COPY --from=build --chown=appuser:appgroup /app/public ./public
USER 10001
EXPOSE 3000
ENV PORT=3000
CMD ["node", "server.js"]
