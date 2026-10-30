namespace EdgeTtsSharp;

public record Subtitle(int? Index, TimeSpan Start, TimeSpan End, string Content);
