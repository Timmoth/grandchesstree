using System.Diagnostics;
using System.Text;

namespace GrandChessTree.Api.Middleware
{
    public class RequestTimingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestTimingMiddleware> _logger;

        public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context); // Process request
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogInformation("Request {Method} {Path} completed in {ElapsedMilliseconds} ms",
                    context.Request.Method, context.Request.Path, stopwatch.ElapsedMilliseconds);
            }
        }
    }

public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;

        public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                // Log Request
                var requestBody = await ReadRequestBody(context);
                _logger.LogInformation("Request: {Method} {Path} - Body: {Body}",
                    context.Request.Method, context.Request.Path, requestBody);

                // Capture Response
                var originalResponseBodyStream = context.Response.Body;
                using var responseBodyStream = new MemoryStream();
                context.Response.Body = responseBodyStream;

                await _next(context); // Call next middleware

                // Log Response
                var responseBody = await ReadResponseBody(context);
                _logger.LogInformation("Response: {StatusCode} - Body: {Body}",
                    context.Response.StatusCode, responseBody);

                // Reset response stream
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                await responseBodyStream.CopyToAsync(originalResponseBodyStream);
            }
            catch (Exception ex)
            {
                // Log Exception
                _logger.LogError(ex, "Unhandled Exception occurred: {Message}", ex.Message);
                throw; // Ensure the exception propagates
            }
        }

        private async Task<string> ReadRequestBody(HttpContext context)
        {
            context.Request.EnableBuffering(); // Allows multiple reads
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0; // Reset stream position
            return string.IsNullOrWhiteSpace(body) ? "(empty)" : body;
        }

        private async Task<string> ReadResponseBody(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            return string.IsNullOrWhiteSpace(body) ? "(empty)" : body;
        }
    }


}
