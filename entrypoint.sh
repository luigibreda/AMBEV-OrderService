#!/bin/bash
set -e

# Export .NET tools to PATH
export PATH="$PATH:/root/.dotnet/tools"

echo "=== OrderService Container Initialization ==="

# Aguarda o PostgreSQL ficar disponível
aguardar_postgres() {
    echo "Aguardando PostgreSQL iniciar..."
    until PGPASSWORD=$POSTGRES_PASSWORD pg_isready -h "db" -U "$POSTGRES_USER" -d "$POSTGRES_DB" > /dev/null 2>&1; do
        echo "PostgreSQL não está pronto, aguardando..."
        sleep 2
    done
    echo "PostgreSQL está pronto!"
}

# Aplica as migrações do banco de dados
aplicar_migracoes() {
    echo "Verificando migrações pendentes..."
    cd /app

    if [ ! -f "OrderService.WebApi.dll" ]; then
        echo "Erro: Arquivo OrderService.WebApi.dll não encontrado"
        ls -la /app
        exit 1
    fi

    echo "Aplicando migrações com tentativas..."
    for i in {1..5}; do
        dotnet OrderService.WebApi.dll --migrate && break
        echo "Falha ao aplicar migrações (tentativa $i). Tentando novamente em 5 segundos..."
        sleep 5
    done

    # Verifica se a migração ainda falha após todas as tentativas
    if ! dotnet OrderService.WebApi.dll --migrate > /dev/null 2>&1; then
        echo "ERRO: Falha ao aplicar migrações após várias tentativas."
        exit 1
    fi

    echo "Migrações aplicadas com sucesso!"
}

# Inicia a aplicação
iniciar_aplicacao() {
    echo "Iniciando a aplicação..."
    dotnet OrderService.WebApi.dll
}

# Executa os passos em sequência
aguardar_postgres
aplicar_migracoes
iniciar_aplicacao
