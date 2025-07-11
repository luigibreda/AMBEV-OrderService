# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Install dotnet-ef as a global tool
RUN dotnet tool install --tool-path /usr/local/bin dotnet-ef --version 8.0.0

# Copy solution and project files
COPY ["OrderService.sln", "./"]
COPY ["src/OrderService.Domain/OrderService.Domain.csproj", "src/OrderService.Domain/"]
COPY ["src/OrderService.Application/OrderService.Application.csproj", "src/OrderService.Application/"]
COPY ["src/OrderService.Infrastructure/OrderService.Infrastructure.csproj", "src/OrderService.Infrastructure/"]
COPY ["src/OrderService.WebApi/OrderService.WebApi.csproj", "src/OrderService.WebApi/"]

# Copy the rest of the source code
COPY . .

# Restore and build only the required projects
RUN dotnet restore "src/OrderService.WebApi/OrderService.WebApi.csproj"
RUN dotnet build "src/OrderService.WebApi/OrderService.WebApi.csproj" -c Release --no-restore

# Publish the WebApi project
RUN dotnet publish "src/OrderService.WebApi/OrderService.WebApi.csproj" -c Release -o /app/publish --no-restore

# Runtime stage - Using SDK to have access to dotnet-ef
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime
WORKDIR /app

# Install PostgreSQL client
RUN apt-get update && apt-get install -y --no-install-recommends \
    postgresql-client \
    && rm -rf /var/lib/apt/lists/*

# Install dotnet-ef as a global tool
RUN dotnet tool install --tool-path /usr/local/bin dotnet-ef --version 8.0.0

# Add .NET tools to PATH
ENV PATH="$PATH:/root/.dotnet/tools:/usr/local/bin"

# Copy published files
COPY --from=build /app/publish .

# Copy project files for migrations
COPY --from=build /src/src/OrderService.Infrastructure/ ./OrderService.Infrastructure/
COPY --from=build /src/src/OrderService.Domain/ ./OrderService.Domain/

# Copy entrypoint script and make it executable
COPY entrypoint.sh .
RUN chmod +x /app/entrypoint.sh

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_HTTP_PORTS=80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV TZ=America/Sao_Paulo
ENV LC_ALL=pt_BR.UTF-8
ENV LANG=pt_BR.UTF-8
ENV LANGUAGE=pt_BR:pt:en
ENV SWAGGER_ENABLED=true

# Make sure the entrypoint script has Unix line endings
RUN sed -i 's/\r$//' /app/entrypoint.sh

# Expose port 80
EXPOSE 80

# Set the entry point
ENTRYPOINT ["/app/entrypoint.sh"]
