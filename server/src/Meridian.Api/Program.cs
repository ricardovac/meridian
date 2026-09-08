using System.Text;
using System.Text.Json.Serialization;
using Meridian.Api.Filters;
using Meridian.Api.Middleware;
using Meridian.Api.Security;
using Meridian.Application.Abstractions;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IdempotencyFilter>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
jwtOptions.EnsureConfiguredFor(builder.Environment.IsDevelopment());

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

const string corsPolicy = "Frontend";
builder.Services.AddCors(options =>
    options.AddPolicy(corsPolicy, policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Meridian API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT returned by /api/auth/login.",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

var app = builder.Build();

DependencyInjection.EnsureDatabase(
    app.Services, app.Configuration, app.Services.GetRequiredService<ILogger<Program>>());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors(corsPolicy);
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", async (MeridianDbContext context, CancellationToken cancellationToken) =>
{
    var databaseUp = await context.Database.CanConnectAsync(cancellationToken);
    var payload = new { status = databaseUp ? "healthy" : "degraded", database = databaseUp ? "connected" : "unavailable" };
    return databaseUp
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

public partial class Program;
