using ETicaret.Api.Data;
using ETicaret.Api.Models.Entities;

namespace ETicaret.Api.Middleware;

public class LoggingMiddleware
{
    private readonly RequestDelegate _next;

    public LoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        await _next(context);

        if (context.Request.Method != "GET")
        {
            int? userId = null;
            var userIdClaim = context.User.FindFirst("UserId");
            if (userIdClaim != null)
            {
                userId = int.Parse(userIdClaim.Value);
            }

            var log = new Log
            {
                UserId = userId,
                Action = $"{context.Request.Method} {context.Request.Path}",
                Details = $"Status: {context.Response.StatusCode}",
                Timestamp = DateTime.UtcNow
            };

            dbContext.Logs.Add(log);
            await dbContext.SaveChangesAsync();
        }
    }
}