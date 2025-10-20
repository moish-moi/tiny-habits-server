using System.Collections.Concurrent;
using TinyHabits.Api.Models;

namespace TinyHabits.Api.Services;

public class InMemoryHabitStore
{
    private int _nextHabitId = 1;
    private int _nextCheckinId = 1;

    // לכל משתמש: רשימת הרגלים
    private readonly ConcurrentDictionary<int, List<Habit>> _habitsByUserId = new();
    // לכל הרגל: אוסף סימונים לפי תאריך
    private readonly ConcurrentDictionary<int, HashSet<DateOnly>> _checkinsByHabitId = new();

    public List<Habit> GetHabits(int userId)
        => _habitsByUserId.TryGetValue(userId, out var list) ? list : new List<Habit>();

    public Habit AddHabit(int userId, string title, string? color)
    {
        var h = new Habit { Id = _nextHabitId++, UserId = userId, Title = title, Color = color };
        var list = _habitsByUserId.GetOrAdd(userId, _ => new List<Habit>());
        list.Add(h);
        return h;
    }

    public Habit? FindHabit(int userId, int habitId)
        => GetHabits(userId).FirstOrDefault(h => h.Id == habitId);

    public void ArchiveHabit(int userId, int habitId)
    {
        var h = FindHabit(userId, habitId);
        if (h is null) throw new KeyNotFoundException("Habit not found");
        h.IsArchived = true;
    }

    public Checkin AddCheckin(int userId, int habitId, DateOnly date)
    {
        var h = FindHabit(userId, habitId) ?? throw new KeyNotFoundException("Habit not found");
        var set = _checkinsByHabitId.GetOrAdd(h.Id, _ => new HashSet<DateOnly>());
        if (!set.Add(date))
            throw new InvalidOperationException("Checkin already exists for this date");
        return new Checkin { Id = _nextCheckinId++, HabitId = h.Id, Date = date };
    }

    public IEnumerable<DateOnly> GetCheckinsRange(int userId, int habitId, DateOnly from, DateOnly to)
    {
        var h = FindHabit(userId, habitId) ?? throw new KeyNotFoundException("Habit not found");
        if (_checkinsByHabitId.TryGetValue(h.Id, out var set))
            return set.Where(d => d >= from && d <= to).OrderBy(d => d);
        return Enumerable.Empty<DateOnly>();
    }

    public int GetCurrentStreak(int userId, int habitId)
    {
        var h = FindHabit(userId, habitId);
        if (h is null) return 0;

        if (!_checkinsByHabitId.TryGetValue(h.Id, out var set) || set.Count == 0)
            return 0;

        var today = DateOnly.FromDateTime(DateTime.Today);
        int streak = 0;
        var day = today;
        while (set.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }
}
