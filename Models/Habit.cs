namespace TinyHabits.Api.Models;

public class Habit
{
    public int Id { get; set; }
    public int UserId { get; set; }              // שייכות למשתמש
    public string Title { get; set; } = "";
    public string? Color { get; set; }           // אופציונלי (hex למשל)
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
