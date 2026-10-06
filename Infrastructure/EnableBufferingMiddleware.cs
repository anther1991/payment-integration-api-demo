namespace API_Thanh_toan.Infrastructure;

public class EnableBufferingMiddleware
{
    private readonly RequestDelegate _next;

    public EnableBufferingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        await _next(context);
    }
}
