using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace ContextEngine.Api.Tests.Unit
{
    public class GlobalExceptionHandlerTests
    {
        [Theory]
        [MemberData(nameof(ExceptionStatusCodeCases))]
        public async Task TryHandleAsync_MapsExceptionTypeToExpectedStatusCode(Exception exception, int expectedStatusCode)
        {
            var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
            var context = new DefaultHttpContext
            {
                Response = { Body = new MemoryStream() }
            };

            var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

            Assert.True(handled);
            Assert.Equal(expectedStatusCode, context.Response.StatusCode);

            var problemDetails = await ReadProblemDetails(context);
            Assert.Equal(expectedStatusCode, problemDetails.GetProperty("status").GetInt32());
        }

        public static IEnumerable<object[]> ExceptionStatusCodeCases()
        {
            yield return new object[] { new NotSupportedException("bad type"), StatusCodes.Status400BadRequest };
            yield return new object[] { new NotImplementedException("not done"), StatusCodes.Status501NotImplemented };
            yield return new object[] { new FileNotFoundException("missing"), StatusCodes.Status404NotFound };
            yield return new object[] { new DirectoryNotFoundException("missing dir"), StatusCodes.Status404NotFound };
            yield return new object[] { new UnauthorizedAccessException("path outside allowed root"), StatusCodes.Status403Forbidden };
            yield return new object[] { new InvalidOperationException("boom"), StatusCodes.Status500InternalServerError };
        }

        [Fact]
        public async Task TryHandleAsync_UnrecognizedException_DoesNotLeakMessageInResponse()
        {
            var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
            var context = new DefaultHttpContext
            {
                Response = { Body = new MemoryStream() }
            };

            await handler.TryHandleAsync(context, new InvalidOperationException("sensitive internal detail"), CancellationToken.None);

            var problemDetails = await ReadProblemDetails(context);
            var detail = problemDetails.TryGetProperty("detail", out var detailProperty) ? detailProperty.GetString() : null;

            Assert.Null(detail);
        }

        [Fact]
        public async Task TryHandleAsync_KnownException_IncludesMessageAsDetail()
        {
            var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
            var context = new DefaultHttpContext
            {
                Response = { Body = new MemoryStream() }
            };

            await handler.TryHandleAsync(context, new NotSupportedException("no parser for .txt"), CancellationToken.None);

            var problemDetails = await ReadProblemDetails(context);
            Assert.Equal("no parser for .txt", problemDetails.GetProperty("detail").GetString());
        }

        private static async Task<JsonElement> ReadProblemDetails(DefaultHttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            var body = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
    }
}
