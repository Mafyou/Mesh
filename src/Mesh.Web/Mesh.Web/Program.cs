var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddHttpClient("firmware", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MeshUnited-Flasher/1.0");
    client.Timeout = TimeSpan.FromSeconds(60);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Proxy pour télécharger des binaires firmware depuis GitHub (contourne CORS navigateur)
app.MapGet("/api/firmware-proxy", async (string url, IHttpClientFactory factory) =>
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
        uri.Scheme != Uri.UriSchemeHttps ||
        !IsAllowedFirmwareHost(uri.Host))
    {
        return Results.BadRequest("URL non autorisée.");
    }

    var client = factory.CreateClient("firmware");
    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    if (!response.IsSuccessStatusCode)
        return Results.StatusCode((int)response.StatusCode);

    var bytes = await response.Content.ReadAsByteArrayAsync();
    return Results.Bytes(bytes, "application/octet-stream");
});

static bool IsAllowedFirmwareHost(string host) =>
    host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
    host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
    host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

// Sert les fichiers ajoutés dynamiquement (firmware, app) sans interférer avec MapStaticAssets
app.MapGet("/firmware/{filename}", (string filename, IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "firmware", filename);
    return File.Exists(path)
        ? Results.File(path, "application/octet-stream", fileDownloadName: filename)
        : Results.NotFound();
});

app.MapGet("/app/{filename}", (string filename, IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "app", filename);
    return File.Exists(path)
        ? Results.File(path, "application/octet-stream", fileDownloadName: filename)
        : Results.NotFound();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Mesh.Web.Client._Imports).Assembly);

app.Run();
