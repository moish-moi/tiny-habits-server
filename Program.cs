using TinyHabits.Api.Dtos;
using TinyHabits.Api.Models;
using TinyHabits.Api.Services;
using Microsoft.AspNetCore.Identity;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;



var builder = WebApplication.CreateBuilder(args);

// OpenAPI (NET 9)
builder.Services.AddOpenApi();


// === JWT Auth ===
var jwtCfg = builder.Configuration.GetSection("Jwt");
var secretKey = jwtCfg["Key"]!;
var issuer = jwtCfg["Issuer"];
var audience = jwtCfg["Audience"];

builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
{
    o.RequireHttpsMetadata = false; // DEV בלבד
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();



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
// חדש:
builder.Services.AddSingleton<InMemoryHabitStore>();

// עזר: יצירת JWT
string CreateJwt(User user)
{
    var jwtCfg = builder.Configuration.GetSection("Jwt");
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtCfg["Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expires = DateTime.UtcNow.AddMinutes(int.Parse(jwtCfg["ExpiresMinutes"]!));

    // סטנדרטי: sub = userId, וגם email
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var token = new JwtSecurityToken(
        issuer: jwtCfg["Issuer"],
        audience: jwtCfg["Audience"],
        claims: claims,
        expires: expires,
        signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(token);
}


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

app.UseAuthentication();   // ✅ הוסף
app.UseAuthorization();    // ✅ הוסף

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

    // JWT אמיתי
var token = CreateJwt(user);
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

    // JWT אמיתי
    var token = CreateJwt(user);
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

// ====== HABITS GROUP ======
var habits = app.MapGroup("/api/habits")
                .RequireAuthorization()    // כל המסלולים כאן דורשים JWT
                .WithOpenApi();

// עוזר: חילוץ userId מתוך ה-claims של ה-JWT
static int GetUserIdFromClaims(ClaimsPrincipal user)
{
    var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
             ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrWhiteSpace(sub)) throw new UnauthorizedAccessException();
    return int.Parse(sub);
}

// GET /api/habits — רשימת הרגלים (Active) + Streak מחושב
habits.MapGet("/", (HttpContext ctx, InMemoryHabitStore store) =>
{
    var userId = GetUserIdFromClaims(ctx.User);
    var list = store.GetHabits(userId)
                    .Where(h => !h.IsArchived)
                    .Select(h => new HabitResponse(
                        h.Id, h.Title, h.Color, h.IsArchived,
                        store.GetCurrentStreak(userId, h.Id)
                    ))
                    .ToList();
    return Results.Ok(list);
})
.WithName("ListHabits")
.Produces<List<HabitResponse>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// POST /api/habits — יצירת הרגל חדש
habits.MapPost("/", (HttpContext ctx, HabitCreateRequest body, InMemoryHabitStore store) =>
{
    var userId = GetUserIdFromClaims(ctx.User);
    if (string.IsNullOrWhiteSpace(body.Title) || body.Title.Length < 3)
        return Results.BadRequest("Title must be at least 3 chars");

    var h = store.AddHabit(userId, body.Title.Trim(), body.Color);
    var res = new HabitResponse(h.Id, h.Title, h.Color, h.IsArchived, 0);
    return Results.Ok(res);
})
.WithName("CreateHabit")
.Produces<HabitResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

// POST /api/habits/{id}/checkins — סימון בוצע לתאריך (ברירת מחדל: היום)
habits.MapPost("/{id:int}/checkins", (HttpContext ctx, int id, CheckinRequest body, InMemoryHabitStore store) =>
{
    var userId = GetUserIdFromClaims(ctx.User);
    var date = string.IsNullOrWhiteSpace(body.Date)
        ? DateOnly.FromDateTime(DateTime.Today)
        : DateOnly.Parse(body.Date!);

    try
    {
        var ck = store.AddCheckin(userId, id, date);
        return Results.Ok(new CheckinResponse(ck.Id, ck.Date.ToString("yyyy-MM-dd")));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound("Habit not found");
    }
    catch (InvalidOperationException)
    {
        return Results.Conflict("Checkin for that date already exists");
    }
})
.WithName("AddCheckin")
.Produces<CheckinResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict);

// (רשות) GET /api/habits/{id}/checkins?from=...&to=...
habits.MapGet("/{id:int}/checkins", (HttpContext ctx, int id, string? from, string? to, InMemoryHabitStore store) =>
{
    var userId = GetUserIdFromClaims(ctx.User);

    var fromDate = string.IsNullOrWhiteSpace(from)
        ? DateOnly.FromDateTime(DateTime.Today.AddDays(-7))
        : DateOnly.Parse(from!);

    var toDate = string.IsNullOrWhiteSpace(to)
        ? DateOnly.FromDateTime(DateTime.Today)
        : DateOnly.Parse(to!);

    try
    {
        var days = store.GetCheckinsRange(userId, id, fromDate, toDate)
                        .Select(d => d.ToString("yyyy-MM-dd"))
                        .ToList();
        return Results.Ok(days);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound("Habit not found");
    }
})
.WithName("GetCheckins")
.Produces<List<string>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status404NotFound);


app.Run();
