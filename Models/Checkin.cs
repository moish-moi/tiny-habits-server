namespace TinyHabits.Api.Models;

public class Checkin
{
    public int Id { get; set; }
    public int HabitId { get; set; }
    public DateOnly Date { get; set; }           // תאריך ללא שעה
}
