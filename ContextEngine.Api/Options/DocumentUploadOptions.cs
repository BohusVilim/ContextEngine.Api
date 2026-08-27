namespace ContextEngine.Api.Options
{
    /// <summary>
    /// Configuration for how <see cref="Services.DocumentService.UploadDocumentAsync"/> handles the
    /// server-local file path it's given. Bound from the <c>DocumentUpload</c> section of
    /// appsettings.json.
    /// </summary>
    public class DocumentUploadOptions
    {
        /// <summary>
        /// Directory that <c>documentPath</c> must resolve to (or resolve to a location inside), for
        /// every call to <c>POST /api/documents</c>. A path outside it is rejected with
        /// <see cref="UnauthorizedAccessException"/> (mapped to <c>403 Forbidden</c> by
        /// <see cref="GlobalExceptionHandler"/>) instead of being read.
        /// </summary>
        /// <remarks>
        /// Left <see langword="null"/> by default: this API is designed as a trusted local tool where
        /// the caller (e.g. an AI agent) and the server process already share a filesystem, so by
        /// default any path the server's OS user can read is accepted, exactly as before this option
        /// existed - see the README's "Known limitations". Set this when you want to sandbox uploads
        /// to a specific folder instead, e.g. when the API might ever be reachable from outside a
        /// single trusted local session.
        /// </remarks>
        public string? AllowedRootPath { get; set; }
    }
}
