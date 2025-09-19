# Multi-stage Dockerfile for Lovecraft.Blazor (Blazor Server)

ARG ACCESS_CODE=""
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# Ensure curl is available for the docker healthcheck. The aspnet base image is Debian-based.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 5002

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Lovecraft.Blazor/Lovecraft.Blazor.csproj", "Lovecraft.Blazor/"]
RUN dotnet restore "./Lovecraft.Blazor/Lovecraft.Blazor.csproj"
COPY . .
WORKDIR "/src/Lovecraft.Blazor"
RUN dotnet build "Lovecraft.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Lovecraft.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Expose the access code as an environment variable inside the container
ENV ACCESS_CODE=${ACCESS_CODE}
ENTRYPOINT ["dotnet", "Lovecraft.Blazor.dll"]
