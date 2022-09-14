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
                        , options=>
                        {
                            options.QueueProcessingOrder = QueueProcessingOrder.NewestFirst;
                            options.PermitLimit = 1;
                            options.QueueLimit=0;
                        })
    .AddTokenBucketLimiter("token"
            , options=>{
                options.TokenLimit = 5;
                options.QueueProcessingOrder = QueueProcessingOrder.NewestFirst;
                options.QueueLimit = 0;
                options.ReplenishmentPeriod = TimeSpan.FromSeconds(5);
                options.TokensPerPeriod = 1;
                options.AutoReplenishment = false;
            })
    .AddPolicy(policyName: "token-policy", partitioner: httpContext =>
        {
            //Check if the request has any query string parameters.
            if (httpContext.Request.QueryString.HasValue)
            {
                //If yes, let's don't have a rate limiting policy for this request.
                return RateLimitPartition.GetNoLimiter<string>("free");
            }
            else
            {
                //If no, let's have a rate limiting policy for this request.
                return RateLimitPartition.GetTokenBucketLimiter("token", key =>
                    new TokenBucketRateLimiterOptions(){
                          TokenLimit = 5,
                          QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
                          QueueLimit = 0,
                          ReplenishmentPeriod = TimeSpan.FromSeconds(5),
                          TokensPerPeriod = 1,
                          AutoReplenishment = false
                    });
            }
        })
);

app.MapGet("/free", context =>
{

    context.Response.StatusCode = StatusCodes.Status200OK;
    return context.Response.WriteAsync("All free to call!");

});

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
