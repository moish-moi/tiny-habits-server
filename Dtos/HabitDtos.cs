namespace TinyHabits.Api.Dtos;

public record HabitCreateRequest(string Title, string? Color);
public record HabitResponse(int Id, string Title, string? Color, bool IsArchived, int Streak);
public record CheckinRequest(string? Date); // ISO "YYYY-MM-DD" (אם null -> היום)
public record CheckinResponse(int Id, string Date);
