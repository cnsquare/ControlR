# EnkrOma server image.
#
# Self-contained, deployment-oriented Dockerfile. Unlike the repo's
# ControlR.Web.Server/Dockerfile (which lists each project file individually for layer caching and
# has drifted out of sync with the project graph), this copies the whole build context before
# restoring, so it keeps working as projects are added or removed.
#
# Build from the REPO ROOT after applying branding:
#   docker build -f deploy/enkroma/Dockerfile --build-arg CURRENT_VERSION=1.0.0 -t enkroma/controlr:1.0.0 .
#
# .dockerignore at the repo root already excludes bin/, obj/, node_modules/, and .git.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt update && apt -y install curl
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "./ControlR.Web.Server/ControlR.Web.Server.csproj"
WORKDIR "/src/ControlR.Web.Server"
RUN dotnet build "./ControlR.Web.Server.csproj" -c "$BUILD_CONFIGURATION" -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG CURRENT_VERSION
RUN dotnet publish "./ControlR.Web.Server.csproj" -c "$BUILD_CONFIGURATION" \
    -p:Version=$CURRENT_VERSION -p:FileVersion=$CURRENT_VERSION \
    -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ControlR.Web.Server.dll"]

HEALTHCHECK CMD curl -f http://localhost:8080/health || exit 1
