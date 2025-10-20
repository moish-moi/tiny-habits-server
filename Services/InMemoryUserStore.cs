using System.Collections.Concurrent;
using TinyHabits.Api.Models;

namespace TinyHabits.Api.Services;

/// <summary>
/// מחסן משתמשים בזיכרון בלבד (לשלב הלמידה).
/// </summary>
public class InMemoryUserStore
{
    private readonly ConcurrentDictionary<string, User> _byEmail = new();
    private int _nextId = 1;

    public bool Exists(string email) => _byEmail.ContainsKey(email.ToLowerInvariant());

    public User? FindByEmail(string email)
    {
        _byEmail.TryGetValue(email.ToLowerInvariant(), out var user);
        return user;
    }

    public User Add(string email, string passwordHash)
    {
        var user = new User
        {
            Id = _nextId++,
            Email = email,
            PasswordHash = passwordHash
        };
        if (!_byEmail.TryAdd(email.ToLowerInvariant(), user))
            throw new InvalidOperationException("Email already exists");
        return user;
    }
}
