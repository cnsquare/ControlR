# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# The build SDK must satisfy the repo's global.json, which pins an exact SDK with
# "rollForward": "disable". The floating :10.0 tag rolls forward to a newer patch (e.g. 10.0.404),
# which that pin rejects — `dotnet restore` then fails with "The application 'restore' does not exist".
# Keep DOTNET_SDK_VERSION in sync with global.json's sdk.version (override with --build-arg if needed).
ARG DOTNET_SDK_VERSION=10.0.403

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt update
RUN apt -y install curl
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy the whole context before restoring. Listing project files individually for layer caching drifts
# out of sync as projects are added/removed/renamed (it referenced a long-gone ControlR.Libraries.Clients),
# which breaks restore. Correctness over cache here.
COPY . .
RUN dotnet restore "./ControlR.Web.Server/ControlR.Web.Server.csproj"
WORKDIR "/src/ControlR.Web.Server"
RUN dotnet build "./ControlR.Web.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG CURRENT_VERSION
RUN dotnet publish "./ControlR.Web.Server.csproj" -c $BUILD_CONFIGURATION -p:Version=$CURRENT_VERSION -p:FileVersion=$CURRENT_VERSION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ControlR.Web.Server.dll"]

HEALTHCHECK \
  CMD curl -f http://localhost:8080/health || exit 1