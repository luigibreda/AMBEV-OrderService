# Estágio de build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Instalação do dotnet-ef
RUN dotnet tool install --tool-path /usr/local/bin dotnet-ef --version 8.0.0

# Cópia dos arquivos do projeto
COPY ["OrderService.sln", "./"]
COPY ["src/OrderService.Domain/OrderService.Domain.csproj", "src/OrderService.Domain/"]
COPY ["src/OrderService.Application/OrderService.Application.csproj", "src/OrderService.Application/"]
COPY ["src/OrderService.Infrastructure/OrderService.Infrastructure.csproj", "src/OrderService.Infrastructure/"]
COPY ["src/OrderService.WebApi/OrderService.WebApi.csproj", "src/OrderService.WebApi/"]

# Cópia do restante do código
COPY . .

# Restauração e build
RUN dotnet restore "src/OrderService.WebApi/OrderService.WebApi.csproj"
RUN dotnet build "src/OrderService.WebApi/OrderService.WebApi.csproj" -c Release --no-restore

# Publicação da aplicação
RUN dotnet publish "src/OrderService.WebApi/OrderService.WebApi.csproj" -c Release -o /app/publish --no-restore

# Estágio de execução
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime
WORKDIR /app

# Instalação do cliente PostgreSQL
RUN apt-get update && apt-get install -y --no-install-recommends \
    postgresql-client \
    && rm -rf /var/lib/apt/lists/*

# Instalação do dotnet-ef
RUN dotnet tool install --tool-path /usr/local/bin dotnet-ef --version 8.0.0
ENV PATH="$PATH:/root/.dotnet/tools:/usr/local/bin"

# Cópia dos arquivos publicados e dependências
COPY --from=build /app/publish .
COPY --from=build /src/src/OrderService.Infrastructure/ ./OrderService.Infrastructure/
COPY --from=build /src/src/OrderService.Domain/ ./OrderService.Domain/

# Configuração do entrypoint
COPY entrypoint.sh .
RUN chmod +x /app/entrypoint.sh

# Variáveis de ambiente
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_HTTP_PORTS=80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV TZ=America/Sao_Paulo
ENV LC_ALL=pt_BR.UTF-8
ENV LANG=pt_BR.UTF-8
ENV LANGUAGE=pt_BR:pt:en
ENV SWAGGER_ENABLED=true

# Garante que o entrypoint tenha terminações de linha Unix
RUN sed -i 's/\r$//' /app/entrypoint.sh

EXPOSE 80
ENTRYPOINT ["/app/entrypoint.sh"]
