#!/bin/bash
set -e

# Export .NET tools to PATH
export PATH="$PATH:/root/.dotnet/tools"

echo "=== OrderService Container Initialization ==="

# Function to check if PostgreSQL is ready
wait_for_postgres() {
    echo "Waiting for PostgreSQL to start..."
    until PGPASSWORD=admin psql -h "db" -U "admin" -d "ambev_orders" -c '\q' > /dev/null 2>&1; do
        echo "PostgreSQL is not available - waiting..."
        sleep 1
    done
    echo "PostgreSQL is ready!"
}

# Apply database migrations
apply_migrations() {
    echo "Applying database migrations..."
    cd /app
    
    # Check if the required files exist
    if [ ! -f "OrderService.WebApi.dll" ]; then
        echo "Error: OrderService.WebApi.dll not found in /app directory"
        ls -la /app
        exit 1
    fi
    
    echo "Running migrations using the published assembly..."
    dotnet OrderService.WebApi.dll --migrate
    
    if [ $? -eq 0 ]; then
        echo "Migrations applied successfully!"
    else
        echo "Failed to apply migrations!"
        exit 1
    fi
}

# Start the application
start_application() {
    echo "Starting OrderService..."
    dotnet OrderService.WebApi.dll
}

# Execute the steps
wait_for_postgres
apply_migrations
start_application
