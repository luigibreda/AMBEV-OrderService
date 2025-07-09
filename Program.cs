using OrderService.Data;
using Microsoft.EntityFrameworkCore;
using OrderService.Consumers;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Conexão com PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql("Host=localhost;Port=5432;Database=netrin_safepartner;Username=devadmin;Password=adm123456"));

// Registra a ConnectionFactory do RabbitMQ para ser usada pela Controller
builder.Services.AddSingleton(sp => new ConnectionFactory()
{
    HostName = "localhost",
    DispatchConsumersAsync = true
});

// Serviços da API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer(); // Swagger
builder.Services.AddSwaggerGen();          // Swagger

// RabbitMQ
builder.Services.AddHostedService<RabbitMqOrderConsumer>();

var app = builder.Build();

// Pipeline da aplicação
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
