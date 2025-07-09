using OrderService.Data;
using Microsoft.EntityFrameworkCore;
using OrderService.Consumers;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Conexão com PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Registra a ConnectionFactory do RabbitMQ para ser usada pela Controller
builder.Services.AddSingleton(sp => new ConnectionFactory()
{
    HostName = "rabbitmq",
    DispatchConsumersAsync = true
});

// Serviços da API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer(); // Swagger
builder.Services.AddSwaggerGen();          // Swagger

// RabbitMQ
builder.Services.AddHostedService<RabbitMqOrderConsumer>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        // Loga a tentativa de aplicar migrações
        app.Logger.LogInformation("Attempting to apply database migrations...");
        dbContext.Database.Migrate(); // Este método aplica todas as migrações pendentes
        app.Logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        // Loga qualquer erro que ocorra durante a aplicação das migrações
        app.Logger.LogError(ex, "An error occurred while applying database migrations.");
        // Em um ambiente de produção crítico, você pode querer re-lançar a exceção
        // ou encerrar a aplicação se o banco de dados for essencial e não puder ser configurado.
        // throw;
    }
}

// Pipeline da aplicação
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy!"));

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
