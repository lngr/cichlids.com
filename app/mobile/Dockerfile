FROM node:24-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1 AS build
WORKDIR /source
RUN npm install --global pnpm@12.4.2
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY client-core ./client-core
COPY mobile ./mobile
RUN pnpm install --frozen-lockfile && pnpm --filter @cichlids/client-core build
ARG APP_VARIANT=staging
ARG EXPO_PUBLIC_API_URL=https://staging.cichlids.com
ARG EXPO_PUBLIC_KEYCLOAK_URL=https://auth.staging.cichlids.com
ENV APP_VARIANT=$APP_VARIANT EXPO_PUBLIC_API_URL=$EXPO_PUBLIC_API_URL \
    EXPO_PUBLIC_KEYCLOAK_URL=$EXPO_PUBLIC_KEYCLOAK_URL EXPO_NO_TELEMETRY=1
RUN cd mobile && pnpm exec expo export --platform web --output-dir /out

FROM busybox:1.37.0-musl@sha256:5cec3fc171c87218698e85a52af7087de727372aae264a787b8112901a5b0092
COPY --from=build /out /www
RUN printf 'E404:/www/index.html\n' > /etc/httpd.conf
USER 65534:65534
EXPOSE 8080
ENTRYPOINT ["/bin/httpd", "-f", "-p", "8080", "-h", "/www", "-c", "/etc/httpd.conf"]
