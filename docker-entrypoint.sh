#!/bin/bash

# Exporta o PATH para incluir as ferramentas do .NET
export PATH="/root/.dotnet/tools:$PATH"

# Função para verificar se o PostgreSQL está pronto
wait_for_postgres() {
    echo "Aguardando o PostgreSQL iniciar..."
    until PGPASSWORD=admin psql -h "db" -U "admin" -d "ambev_orders" -c "\q"; do
        >&2 echo "PostgreSQL não está disponível - esperando..."
        sleep 1
    done
    echo "PostgreSQL está pronto!"
}

# Aplica as migrações
apply_migrations() {
    echo "Aplicando migrações do banco de dados..."
    
    # Navega para o diretório da aplicação
    cd /app
    
    # Usa o dotnet ef para aplicar as migrações
    dotnet ef database update \
        --project /app/OrderService.Infrastructure \
        --startup-project /app \
        --configuration Release \
        --no-build
    
    echo "Migrações aplicadas com sucesso!"
}

# Executa as etapas
wait_for_postgres
apply_migrations

# Inicia a aplicação
echo "Iniciando a aplicação..."
exec dotnet OrderService.WebApi.dll
