using Anthropic;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ContextEngine.Api;
using ContextEngine.Api.Data;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Models.Identity;
using ContextEngine.Api.Parsers;
using ContextEngine.Api.Parsers.Interfaces;
using ContextEngine.Api.Services;
using ContextEngine.Api.Services.Interfaces;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ContextEngineDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ASP.NET Core Identity, issuing/validating opaque bearer tokens (AddIdentityApiEndpoints's
// built-in scheme - see IdentityConstants.BearerScheme) so callers authenticate with a token from
// POST /login instead of a browser cookie. No separate JWT package needed for this.
builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<ContextEngineDbContext>();

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    // Serialize enums (e.g. ChunkType) as their string names instead of raw numbers, so
    // API responses/requests stay self-descriptive for AI agent consumers.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

    options.IncludeXmlComments(xmlPath);

    // Lets Swagger UI's "Authorize" button attach the bearer token obtained from POST /login to
    // every request it sends, since the [Authorize]-protected endpoints below all expect one.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by POST /login."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddScoped<IChunkService, ChunkService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ISearchService, SearchService>();

builder.Services.AddScoped<IDocxParser, DocxParser>();
builder.Services.AddScoped<IPdfParser, PdfParser>();
builder.Services.AddScoped<ChunkMappings>();

// Loads the local, open-source ONNX embedding model (all-MiniLM-L6-v2 - see
// EmbeddingModel/NOTICE.txt for its source and license) once, here, at startup via Semantic
// Kernel's ONNX Runtime connector. AddBertOnnxEmbeddingGenerator reads the model files and builds
// the underlying InferenceSession synchronously as part of this call (not lazily on first use), so
// semantic search is ready to serve requests as soon as the app finishes starting, with no cloud
// embedding API or key involved at any point.
builder.Services.AddBertOnnxEmbeddingGenerator(
    Path.Combine(AppContext.BaseDirectory, "EmbeddingModel", "model.onnx"),
    Path.Combine(AppContext.BaseDirectory, "EmbeddingModel", "vocab.txt"));
builder.Services.AddSingleton<IEmbeddingService, OnnxEmbeddingService>();

// Reads the API key from the ANTHROPIC_API_KEY environment variable.
builder.Services.AddSingleton(new AnthropicClient());
builder.Services.AddScoped<IAiHelper, AiHelper>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Exposes /register, /login, /refresh etc. for ApplicationUser accounts (see
// AddIdentityApiEndpoints above) - unauthenticated by design, since a caller needs them to obtain
// a token in the first place.
app.MapIdentityApi<ApplicationUser>();

app.MapControllers();

app.Run();

// Exposes the top-level statement Program for WebApplicationFactory<Program> in the test project.
public partial class Program { }
