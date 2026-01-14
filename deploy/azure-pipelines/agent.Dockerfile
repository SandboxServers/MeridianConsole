FROM ubuntu:22.04

ARG DEBIAN_FRONTEND=noninteractive
ARG AGENT_VERSION=4.266.2
ARG AGENT_PACKAGE_URL=
ARG DOTNET_SDK_VERSION=

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        gnupg \
        jq \
        lsb-release \
        make \
        g++ \
        python3 \
        gosu \
        unzip \
        libicu70 \
        libssl3 \
    && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
        | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/ubuntu/22.04/prod jammy main" \
        > /etc/apt/sources.list.d/microsoft-prod.list \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/azure-cli/ jammy main" \
        > /etc/apt/sources.list.d/azure-cli.list \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        nodejs \
        powershell \
        azure-cli \
    && rm -rf /var/lib/apt/lists/*

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

RUN useradd --create-home --home-dir /azp azp
WORKDIR /azp

RUN if [ -n "${AGENT_PACKAGE_URL}" ]; then \
      agent_url="${AGENT_PACKAGE_URL}"; \
    else \
      agent_url="https://download.agent.dev.azure.com/agent/4.266.2/vsts-agent-linux-x64-4.266.2.tar.gz"; \
    fi \
    && curl -fL --retry 5 --retry-connrefused --retry-delay 2 "${agent_url}" -o /tmp/agent.tgz \
    && tar -xzf /tmp/agent.tgz -C /azp \
    && rm /tmp/agent.tgz \
    && ./bin/installdependencies.sh

COPY deploy/azure-pipelines/start.sh /azp/start.sh
RUN chmod +x /azp/start.sh \
    && chown -R azp:azp /azp

ENTRYPOINT ["/azp/start.sh"]
