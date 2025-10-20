namespace TinyHabits.Api.Dtos;

public record AuthResponse(bool Success, string Message, string? Token = null);
