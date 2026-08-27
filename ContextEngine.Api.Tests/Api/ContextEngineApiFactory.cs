using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ContextEngine.Api.Data;
using ContextEngine.Api.Services.Interfaces;
using ContextEngine.Api.Tests.TestHelpers;

namespace ContextEngine.Api.Tests.Api
{
    /// <summary>
    /// Boots the API in-process for integration tests, replacing the SQLite database configured in
    /// Program.cs with a fresh temp-file database per factory instance, so tests never touch the
    /// developer's real ContextEngine.db.
    /// </summary>
    public class ContextEngineApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ContextEngineApiTests_{Guid.NewGuid()}.db");

        /// <summary>
        /// When true (the default), every request is auto-authenticated as a fixed test user (see
        /// <see cref="TestAuthHandler"/>), so tests that exercise business logic don't each need to
        /// register/login for a real token. AuthenticationApiTests overrides this to false to test
        /// the real [Authorize]/login flow instead.
        /// </summary>
        protected virtual bool BypassAuthentication => true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var existingRegistration = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ContextEngineDbContext>));
                if (existingRegistration != null)
                {
                    services.Remove(existingRegistration);
                }

                services.AddDbContext<ContextEngineDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"));

                // Swap in a no-op AI helper so uploads through the test host never call the real
                // Anthropic API (no network access, no API key needed in the test environment).
                var existingAiHelper = services.SingleOrDefault(d => d.ServiceType == typeof(IAiHelper));
                if (existingAiHelper != null)
                {
                    services.Remove(existingAiHelper);
                }

                services.AddScoped<IAiHelper, FakeAiHelper>();

                if (BypassAuthentication)
                {
                    // Re-pointing the default scheme at TestAuthHandler (rather than Identity's
                    // bearer scheme from Program.cs) makes every request authenticate as a fixed
                    // test user regardless of headers.
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, options => { });
                }

                // Build a scoped provider just to create the schema before any request runs.
                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<ContextEngineDbContext>().Database.EnsureCreated();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Microsoft.Data.Sqlite pools native connections by connection string, independently of
            // the DI container being disposed above, so the file stays locked until pools are cleared.
            SqliteConnection.ClearAllPools();

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
    }
}
