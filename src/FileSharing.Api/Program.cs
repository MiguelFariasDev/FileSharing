using FileSharing.Api;
using FileSharing.Api.Extensions;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAuthServices(builder.Configuration);
builder.Services.AddJwtAuthentication();
builder.Services.AddFileStorage(builder.Configuration);
builder.Services.AddSwaggerWithJwtSupport();

// Slows down brute-force guessing of public share tokens against GET /api/public/files/{token}.
// A single shared fixed window (not partitioned per client) is intentional for this phase —
// it is the simplest limiter that still caps the guess rate; per-IP partitioning can be
// layered on later without touching the endpoint itself.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter(RateLimiterPolicyNames.PublicFiles, limiterOptions =>
    {
        limiterOptions.PermitLimit = 30;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
