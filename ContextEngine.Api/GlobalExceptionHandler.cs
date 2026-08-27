using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ContextEngine.Api
{
    /// <summary>
    /// Central exception handler that maps unhandled exceptions to appropriate HTTP status codes
    /// and returns them as RFC 7807 ProblemDetails responses.
    /// </summary>
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Logs the exception and writes a <see cref="ProblemDetails"/> response with a status code
        /// appropriate to the exception type. Always returns true, meaning this handler is the final
        /// stop for every unhandled exception in the pipeline.
        /// </summary>
        /// <param name="httpContext">Context of the request that triggered the exception.</param>
        /// <param name="exception">The unhandled exception.</param>
        /// <param name="cancellationToken">Token used to cancel writing the response.</param>
        /// <returns>Always true, indicating the exception was handled.</returns>
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Unhandled exception occurred");

            int statusCode;
            string title;
            string? detail;

            if (exception is NotSupportedException)
            {
                statusCode = StatusCodes.Status400BadRequest;
                title = "Unsupported request.";
                detail = exception.Message;
            }
            else if (exception is NotImplementedException)
            {
                statusCode = StatusCodes.Status501NotImplemented;
                title = "Not implemented.";
                detail = exception.Message;
            }
            else if (exception is FileNotFoundException || exception is DirectoryNotFoundException)
            {
                statusCode = StatusCodes.Status404NotFound;
                title = "File not found.";
                detail = exception.Message;
            }
            else
            {
                // Unrecognized exceptions are treated as internal errors. The message is deliberately
                // omitted from the response (it's still logged above) to avoid leaking internal details.
                statusCode = StatusCodes.Status500InternalServerError;
                title = "An unexpected error occurred.";
                detail = null;
            }

            httpContext.Response.StatusCode = statusCode;

            await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail
            }, cancellationToken);

            return true;
        }
    }
}
