using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.AccesoDatos.Repositories.Implementaciones;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.LogicaNegocio.Implementations;
using Biozin_Royale_Backend.API.Auth;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SupabaseConnection")));

builder.Services.AddMemoryCache();
builder.Services.AddScoped<IUnitWork, UnitWorkEF>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IStaffLN, StaffLN>();
builder.Services.AddScoped<IAuthLN, AuthLN>();
builder.Services.AddScoped<IProfileLN, ProfileLN>();
builder.Services.AddScoped<IGamesHistoryLN, GamesHistoryLN>();
builder.Services.AddScoped<IWalletLN, WalletLN>();
builder.Services.AddScoped<IBetsLN, BetsLN>();
builder.Services.AddScoped<IPromotionLN, PromotionLN>();
builder.Services.AddScoped<IReportesLN, ReportesLN>();
builder.Services.AddScoped<IDepositosLN, DepositosLN>();
builder.Services.AddScoped<IMetodosPagoLN, MetodosPagoLN>();
builder.Services.AddScoped<IRetirosLN, RetirosLN>();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("OddsApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OddsApi:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<ISportsLN, SportsLN>();
builder.Services.AddHostedService<Biozin_Royale_Backend.API.BetSettlementService>();

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
            RoleClaimType = "role",
            IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                securityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt && jwt.Issuer == supabaseIssuer
                    ? supabaseJwks.GetSigningKeys()
                    : new SecurityKey[] { localSigningKey }
        };

        // Revisa si el token corresponde a una sesión cerrada desde "Sesiones activas".
        // Los tokens propios (login manual/2FA) se identifican por "jti"; los de Supabase
        // (Google) no traen ese jti pero sí un "session_id" estable entre refrescos — ver
        // AuthLN.SincronizarOAuthAsync. Sin fila en `sessions` para ninguno de los dos
        // (tokens de staff, o emitidos antes de esta feature) se deja pasar sin más — solo
        // bloquea cuando SÍ hay fila y está marcada como inactiva. Cacheado 30s por id para
        // no pegarle a la base de datos en cada request autenticado de la app.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var candidatos = new[] { "jti", "session_id" }
                    .Select(claim => context.Principal?.FindFirst(claim)?.Value)
                    .Select(valor => Guid.TryParse(valor, out var id) ? id : (Guid?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct();

                var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                ApplicationDbContext? db = null;

                foreach (var id in candidatos)
                {
                    if (cache.TryGetValue(id, out bool revocada))
                    {
                        if (revocada) { context.Fail("Sesión cerrada."); return; }
                        continue;
                    }

                    db ??= context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var sesion = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
                    if (sesion is null) continue;

                    cache.Set(id, !sesion.IsActive, TimeSpan.FromSeconds(30));
                    if (!sesion.IsActive) { context.Fail("Sesión cerrada."); return; }
                }
            }
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
