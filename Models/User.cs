namespace TinyHabits.Api.Models;

public class User
{
    public int Id { get; set; }                // מזהה ייחודי (בינתיים נגדיל ידנית)
    public string Email { get; set; } = "";    // מייל ייחודי
    public string PasswordHash { get; set; } = ""; // האש של הסיסמה (לא טקסט רגיל)
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
