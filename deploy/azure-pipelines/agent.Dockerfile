FROM alpine:3.21

ARG AGENT_VERSION=4.266.2
ARG AGENT_PACKAGE_URL=
ARG DOTNET_SDK_VERSION=

# Install base dependencies
RUN apk add --no-cache \
    bash \
    ca-certificates \
    curl \
    git \
    jq \
    make \
    g++ \
    python3 \
    su-exec \
    icu-libs \
    libstdc++ \
    openssl \
    krb5-libs \
    zlib \
    libgcc \
    libintl

# Install Node.js 20 (required for npm in pipelines)
RUN apk add --no-cache nodejs npm

# Install Azure CLI (slim, no dependencies)
RUN apk add --no-cache py3-pip \
    && pip3 install --no-cache-dir --break-system-packages azure-cli \
    && az --version

# Install PowerShell
RUN apk add --no-cache \
    libgdiplus \
    --repository=https://dl-cdn.alpinelinux.org/alpine/edge/testing \
    && curl -L https://github.com/PowerShell/PowerShell/releases/download/v7.4.6/powershell-7.4.6-linux-musl-x64.tar.gz -o /tmp/powershell.tar.gz \
    && mkdir -p /opt/microsoft/powershell/7 \
    && tar zxf /tmp/powershell.tar.gz -C /opt/microsoft/powershell/7 \
    && chmod +x /opt/microsoft/powershell/7/pwsh \
    && ln -s /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh \
    && rm /tmp/powershell.tar.gz

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
RUN adduser -D -h /azp azp
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
RUN apk add --no-cache tini
ENTRYPOINT ["/sbin/tini", "--", "/azp/start.sh"]
