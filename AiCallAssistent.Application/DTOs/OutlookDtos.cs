namespace AiCallAssistent.Application.DTOs;

public record OutlookEventDto(
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? BodyPreview);
