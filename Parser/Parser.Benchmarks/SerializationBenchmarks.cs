using BenchmarkDotNet.Attributes;
using Parser.Models;
using System.Text.Json;

namespace Parser.Benchmarks;

[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private readonly UserProfile _profile = new()
    {
        Id = 42,
        Username = "user",
        CreatedAt = DateTime.UtcNow
    };

    private readonly byte[] _jsonBytes;
    private readonly byte[] _binaryBytes;

    public SerializationBenchmarks()
    {
        _jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_profile);

        using var stream = new MemoryStream();
        _profile.SerializeToBinary(stream);
        _binaryBytes = stream.ToArray();
    }

    [Benchmark(Baseline = true)]
    public byte[] JsonSerialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_profile);
    }

    [Benchmark]
    public byte[] BinarySerialize()
    {
        using var stream = new MemoryStream();
        _profile.SerializeToBinary(stream);
        return stream.ToArray();
    }

    [Benchmark]
    public UserProfile? JsonDeserialize()
    {
        return JsonSerializer.Deserialize<UserProfile>(_jsonBytes);
    }

    [Benchmark]
    public UserProfile BinaryDeserialize()
    {
        using var stream = new MemoryStream(_binaryBytes);
        return UserProfile.DeserializeFromBinary(stream);
    }
}
