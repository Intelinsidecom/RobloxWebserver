using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http.Features;

namespace Api.Middleware;

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestResponseLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        // Ensure buffering is enabled with a generous limit (10 MB)
        context.Request.EnableBuffering(bufferThreshold: 1024 * 30, bufferLimit: 1024 * 1024 * 10);

        var requestInfo = await BuildRequestInfo(context.Request);

        // Capture the response by swapping the body stream
        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            await _next(context);
            sw.Stop();
        }
        finally
        {
            // Read response body
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();
            context.Response.Body.Seek(0, SeekOrigin.Begin);

            // Print logs
            PrintLog(requestInfo, context, responseText, sw.Elapsed);

            // Copy the response back to the original body stream
            await responseBuffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;
        }
    }

    private static async Task<string> BuildRequestInfo(HttpRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("info: Custom.RequestLogger[1]");
        sb.AppendLine("      Request:");
        sb.AppendLine($"      Protocol: {request.Protocol}");
        sb.AppendLine($"      Method: {request.Method}");
        sb.AppendLine($"      Scheme: {request.Scheme}");
        sb.AppendLine($"      PathBase: {request.PathBase}");
        sb.AppendLine($"      Path: {request.Path}");
        sb.AppendLine($"      QueryString: {request.QueryString}");
        var rawTarget = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (!string.IsNullOrEmpty(rawTarget))
        {
            sb.AppendLine($"      RawTarget: {rawTarget}");
        }
        sb.AppendLine($"      Host: {request.Host}");
        sb.AppendLine($"      ClientCert: {request.HttpContext.Connection.ClientCertificate != null}");

        // Headers
        foreach (var header in request.Headers)
        {
            if (IsClientIpHeader(header.Key))
            {
                sb.AppendLine($"      {header.Key}: [Redacted]");
            }
            else
            {
                sb.AppendLine($"      {header.Key}: {Sanitize(header.Value)}");
            }
        }

        // Body
        string bodyText = string.Empty;
        try
        {
            // Regardless of Content-Length, attempt to read the buffered body (covers chunked/proxy cases)
            if (request.Body.CanSeek)
            {
                request.Body.Seek(0, SeekOrigin.Begin);
            }
            using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            bodyText = await reader.ReadToEndAsync();
            if (request.Body.CanSeek)
            {
                request.Body.Seek(0, SeekOrigin.Begin);
            }
        }
        catch
        {
            // Ignore body read errors; continue logging other parts
        }

        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            sb.AppendLine("      Body:");
            // Truncate huge bodies to 1MB equivalent to the HttpLogging limits
            var display = bodyText.Length > 1_000_000 ? bodyText.Substring(0, 1_000_000) + "... [truncated]" : bodyText;
            sb.AppendLine("      " + display.Replace("\n", "\n      "));
        }

        // Form fields (if applicable)
        try
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync();
                if (form.Count > 0)
                {
                    sb.AppendLine("      Form:");
                    foreach (var kv in form)
                    {
                        sb.AppendLine($"      {kv.Key}: {Sanitize(kv.Value)}");
                    }
                }
            }
        }
        catch
        {
            // Ignore form parsing errors
        }

        return sb.ToString();
    }

    private static void PrintLog(string requestInfo, HttpContext context, string responseText, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.Append(requestInfo);
        sb.AppendLine("info: Custom.RequestLogger[2]");
        sb.AppendLine("      Response:");
        sb.AppendLine($"      StatusCode: {context.Response.StatusCode}");

        foreach (var header in context.Response.Headers)
        {
            sb.AppendLine($"      {header.Key}: {Sanitize(header.Value)}");
        }

        if (!string.IsNullOrEmpty(responseText))
        {
            var body = responseText.Length > 1_000_000 ? responseText.Substring(0, 1_000_000) + "... [truncated]" : responseText;
            sb.AppendLine("      Body:");
            sb.AppendLine("      " + body.Replace("\n", "\n      "));
        }
        else
        {
            sb.AppendLine("      Body:");
            sb.AppendLine("      (empty)");
        }

        sb.AppendLine($"      Duration: {elapsed.TotalMilliseconds:n0} ms");

        Console.WriteLine(sb.ToString());
    }

    private static string Sanitize(StringValues value)
    {
        // Keep as-is but allow easy redaction extension in future
        return value.ToString();
    }

    private static bool IsClientIpHeader(string key)
    {
        return key.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase)
            || key.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase)
            || key.Equals("CF-Connecting-IP", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Cf-Connecting-Ip", StringComparison.OrdinalIgnoreCase)
            || key.Equals("True-Client-IP", StringComparison.OrdinalIgnoreCase)
            || key.Equals("X-Client-IP", StringComparison.OrdinalIgnoreCase)
            || key.Equals("X-Cluster-Client-IP", StringComparison.OrdinalIgnoreCase);
    }
}
