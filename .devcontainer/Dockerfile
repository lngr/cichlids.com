FROM mcr.microsoft.com/devcontainers/base:noble

# Install base packages for dev container
RUN apt-get update && \
    export DEBIAN_FRONTEND=noninteractive && \
    apt-get install -y \
        screen \
        socat \
        telnet \
        netcat-openbsd \
        xauth \
        xclip \
        xsel \
        xdg-utils \
        tzdata \
        curl \
        git \
        jq \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

ARG HOST_USER=
ARG HOST_UID=
ARG HOST_GID=
ARG HOST_HOME=

# Standard-Nicht-Root-User des Basis-Images (Konvention: UID 1000) auf den
# Host-User abbilden, damit gemountete Host-Configs ihre absoluten Pfade im
# Container nativ auflösen. Ohne gesetzte HOST_*-Args bleibt das Image unverändert.
RUN set -eux; \
    if [ -n "$HOST_USER" ]; then \
      old_user="$(getent passwd 1000 | cut -d: -f1 || true)"; \
      if [ -z "$old_user" ]; then \
        groupadd -g "$HOST_GID" "$HOST_USER" 2>/dev/null || true; \
        useradd -m -u "$HOST_UID" -g "$HOST_GID" -s /bin/bash -d "$HOST_HOME" "$HOST_USER"; \
      else \
        old_home="$(getent passwd "$old_user" | cut -d: -f6)"; \
        if [ "$HOST_GID" != "$(id -g "$old_user")" ]; then groupmod -g "$HOST_GID" "$(id -gn "$old_user")"; fi; \
        if [ "$HOST_UID" != "$(id -u "$old_user")" ]; then usermod -u "$HOST_UID" "$old_user"; fi; \
        if [ "$HOST_USER" != "$old_user" ]; then \
          usermod -l "$HOST_USER" "$old_user"; \
          groupmod -n "$HOST_USER" "$old_user" 2>/dev/null || true; \
          if [ -f "/etc/sudoers.d/$old_user" ]; then \
            mv "/etc/sudoers.d/$old_user" "/etc/sudoers.d/$HOST_USER"; \
            sed -i "s/\\b$old_user\\b/$HOST_USER/g" "/etc/sudoers.d/$HOST_USER"; \
          fi; \
        fi; \
        if [ "$HOST_HOME" != "$old_home" ]; then \
          mkdir -p "$(dirname "$HOST_HOME")"; \
          usermod -d "$HOST_HOME" -m "$HOST_USER"; \
        fi; \
      fi; \
      chown -R "$HOST_UID:$HOST_GID" "$HOST_HOME"; \
    fi
