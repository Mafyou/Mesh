var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

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

// Sert les fichiers ajoutés dynamiquement (ex. firmware) sans interférer avec MapStaticAssets
app.MapGet("/firmware/{filename}", (string filename, IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "firmware", filename);
    return File.Exists(path)
        ? Results.File(path, "application/octet-stream")
        : Results.NotFound();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Mesh.Web.Client._Imports).Assembly);

app.Run();
