FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

# Yarn's Debian repo key can break apt-get update on this base image.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
  /usr/share/keyrings/yarnkey.gpg \
  /etc/apt/trusted.gpg.d/yarn.gpg

ARG ASSETRIPPER_VERSION=2.0.0
# Last 3.11 release that still publishes the Windows embeddable zip. Matches UnityPy's cp311 win_amd64 wheels.
ARG WIN_PYTHON_VERSION=3.11.9

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    PATH="/opt/assetripper:/home/vscode/.dotnet/tools:/home/vscode/.local/bin:${PATH}" \
    STEAM_ROOT=/mnt/steam \
    MT1_ROOT="/mnt/steam/steamapps/common/Monster Train" \
    MT2_ROOT="/mnt/steam/steamapps/common/Monster Train 2" \
    EXTRACT_ROOT=/workspace/.extract \
    WINEARCH=win64 \
    WINEPREFIX=/home/vscode/.wine \
    WINEDEBUG=-all \
    WINEDLLOVERRIDES=mscoree,mshtml= \
    WINPYTHON=/home/vscode/.local/winpython

RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    python3-venv \
    ffmpeg \
    jq \
    unzip \
    xz-utils \
    file \
    ripgrep \
    wine \
    xvfb \
    xauth \
    && rm -rf /var/lib/apt/lists/*

# ilspycmd 9.x targets net8; base image only ships the .NET 9 runtime.
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- \
      --channel 8.0 \
      --runtime dotnet \
      --install-dir /usr/share/dotnet \
    && dotnet --list-runtimes

# AssetRipper: interactive Unity extract UI (Free build is web/GUI; use UnityPy/ilspycmd for CLI).
RUN set -eux; \
    arch="$(uname -m)"; \
    case "$arch" in \
      x86_64) ar_arch=x64 ;; \
      aarch64|arm64) ar_arch=arm64 ;; \
      *) echo "unsupported arch: $arch" >&2; exit 1 ;; \
    esac; \
    curl -fsSL -o /tmp/assetripper.tar.xz \
      "https://github.com/AssetRipper/AssetRipper/releases/download/${ASSETRIPPER_VERSION}/AssetRipper_linux_${ar_arch}.tar.xz"; \
    mkdir -p /opt/assetripper; \
    tar -xJf /tmp/assetripper.tar.xz -C /opt/assetripper; \
    rm /tmp/assetripper.tar.xz; \
    bin="$(find /opt/assetripper -maxdepth 2 -type f \( -name 'AssetRipper' -o -name 'AssetRipper.GUI*' \) ! -name '*.so' | head -n1)"; \
    test -n "$bin"; \
    chmod +x "$bin"; \
    ln -sf "$bin" /usr/local/bin/AssetRipper

USER vscode
WORKDIR /home/vscode

# Mono assembly decompiler (MT1 + current MT2 Steam builds ship Managed/*.dll).
RUN dotnet tool install --global ilspycmd --version 9.1.0.7988

# Python asset tooling (scriptable extracts / music / textures).
RUN python3 -m pip install --user --break-system-packages \
    "UnityPy>=1.10" \
    pillow

# Windows CPython under Wine, used to PyInstaller a win_x64 extractor.
# wine32 is intentionally omitted; this prefix is win64-only.
RUN set -eux; \
    xvfb-run -a wineboot --init; \
    mkdir -p "${WINPYTHON}/Lib/site-packages"; \
    curl -fsSL -o /tmp/python-embed.zip \
      "https://www.python.org/ftp/python/${WIN_PYTHON_VERSION}/python-${WIN_PYTHON_VERSION}-embed-amd64.zip"; \
    unzip -q -o /tmp/python-embed.zip -d "${WINPYTHON}"; \
    rm /tmp/python-embed.zip; \
    sed -i 's/^#import site/import site/' "${WINPYTHON}"/python*._pth; \
    grep -q 'site-packages' "${WINPYTHON}"/python*._pth \
      || echo 'Lib\site-packages' >> "${WINPYTHON}"/python*._pth; \
    curl -fsSL -o /tmp/get-pip.py https://bootstrap.pypa.io/get-pip.py; \
    xvfb-run -a wine "${WINPYTHON}/python.exe" /tmp/get-pip.py --no-warn-script-location; \
    rm /tmp/get-pip.py; \
    xvfb-run -a wine "${WINPYTHON}/python.exe" -m pip install --no-warn-script-location \
      pyinstaller "UnityPy>=1.10"; \
    xvfb-run -a wine "${WINPYTHON}/python.exe" -c "import UnityPy, PyInstaller, fmod_toolkit"

# Cursor Agent CLI (auth via CURSOR_API_KEY or `agent login`).
RUN curl -fsSL https://cursor.com/install | bash

USER root
# Ensure agent is on PATH for all shells even if install landed under ~/.local
RUN if [ -x /home/vscode/.local/bin/agent ]; then \
      ln -sf /home/vscode/.local/bin/agent /usr/local/bin/agent; \
      ln -sf /home/vscode/.local/bin/agent /usr/local/bin/cursor-agent; \
    elif [ -x /home/vscode/.local/bin/cursor-agent ]; then \
      ln -sf /home/vscode/.local/bin/cursor-agent /usr/local/bin/cursor-agent; \
      ln -sf /home/vscode/.local/bin/cursor-agent /usr/local/bin/agent; \
    fi

RUN echo '#!/bin/sh' > /usr/local/bin/wine-python \
    && echo 'exec wine "${WINPYTHON}/python.exe" "$@"' >> /usr/local/bin/wine-python \
    && chmod 755 /usr/local/bin/wine-python

USER vscode
