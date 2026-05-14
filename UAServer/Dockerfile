#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Default to Debug so Visual Studio's Container Tools (F5 / "Docker" launch
# profile) get a debuggable image without any extra configuration. CI overrides
# this to Release via --build-arg in .github/workflows/docker-publish.yml.
ARG BUILD_CONFIGURATION=Debug

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 4840
EXPOSE 5000
EXPOSE 5001
EXPOSE 19520
EXPOSE 19521

# The base image already ships with a non-root `app` user at UID/GID 1000
# Running the server as root would let a compromised protocol driver (loaded as a plain
# .NET assembly) trivially escalate inside the container, especially when
# operators add --privileged for serial / Matter use cases.
RUN mkdir -p /app/pki /app/logs /app/settings /app/nodesets /app/drivers \
 && chown -R app:app /app

# TCP probe against the OPC UA listener so Docker / Kubernetes can tell the
# difference between "process running" and "OPC UA endpoint accepting
# connections". Uses a tiny bash one-liner so we don't have to install curl
# or netcat in the runtime image.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/4840' || exit 1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION
WORKDIR /src
COPY ["UAEdgeTranslator.csproj", "."]
RUN dotnet restore "./UAEdgeTranslator.csproj" /p:PlatformTarget=AnyCPU
COPY . .
WORKDIR "/src/."
ARG TARGET_FRAMEWORK=net10.0
RUN dotnet build "UAEdgeTranslator.csproj" -c $BUILD_CONFIGURATION -o /app/build -f $TARGET_FRAMEWORK /p:PlatformTarget=AnyCPU

FROM build AS publish
ARG BUILD_CONFIGURATION
ARG TARGET_FRAMEWORK=net10.0
RUN dotnet publish "UAEdgeTranslator.csproj" --no-restore -c $BUILD_CONFIGURATION -o /app/publish -f $TARGET_FRAMEWORK /p:UseAppHost=false /p:PlatformTarget=AnyCPU

FROM base AS final
WORKDIR /app
COPY --from=publish --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "UAEdgeTranslator.dll"]