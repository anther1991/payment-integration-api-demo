using API_Thanh_toan.Data;
using API_Thanh_toan.Infrastructure;
using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Text;

// Setup Serilog
var config = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    // 1. Access Log: Successful requests (Information) in Controllers (Retain 180 days / 6 months)
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(evt => 
            evt.Level == LogEventLevel.Information && 
            evt.Properties.TryGetValue("SourceContext", out var src) && 
            src.ToString().Contains("API_Thanh_toan.Controllers"))
        .WriteTo.File(new JsonFormatter(renderMessage: true), "logs/access-.json", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 180))
    // 2. Application Log: Standard system/ops logs, exceptions (Retain 180 days / 6 months)
    .WriteTo.Logger(lc => lc
        .Filter.ByExcluding(evt => 
            evt.Level == LogEventLevel.Information && 
            evt.Properties.TryGetValue("SourceContext", out var src) && 
            src.ToString().Contains("API_Thanh_toan.Controllers"))
        .WriteTo.File("logs/app-.txt", 
            rollingInterval: RollingInterval.Day, 
            retainedFileCountLimit: 180,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
    // 4. Console Sink for terminal debugging
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

try
{
    // 3. Windows Event Log: Warnings and Errors (Disabled manageEventSource to prevent admin checks)
    config = config.WriteTo.Logger(lc => lc
        .MinimumLevel.Warning()
        .WriteTo.EventLog("UtilityPaymentAPI", manageEventSource: false));
}
catch (System.Security.SecurityException)
{
    Console.WriteLine("Warning: Access to Windows Event Log is restricted (requires Administrator privileges). Skipping EventLog Sink.");
}

Log.Logger = config.CreateLogger();

try
{
    Log.Information("Starting Utility Payment API...");

    var builder = WebApplication.CreateBuilder(args);

    // Register Serilog as the logging provider
    builder.Host.UseSerilog();

    // Add services to the container.
    builder.Services.AddMemoryCache();

    var keysFolder = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys"));
    if (!keysFolder.Exists) keysFolder.Create();
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(keysFolder)
        .SetApplicationName("UtilityPaymentAPI");

    builder.Services.AddSingleton<IDataProtectionHelper, DataProtectionHelper>();
    builder.Services.AddScoped<IPaymentClientRepository, PaymentClientRepository>();
    builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
    builder.Services.AddScoped<IClientAuthService, ClientAuthService>();
    builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
    builder.Services.AddScoped<IInvoiceLookupService, InvoiceLookupService>();
    builder.Services.AddScoped<IInvoiceValidationRepository, InvoiceValidationRepository>();
    builder.Services.AddScoped<IInvoiceValidationService, InvoiceValidationService>();
    builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
    builder.Services.AddScoped<IPaymentService, PaymentService>();
    builder.Services.AddScoped<IPaymentStatusRepository, PaymentStatusRepository>();
    builder.Services.AddScoped<IPaymentStatusService, PaymentStatusService>();
    builder.Services.AddScoped<IPaymentLinkTokenRepository, PaymentLinkTokenRepository>();
    builder.Services.AddScoped<IPaymentLinkLookupService, PaymentLinkLookupService>();
    builder.Services.AddScoped<HmacSignatureFilter>();

    var jwtSettings = builder.Configuration.GetSection("JwtSettings");
    var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured.");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

    builder.Services.AddControllers();
    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.SuppressModelStateInvalidFilter = true;
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Utility Payment Integration API v1");
        c.RoutePrefix = "swagger";
    });

    app.UseHttpsRedirection();

    app.UseMiddleware<EnableBufferingMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Utility Payment API terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
