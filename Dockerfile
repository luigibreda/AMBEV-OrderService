# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia os arquivos de projeto necessários
COPY ["OrderService.sln", "."]
COPY ["src/OrderService.Domain/OrderService.Domain.csproj", "src/OrderService.Domain/"]
COPY ["src/OrderService.Application/OrderService.Application.csproj", "src/OrderService.Application/"]
COPY ["src/OrderService.Infrastructure/OrderService.Infrastructure.csproj", "src/OrderService.Infrastructure/"]
COPY ["src/OrderService.WebApi/OrderService.WebApi.csproj", "src/OrderService.WebApi/"]

# Restaura apenas os projetos necessários (ignora os testes)
RUN dotnet restore "src/OrderService.WebApi/OrderService.WebApi.csproj"

# Copia o resto dos arquivos
COPY . .

# Publica a aplicação
WORKDIR "/src/src/OrderService.WebApi"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final
WORKDIR /app

# Define variáveis de ambiente
ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:80

# Expõe as portas
EXPOSE 80

# Copia os arquivos publicados
COPY --from=build /app/publish .

# Copia os arquivos de projeto para o runtime (necessário para migrações)
COPY --from=build /src/src/OrderService.Infrastructure/ ./OrderService.Infrastructure/
COPY --from=build /src/src/OrderService.Domain/ ./OrderService.Domain/
COPY --from=build /src/src/OrderService.WebApi/ ./

# Cria um script para aplicar migrações e iniciar a aplicação
RUN echo '#!/bin/bash\n\nset -e\n\n# Aplica migrações\necho "Aplicando migrações do banco de dados..."\ndotnet ef database update \
  --project /app/OrderService.Infrastructure \
  --startup-project /app \
  --no-build \
  --configuration Release \
  --framework net8.0\necho "Migrações aplicadas com sucesso."\n\n# Inicia a aplicação\necho "Iniciando a aplicação..."\nexec dotnet OrderService.WebApi.dll' > /app/entrypoint.sh && \
    chmod +x /app/entrypoint.sh

# Instala as ferramentas do EF Core
RUN dotnet tool install --global dotnet-ef --version 9.0.1
ENV PATH="$PATH:/root/.dotnet/tools"

# Ponto de entrada
ENTRYPOINT ["/app/entrypoint.sh"]
