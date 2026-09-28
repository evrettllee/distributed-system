using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Data;
using Server.Handlers;
using Server.Network;
using Server.Services;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("Ошибка: строка подключения не найдена в appsettings.json");
    return;
}

var optionsBuilder = new DbContextOptionsBuilder<EducationDbContext>();
optionsBuilder.UseSqlServer(connectionString);

EducationDbContext context;
try
{
    context = new EducationDbContext(optionsBuilder.Options);

    if (!context.Database.CanConnect())
    {
        Console.WriteLine("Ошибка: не удалось подключиться к базе данных");
        return;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка подключения к БД: {ex.Message}");
    return;
}

var service = new EducationService(context);
var handler = new OperationHandler(service);
var server = new TcpServer(handler);

server.Start();