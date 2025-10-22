using TinyHabits.Api.Dtos;
using TinyHabits.Api.Models;
using TinyHabits.Api.Data;
using Microsoft.EntityFrameworkCore;
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

// EF Core + SQLite
builder.Services.AddDbContext<TinyHabitsDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

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
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();


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

// להחיל מיגרציות/ליצור DB אם לא קיים
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TinyHabitsDbContext>();
    db.Database.Migrate();
}


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

// var logger = app.Logger;


// POST /api/auth/register
auth.MapPost("/register", async (RegisterRequest req, TinyHabitsDbContext db, IPasswordHasher<User> hasher) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new AuthResponse(false, "Email and password are required"));

    var exists = await db.Users.AnyAsync(u => u.Email == req.Email);
    if (exists)
        return Results.Conflict(new AuthResponse(false, "Email already registered"));

    var user = new User { Email = req.Email };
    user.PasswordHash = hasher.HashPassword(user, req.Password);

    db.Users.Add(user);
    await db.SaveChangesAsync(); // כדי לקבל Id

    var token = CreateJwt(user);
    return Results.Ok(new AuthResponse(true, "Registered", token));
})
.WithName("Register")
.Produces<AuthResponse>(StatusCodes.Status200OK)
.Produces<AuthResponse>(StatusCodes.Status400BadRequest)
.Produces<AuthResponse>(StatusCodes.Status409Conflict);

// POST /api/auth/login
auth.MapPost("/login", async (LoginRequest req, TinyHabitsDbContext db, IPasswordHasher<User> hasher) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == req.Email);
    if (user is null)
        return Results.Unauthorized();

    var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
    if (verify == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

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
habits.MapGet("/", async (HttpContext ctx, TinyHabitsDbContext db) =>
{
    var userId = GetUserIdFromClaims(ctx.User);

    var userHabits = await db.Habits
        .Where(h => h.UserId == userId && !h.IsArchived)
        .ToListAsync();

    // חישוב streak פשוט: כמה ימים רצופים אחורה החל מהיום (אם אין צ׳ק־אין היום – streak=0)
    var today = DateOnly.FromDateTime(DateTime.Today);

    var result = new List<HabitResponse>();
    foreach (var h in userHabits)
    {
        var checkins = await db.Checkins
            .Where(c => c.HabitId == h.Id && c.Date <= today)
            .OrderByDescending(c => c.Date)
            .Select(c => c.Date)
            .ToListAsync();

        int streak = 0;
        var expected = today;

        foreach (var d in checkins)
        {
            if (d == expected)
            {
                streak++;
                expected = expected.AddDays(-1);
            }
            else if (d < expected)
            {
                break; // נקטע הרצף
            }
        }

        result.Add(new HabitResponse(h.Id, h.Title, h.Color, h.IsArchived, streak));
    }

    return Results.Ok(result);
})
.WithName("ListHabits")
.Produces<List<HabitResponse>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// POST /api/habits — יצירת הרגל חדש
habits.MapPost("/", async (HttpContext ctx, HabitCreateRequest body, TinyHabitsDbContext db) =>
{
    var userId = GetUserIdFromClaims(ctx.User);

    if (string.IsNullOrWhiteSpace(body.Title) || body.Title.Length < 3)
        return Results.BadRequest("Title must be at least 3 chars");

    var h = new Habit
    {
        UserId = userId,
        Title = body.Title.Trim(),
        Color = body.Color,
        IsArchived = false,
        CreatedAtUtc = DateTime.UtcNow
    };

    db.Habits.Add(h);
    await db.SaveChangesAsync();

    var res = new HabitResponse(h.Id, h.Title, h.Color, h.IsArchived, 0);
    return Results.Ok(res);
})
.WithName("CreateHabit")
.Produces<HabitResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

// POST /api/habits/{id}/checkins — סימון בוצע לתאריך (ברירת מחדל: היום)
habits.MapPost("/{id:int}/checkins", async (HttpContext ctx, int id, CheckinRequest body, TinyHabitsDbContext db) =>
{
    var userId = GetUserIdFromClaims(ctx.User);

    var habit = await db.Habits.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);
    if (habit is null)
        return Results.NotFound("Habit not found");

    var date = string.IsNullOrWhiteSpace(body.Date)
        ? DateOnly.FromDateTime(DateTime.Today)
        : DateOnly.Parse(body.Date!);

    var exists = await db.Checkins.AnyAsync(c => c.HabitId == id && c.Date == date);
    if (exists)
        return Results.Conflict("Checkin for that date already exists");

    var ck = new Checkin { HabitId = id, Date = date };
    db.Checkins.Add(ck);
    await db.SaveChangesAsync();

    return Results.Ok(new CheckinResponse(ck.Id, ck.Date.ToString("yyyy-MM-dd")));
})
.WithName("AddCheckin")
.Produces<CheckinResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict);

// (רשות) GET /api/habits/{id}/checkins?from=...&to=...
habits.MapGet("/{id:int}/checkins", async (HttpContext ctx, int id, string? from, string? to, TinyHabitsDbContext db) =>
{
    var userId = GetUserIdFromClaims(ctx.User);

    var habit = await db.Habits.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);
    if (habit is null)
        return Results.NotFound("Habit not found");

    var fromDate = string.IsNullOrWhiteSpace(from)
        ? DateOnly.FromDateTime(DateTime.Today.AddDays(-7))
        : DateOnly.Parse(from!);

    var toDate = string.IsNullOrWhiteSpace(to)
        ? DateOnly.FromDateTime(DateTime.Today)
        : DateOnly.Parse(to!);

    var days = await db.Checkins
        .Where(c => c.HabitId == id && c.Date >= fromDate && c.Date <= toDate)
        .OrderBy(c => c.Date)
        .Select(c => c.Date.ToString("yyyy-MM-dd"))
        .ToListAsync();

    return Results.Ok(days);
})
.WithName("GetCheckins")
.Produces<List<string>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status404NotFound);


app.Run();
