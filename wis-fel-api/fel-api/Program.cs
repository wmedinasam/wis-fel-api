using Scalar.AspNetCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using fel_api.Auth;
using fel_api.Data;
using fel_api.Domain.Identity;
using fel_api.RateLimiting;
using fel_api.Services.Agents;
using fel_api.Services.Auth;
using fel_api.Services.Catalog;
using fel_api.Services.Documents;
using fel_api.Services.Subscriptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("FelApi");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'FelApi' is required. Configure it with User Secrets in Development or deployment configuration.");
}

builder.Services.AddDbContext<FelApiDbContext>(options => options.UseSqlServer(connectionString));

var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("wis-fel-api");
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    // shortcut: key ring en disco sin cifrar; mover a Key Vault o a un certificado cuando el despliegue lo permita.
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "DataProtection:KeyRingPath is required outside Development: without a persisted key ring the stored certifier credentials cannot be decrypted after a restart.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddScoped<JwtTokenIssuer>();
builder.Services.AddScoped<PlatformAuthenticationService>();
builder.Services.AddScoped<PlatformAdministratorBootstrapper>();
builder.Services.AddScoped<ClientProfileService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<AgentLicenseValidator>();
builder.Services.AddScoped<AgentProvisioningService>();
builder.Services.AddScoped<AgentHandshakeService>();
builder.Services.AddScoped<AgentHeartbeatService>();
builder.Services.AddScoped<DocumentStatusService>();
builder.Services.AddScoped<DocumentQueryService>();
builder.Services.AddScoped<TenantQuotaValidator>();
builder.Services.AddScoped<CertifierConfigurationResolver>();
builder.Services.AddSingleton<CertifierCredentialProtector>();
builder.Services.AddScoped<CertifierCredentialService>();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer)
        && !string.IsNullOrWhiteSpace(options.PlatformAudience)
        && !string.IsNullOrWhiteSpace(options.AgentAudience)
        && options.PlatformTokenMinutes > 0
        && options.AgentTokenMinutes > 0
        && options.PlatformRefreshTokenDays > 0
        && options.AgentRefreshTokenDays > 0,
        "JWT issuer, audiences, and token lifetimes must be configured.")
    .Validate(options => IsValidSigningKey(options.SigningKey), "Jwt:SigningKey must be a base64-encoded key of at least 32 bytes.")
    .ValidateOnStart();

builder.Services.AddOptions<ClientJwtOptions>()
    .Bind(builder.Configuration.GetSection(ClientJwtOptions.SectionName));
builder.Services.AddOptions<AgentHeartbeatOptions>()
    .Bind(builder.Configuration.GetSection(AgentHeartbeatOptions.SectionName))
    .Validate(options => options.CommandLeaseSeconds > 0 && options.MaxCommandsPerHeartbeat > 0,
        "Agent heartbeat lease and command limits must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .Validate(options => options.PlatformLogin.IsValid
        && options.PlatformRefresh.IsValid
        && options.AgentHandshake.IsValid
        && options.AgentRefresh.IsValid
        && options.ClientMe.IsValid
        && options.AgentHeartbeat.IsValid
        && options.AgentDocumentStatus.IsValid
        && options.AgentConfiguration.IsValid
        && options.PlatformAgentManagement.IsValid
        && options.PlatformDocuments.IsValid
        && options.TenantDocuments.IsValid,
        "All rate-limit permit counts and windows must be positive.")
    .ValidateOnStart();

builder.Services.AddAuthentication()
    .AddJwtBearer("LocalJwt", options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new();
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudiences = [jwt.PlatformAudience, jwt.AgentAudience],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = TryCreateSigningKey(jwt.SigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub
        };
    })
    .AddJwtBearer("ClientJwt", options =>
    {
        var clientJwt = builder.Configuration.GetSection(ClientJwtOptions.SectionName).Get<ClientJwtOptions>() ?? new();
        options.Authority = string.IsNullOrWhiteSpace(clientJwt.Authority) ? null : clientJwt.Authority;
        options.Audience = string.IsNullOrWhiteSpace(clientJwt.Audience) ? null : clientJwt.Audience;
        options.RequireHttpsMetadata = clientJwt.RequireHttpsMetadata;
        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdministrator", policy => policy
        .AddAuthenticationSchemes("LocalJwt")
        .RequireAuthenticatedUser()
        .RequireClaim("principal_type", "platform_admin"));
    options.AddPolicy("Agent", policy => policy
        .AddAuthenticationSchemes("LocalJwt")
        .RequireAuthenticatedUser()
        .RequireClaim("principal_type", "agent")
        .RequireClaim("agent_id")
        .RequireClaim("tenant_id")
        .RequireClaim("company_id"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Too many requests",
            Status = StatusCodes.Status429TooManyRequests
        }, cancellationToken);
    };

    AddFixedWindowPolicy(options, "platform-login", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.PlatformLogin,
        context => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}");
    AddFixedWindowPolicy(options, "platform-refresh", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.PlatformRefresh,
        context => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}");
    AddFixedWindowPolicy(options, "agent-handshake", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.AgentHandshake,
        context => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}");
    AddFixedWindowPolicy(options, "agent-refresh", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.AgentRefresh,
        context => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}");
    AddFixedWindowPolicy(options, "client-me", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.ClientMe,
        context => $"subject:{context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}");
    AddFixedWindowPolicy(options, "agent-heartbeat", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.AgentHeartbeat,
        context => $"agent:{context.User.FindFirstValue("agent_id") ?? "unknown"}");
    AddFixedWindowPolicy(options, "agent-document-status", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.AgentDocumentStatus,
        context => $"agent:{context.User.FindFirstValue("agent_id") ?? "unknown"}");
    AddFixedWindowPolicy(options, "agent-configuration", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.AgentConfiguration,
        context => $"agent:{context.User.FindFirstValue("agent_id") ?? "unknown"}");
    AddFixedWindowPolicy(options, "platform-agent-management", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.PlatformAgentManagement,
        context => $"admin:{context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "unknown"}");
    AddFixedWindowPolicy(options, "platform-documents", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.PlatformDocuments,
        context => $"admin:{context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "unknown"}");
    AddFixedWindowPolicy(options, "tenant-documents", context => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.TenantDocuments,
        context => $"subject:{context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}");
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "API de la Plataforma de Firma Electrónica";
        options.Theme = ScalarTheme.Purple; // Cambiar tema visual
        options.DefaultHttpClient = new KeyValuePair<ScalarTarget, ScalarClient>(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DevelopmentSeed:Enabled"))
{
    await using var seedScope = app.Services.CreateAsyncScope();
    await DevelopmentDataSeeder.SeedAsync(
        seedScope.ServiceProvider.GetRequiredService<FelApiDbContext>(),
        seedScope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>(),
        app.Configuration["ClientJwt:Authority"]);
}

if (args.Length == 2 && args[0] == "--bootstrap-platform-admin")
{
    var password = ConsoleSecretReader.Read("New platform admin password: ");
    await using var scope = app.Services.CreateAsyncScope();
    var bootstrapper = scope.ServiceProvider.GetRequiredService<PlatformAdministratorBootstrapper>();
    await bootstrapper.CreateFirstAdministratorAsync(args[1], password);
    Console.WriteLine("Platform administrator created.");
    return;
}

app.Run();

static bool IsValidSigningKey(string value)
{
    try
    {
        return Convert.FromBase64String(value).Length >= 32;
    }
    catch (FormatException)
    {
        return false;
    }
}

static SymmetricSecurityKey? TryCreateSigningKey(string value)
{
    try
    {
        var bytes = Convert.FromBase64String(value);
        return bytes.Length >= 32 ? new SymmetricSecurityKey(bytes) : null;
    }
    catch (FormatException)
    {
        return null;
    }
}

static void AddFixedWindowPolicy(
    Microsoft.AspNetCore.RateLimiting.RateLimiterOptions options,
    string name,
    Func<HttpContext, FixedWindowLimit> getLimit,
    Func<HttpContext, string> partitionKey)
{
    options.AddPolicy(name, context =>
    {
        var limit = getLimit(context);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
}

    public partial class Program { }
