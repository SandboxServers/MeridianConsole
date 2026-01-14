FROM ubuntu:22.04

ARG AGENT_VERSION=4.266.2
ARG AGENT_PACKAGE_URL=
ARG DOTNET_SDK_VERSION=

ENV DEBIAN_FRONTEND=noninteractive

# Install base dependencies
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        bash \
        ca-certificates \
        curl \
        git \
        jq \
        make \
        g++ \
        python3 \
        python3-pip \
        unzip \
        gnupg \
        lsb-release \
        libicu70 \
        libssl3 \
        libkrb5-3 \
        zlib1g \
        libstdc++6 \
        libc6 \
        gosu \
        tini \
    && rm -rf /var/lib/apt/lists/*

# Microsoft package sources (PowerShell + Azure CLI)
RUN curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
        | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/ubuntu/22.04/prod jammy main" \
        > /etc/apt/sources.list.d/microsoft-prod.list \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/azure-cli/ jammy main" \
        > /etc/apt/sources.list.d/azure-cli.list

# Install Azure CLI and PowerShell
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        azure-cli \
        powershell \
    && rm -rf /var/lib/apt/lists/*

# Install Node.js 20 (required for npm in pipelines)
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get update \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install .NET SDK
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="${PATH}:${DOTNET_ROOT}"

COPY global.json /tmp/global.json

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && if [ -n "${DOTNET_SDK_VERSION}" ]; then \
         sdk_version="${DOTNET_SDK_VERSION}"; \
       else \
         sdk_version="$(jq -r '.sdk.version' /tmp/global.json)"; \
       fi \
    && /tmp/dotnet-install.sh --version "${sdk_version}" --install-dir "${DOTNET_ROOT}" \
    && rm /tmp/dotnet-install.sh /tmp/global.json

# Create azp user
RUN useradd --create-home --home-dir /azp --shell /bin/bash azp
WORKDIR /azp

# Install Azure DevOps agent
RUN if [ -n "${AGENT_PACKAGE_URL}" ]; then \
      agent_url="${AGENT_PACKAGE_URL}"; \
    else \
      agent_url="https://download.agent.dev.azure.com/agent/${AGENT_VERSION}/vsts-agent-linux-x64-${AGENT_VERSION}.tar.gz"; \
    fi \
    && curl -fL --retry 5 --retry-connrefused --retry-delay 2 "${agent_url}" -o /tmp/agent.tgz \
    && tar -xzf /tmp/agent.tgz -C /azp \
    && rm /tmp/agent.tgz \
    && chown -R azp:azp /azp

COPY deploy/azure-pipelines/start.sh /azp/start.sh
RUN chmod +x /azp/start.sh

# Use tini for proper signal handling
ENTRYPOINT ["/usr/bin/tini", "--", "/azp/start.sh"]
