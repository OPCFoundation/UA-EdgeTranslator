using WotOpcUaMapper.Components;
using WotOpcUaMapper.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Application services
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<WotFileService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<CloudLibraryClient>();
builder.Services.AddScoped<MapperState>();
builder.Services.AddSingleton<WotOpcUaMapper.UAClientLib.OpcUaApplication>();
builder.Services.AddScoped<WotOpcUaMapper.UAClientLib.UAClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
