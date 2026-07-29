namespace Parser.Models;

public class UserProfile
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
