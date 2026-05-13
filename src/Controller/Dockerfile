# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY VMJobOrchestrator.csproj .
RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet publish -c Release -o /app/publish \
    --runtime linux-x64 \
    --self-contained false \
    /p:PublishSingleFile=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Install PowerShell Core and WinRM dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        wget \
        ca-certificates \
        libssl-dev \
        libkrb5-3 \
        libgssapi-krb5-2 \
        gss-ntlmssp \
        libicu-dev \
        curl \
        openssh-client \
        python3 \
        python3-pip \
        && \
    wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y powershell && \
    # Install pywinrm for alternative approach if PowerShell remoting fails
    pip3 install pywinrm --break-system-packages && \
    rm -rf /var/lib/apt/lists/* && \
    rm packages-microsoft-prod.deb

# Configure PowerShell for basic auth
RUN pwsh -Command "Set-Item WSMan:\localhost\Client\TrustedHosts -Value '*' -Force" || true

# Create non-root user
RUN useradd -m -u 1000 controller

# Copy published app
COPY --from=build /app/publish .
RUN chown -R controller:controller /app

USER controller

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD pwsh -Command "exit 0"

# Environment variables
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENV POWERSHELL_TELEMETRY_OPTOUT=1

# Labels
LABEL maintainer="TestFramework Team"
LABEL description="VMJob Controller for K8s + VM Orchestration (C# version)"

# Run the controller
ENTRYPOINT ["dotnet", "VMJobOrchestrator.dll"]
