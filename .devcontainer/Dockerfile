FROM mcr.microsoft.com/devcontainers/base:noble

# Install base packages for dev container
RUN apt-get update && \
    export DEBIAN_FRONTEND=noninteractive && \
    apt-get install -y \
        tmux \
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
