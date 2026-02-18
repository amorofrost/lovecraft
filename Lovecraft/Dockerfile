# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["Lovecraft.Backend/Lovecraft.Backend.csproj", "Lovecraft.Backend/"]
COPY ["Lovecraft.Common/Lovecraft.Common.csproj", "Lovecraft.Common/"]
RUN dotnet restore "Lovecraft.Backend/Lovecraft.Backend.csproj"

# Copy all source files and build
COPY . .
WORKDIR "/src/Lovecraft.Backend"
RUN dotnet build "Lovecraft.Backend.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Lovecraft.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published files
COPY --from=publish /app/publish .

# Change ownership
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Development

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

# Start the application
ENTRYPOINT ["dotnet", "Lovecraft.Backend.dll"]
