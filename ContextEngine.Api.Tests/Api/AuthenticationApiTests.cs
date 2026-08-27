using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextEngine.Api.Tests.Api
{
    /// <summary>
    /// Exercises the real [Authorize]/Identity bearer-token flow end to end - unlike the other
    /// *ControllerApiTests classes, which run against <see cref="ContextEngineApiFactory"/>'s
    /// auth bypass so they can focus on business logic instead of token plumbing.
    /// </summary>
    public class AuthenticationApiTests : IClassFixture<AuthenticationApiTests.RealAuthApiFactory>
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _client;

        public AuthenticationApiTests(RealAuthApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task ProtectedEndpoint_NoToken_ReturnsUnauthorized()
        {
            var response = await _client.GetAsync("/api/search");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ProtectedEndpoint_InvalidToken_ReturnsUnauthorized()
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

            var response = await _client.GetAsync("/api/search");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsUnauthorized()
        {
            var email = $"{Guid.NewGuid()}@example.com";
            await RegisterAsync(email, "Correct-Horse-Battery-1");

            var response = await _client.PostAsJsonAsync("/login", new { email, password = "Wrong-Password-1" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task RegisterThenLogin_ThenProtectedEndpoint_ReturnsOk()
        {
            var email = $"{Guid.NewGuid()}@example.com";
            var password = "Correct-Horse-Battery-1";

            await RegisterAsync(email, password);
            var accessToken = await LoginAsync(email, password);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/search");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _client.SendAsync(request);

            response.EnsureSuccessStatusCode();
        }

        private async Task RegisterAsync(string email, string password)
        {
            var response = await _client.PostAsJsonAsync("/register", new { email, password });
            response.EnsureSuccessStatusCode();
        }

        private async Task<string> LoginAsync(string email, string password)
        {
            var response = await _client.PostAsJsonAsync("/login", new { email, password });
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            return result!.AccessToken;
        }

        private class LoginResponse
        {
            [JsonPropertyName("accessToken")]
            public string AccessToken { get; set; } = string.Empty;
        }

        /// <summary>Same test host as <see cref="ContextEngineApiFactory"/>, but with the auth bypass turned off.</summary>
        public class RealAuthApiFactory : ContextEngineApiFactory
        {
            protected override bool BypassAuthentication => false;
        }
    }
}
