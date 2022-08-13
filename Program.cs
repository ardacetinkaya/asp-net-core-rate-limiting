using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseAuthorization();
app.UseRateLimiter(
    new RateLimiterOptions()
    {
        OnRejected = (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            context.Lease.GetAllMetadata().ToList()
                .ForEach(m => app.Logger.LogWarning($"Rate limit exceeded: {m.Key} {m.Value}"));

            return new ValueTask();
        },
        RejectionStatusCode = StatusCodes.Status429TooManyRequests

    }
    .AddConcurrencyLimiter("controllers"
                        , new ConcurrencyLimiterOptions(1
                        , QueueProcessingOrder.NewestFirst
                        , 0))
    .AddTokenBucketLimiter("token"
            , new TokenBucketRateLimiterOptions(tokenLimit: 5
            , queueProcessingOrder: QueueProcessingOrder.NewestFirst
            , queueLimit: 0
            , replenishmentPeriod: TimeSpan.FromSeconds(5)
            , tokensPerPeriod: 1
            , autoReplenishment: false))
    .AddPolicy(policyName: "token-policy", partitioner: httpContext =>
        {
            //Check if the request has any query string parameters.
            if (httpContext.Request.QueryString.HasValue)
            {
                //If yes, let's don't have a rate limiting policy for this request.
                return RateLimitPartition.CreateNoLimiter<string>("free");
            }
            else
            {
                //If no, let's have a rate limiting policy for this request.
                return RateLimitPartition.CreateTokenBucketLimiter("token", key =>
                    new TokenBucketRateLimiterOptions(tokenLimit: 5
                        , queueProcessingOrder: QueueProcessingOrder.NewestFirst
                        , queueLimit: 0
                        , replenishmentPeriod: TimeSpan.FromSeconds(5)
                        , tokensPerPeriod: 1
                        , autoReplenishment: false));
            }
        })
    .AddNoLimiter("free")
);

app.MapGet("/free", context =>
{

    context.Response.StatusCode = StatusCodes.Status200OK;
    return context.Response.WriteAsync("All free to call!");

}).RequireRateLimiting("free");

app.MapGet("/hello", context =>
{

    context.Response.StatusCode = StatusCodes.Status200OK;
    return context.Response.WriteAsync("Hello World!");

}).RequireRateLimiting("token");

app.MapGet("/token", context =>
{

    context.Response.StatusCode = StatusCodes.Status200OK;
    return context.Response.WriteAsync("Token limited to 5 per 5 seconds!");

}).RequireRateLimiting("token-policy");

app.MapControllers().RequireRateLimiting("controllers");

app.Run();
