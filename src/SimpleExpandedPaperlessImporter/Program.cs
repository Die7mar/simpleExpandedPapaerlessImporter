using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using SimpleExpandedPaperlessImporter.Configuration;
using SimpleExpandedPaperlessImporter.Data;
using SimpleExpandedPaperlessImporter.Services;
using Serilog;
using Serilog.Events;

// ── Serilog bootstrap ────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/paperless-importer-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // ── Settings override file (live-editable via UI) ─────────
    var settingsFilePath = builder.Configuration["SettingsFilePath"]
        ?? Path.Combine(AppContext.BaseDirectory, "settings.json");
    builder.Configuration.AddJsonFile(settingsFilePath, optional: true, reloadOnChange: true);

    // ── Configuration ─────────────────────────────────────────
    builder.Services.Configure<PaperlessSettings>(
        builder.Configuration.GetSection("Paperless"));
    builder.Services.AddOptions();

    // ── SQLite / EF Core ──────────────────────────────────────
    var dbPath = builder.Configuration["DatabasePath"]
        ?? Path.Combine(AppContext.BaseDirectory, "data", "paperless-importer.db");
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlite($"Data Source={dbPath}"));
    builder.Services.AddDbContextFactory<AppDbContext>(opt =>
        opt.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Scoped);

    // ── Authentication (cookie-based, secure) ─────────────────
    builder.Services.AddAuthentication("Cookies")
        .AddCookie("Cookies", opt =>
        {
            opt.LoginPath  = "/login";
            opt.LogoutPath = "/logout";
            opt.AccessDeniedPath = "/login";
            opt.Cookie.Name     = "sepi_auth";
            opt.Cookie.HttpOnly = true;
            opt.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            opt.Cookie.SameSite    = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
            opt.ExpireTimeSpan  = TimeSpan.FromHours(12);
            opt.SlidingExpiration = true;
        });
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    // ── HTTP Client ───────────────────────────────────────────
    builder.Services.AddHttpClient<PaperlessApiService>()
        .ConfigureHttpClient((_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });

    // ── Application services ──────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ImportStatusService>();
    builder.Services.AddSingleton<SettingsService>();
    builder.Services.AddSingleton<AuthService>();
    builder.Services.AddScoped<EmailConverterService>();
    builder.Services.AddScoped<DocumentImportService>();

    // ── Background services ───────────────────────────────────
    builder.Services.AddSingleton<CorrespondentSyncService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<CorrespondentSyncService>());
    builder.Services.AddHostedService<FolderWatcherService>();
    builder.Services.AddHostedService<FileCleanupService>();

    // ── Blazor ────────────────────────────────────────────────
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var app = builder.Build();

    // ── DB init, default user, job history ───────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
    var authService = app.Services.GetRequiredService<AuthService>();
    await authService.EnsureDefaultUserAsync();

    var statusService = app.Services.GetRequiredService<ImportStatusService>();
    await statusService.InitializeAsync();

    if (!app.Environment.IsDevelopment())
        app.UseExceptionHandler("/Error");

    app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapRazorComponents<SimpleExpandedPaperlessImporter.Components.App>()
        .AddInteractiveServerRenderMode();

    // ── Login endpoint (handles HTML form POST from /login page) ─
    app.MapPost("/do-login", async (HttpContext ctx, AuthService authSvc) =>
    {
        var form     = await ctx.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();

        // Fixed delay to mitigate timing/enumeration attacks
        await Task.Delay(500);

        var user = await authSvc.ValidateAsync(username, password);
        if (user is null)
            return Results.Redirect("/login?error=1");

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, user.Username),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("IsAdmin", user.IsAdmin.ToString())
        };
        var identity  = new System.Security.Claims.ClaimsIdentity(claims, "Cookies");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        await ctx.SignInAsync("Cookies", principal, new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            IsPersistent  = true,
            ExpiresUtc    = DateTimeOffset.UtcNow.AddHours(12)
        });

        return Results.Redirect("/");
    });

    // ── Logout endpoint ───────────────────────────────────────
    app.MapPost("/logout", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync("Cookies");
        return Results.Redirect("/login");
    }).RequireAuthorization();

    // GET logout (redirect after password change)
    app.MapGet("/logout-redirect", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync("Cookies");
        return Results.Redirect("/login");
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    Log.CloseAndFlush();
}
