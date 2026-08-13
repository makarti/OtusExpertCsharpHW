using Parser.SourceGenerators;

namespace Parser.Models;

[GenerateBinarySerializer]
public partial class UserProfile
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
