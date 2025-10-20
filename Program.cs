using TinyHabits.Api.Dtos;
using TinyHabits.Api.Models;
using TinyHabits.Api.Services;
using Microsoft.AspNetCore.Identity;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

// OpenAPI (NET 9)
builder.Services.AddOpenApi();

// CORS: בשלב לימודי נאפשר הכל (נקשיח בהמשך)
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(p =>
        p.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod());
});

// תלות: מחסן משתמשים בזיכרון + Hash לסיסמאות
builder.Services.AddSingleton<InMemoryUserStore>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
    app.MapScalarApiReference(o =>  // UI: /scalar/v1
    {
        o.Title = "Tiny Habits API";
    });
}

//app.UseHttpsRedirection(); // נכבה זמנית כדי לא לבלבל
app.UseCors();

// בדיקת עשן
app.MapGet("/ping", () => "pong");

// ====== AUTH GROUP ======
var auth = app.MapGroup("/api/auth").WithOpenApi();

// POST /api/auth/register
auth.MapPost("/register", (RegisterRequest req, InMemoryUserStore store, IPasswordHasher<User> hasher) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new AuthResponse(false, "Email and password are required"));

    if (store.Exists(req.Email))
        return Results.Conflict(new AuthResponse(false, "Email already registered"));

    var tempUser = new User { Email = req.Email };
    var hash = hasher.HashPassword(tempUser, req.Password);
    var user = store.Add(req.Email, hash);

    // דמו: טוקן פשוט (GUID). נשדרג ל-JWT בהמשך.
    var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    return Results.Ok(new AuthResponse(true, "Registered", token));
})
.WithName("Register")
.Produces<AuthResponse>(StatusCodes.Status200OK)
.Produces<AuthResponse>(StatusCodes.Status400BadRequest)
.Produces<AuthResponse>(StatusCodes.Status409Conflict);

// POST /api/auth/login
auth.MapPost("/login", (LoginRequest req, InMemoryUserStore store, IPasswordHasher<User> hasher) =>
{
    var user = store.FindByEmail(req.Email);
    if (user is null)
        return Results.Unauthorized();

    var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
    if (verify == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    return Results.Ok(new AuthResponse(true, "Logged in", token));
})
.WithName("Login")
.Produces<AuthResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// דוגמת weather הישנה (לא חובה)
app.MapGet("/weatherforecast", () =>
{
    var summaries = new[] { "Freezing","Bracing","Chilly","Cool","Mild","Warm","Balmy","Hot","Sweltering","Scorching" };
    var forecast = Enumerable.Range(1, 5).Select(i =>
        new
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(i)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = summaries[Random.Shared.Next(summaries.Length)]
        });
    return Results.Ok(forecast);
});

app.Run();
