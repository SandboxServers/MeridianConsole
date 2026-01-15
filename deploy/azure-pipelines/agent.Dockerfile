FROM ubuntu:24.04

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
        libicu74 \
        libssl3t64 \
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
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/ubuntu/24.04/prod noble main" \
        > /etc/apt/sources.list.d/microsoft-prod.list \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/azure-cli/ noble main" \
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

# Install Docker CLI (for building container images via mounted socket)
RUN curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
        | gpg --dearmor -o /usr/share/keyrings/docker.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu jammy stable" \
        > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        docker-ce-cli \
        docker-buildx-plugin \
        docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/*

# Install Java 17 (for OWASP Dependency-Check)
RUN apt-get update \
    && apt-get install -y --no-install-recommends openjdk-17-jdk \
    && rm -rf /var/lib/apt/lists/*

ENV JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64

# Install security scanning tools
# Semgrep (SAST)
RUN pip3 install --no-cache-dir semgrep checkov --break-system-packages

# OWASP Dependency-Check (SCA)
RUN curl -fsSL https://github.com/jeremylong/DependencyCheck/releases/download/v11.1.0/dependency-check-11.1.0-release.zip -o /tmp/dep-check.zip \
    && unzip /tmp/dep-check.zip -d /opt \
    && rm /tmp/dep-check.zip \
    && chmod +x /opt/dependency-check/bin/dependency-check.sh \
    && ln -s /opt/dependency-check/bin/dependency-check.sh /usr/local/bin/dependency-check

# Trivy (Container scanning)
RUN curl -fsSL https://github.com/aquasecurity/trivy/releases/download/v0.58.1/trivy_0.58.1_Linux-64bit.tar.gz -o /tmp/trivy.tar.gz \
    && tar -xzf /tmp/trivy.tar.gz -C /usr/local/bin trivy \
    && rm /tmp/trivy.tar.gz \
    && chmod +x /usr/local/bin/trivy

# GitLeaks (Secrets scanning)
RUN curl -fsSL https://github.com/gitleaks/gitleaks/releases/download/v8.21.2/gitleaks_8.21.2_linux_x64.tar.gz -o /tmp/gitleaks.tar.gz \
    && tar -xzf /tmp/gitleaks.tar.gz -C /usr/local/bin gitleaks \
    && rm /tmp/gitleaks.tar.gz \
    && chmod +x /usr/local/bin/gitleaks

# Syft (SBOM generation)
RUN curl -fsSL https://github.com/anchore/syft/releases/download/v1.18.1/syft_1.18.1_linux_amd64.tar.gz -o /tmp/syft.tar.gz \
    && tar -xzf /tmp/syft.tar.gz -C /usr/local/bin syft \
    && rm /tmp/syft.tar.gz \
    && chmod +x /usr/local/bin/syft

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

# Create azp user and add to docker group for socket access
# Note: The docker group GID may vary on the host; start.sh handles this dynamically
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
