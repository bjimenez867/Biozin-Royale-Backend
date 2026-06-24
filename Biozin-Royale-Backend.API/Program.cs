using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.AccesoDatos.Repositories.Implementaciones;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.LogicaNegocio.Implementations;
using Biozin_Royale_Backend.API.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SupabaseConnection")));

builder.Services.AddScoped<IUnitWork, UnitWorkEF>();
builder.Services.AddScoped<IAuthLN, AuthLN>();
builder.Services.AddScoped<IProfileLN, ProfileLN>();
builder.Services.AddScoped<IGamesHistoryLN, GamesHistoryLN>();
builder.Services.AddScoped<IWalletLN, WalletLN>();
builder.Services.AddScoped<IBetsLN, BetsLN>();

builder.Services.AddHttpClient("OddsApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OddsApi:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<ISportsLN, SportsLN>();

var supabaseUrl = builder.Configuration["Supabase:Url"]!;
var supabaseIssuer = $"{supabaseUrl}/auth/v1";
var localIssuer = builder.Configuration["Jwt:LocalIssuer"]!;
var localSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:LocalSigningKey"]!));
var supabaseJwks = new SupabaseJwksProvider(supabaseUrl);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { supabaseIssuer, localIssuer },
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                securityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt && jwt.Issuer == supabaseIssuer
                    ? supabaseJwks.GetSigningKeys()
                    : new SecurityKey[] { localSigningKey }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins("http://localhost:4200", "http://localhost:8100")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

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

app.UseCors("Frontend");

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
