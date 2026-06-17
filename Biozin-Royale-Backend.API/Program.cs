using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.AccesoDatos.UnitOfWork;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SupabaseConnection")));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
