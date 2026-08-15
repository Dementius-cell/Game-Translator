using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslator.Application.Ocr;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Usage: dotnet run --project tools/ComicGeometryCandidateDetectorBenchmark -- --input <detector-input.json> --output <report.json> [--force]");
        return 0;
    }

    try
    {
        var options = ParseOptions(args);
        var inputPath = Path.GetFullPath(options.InputPath);
        var outputPath = Path.GetFullPath(options.OutputPath);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Detector benchmark input was not found.", inputPath);
        }

        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Input and output paths must be different.");
        }

        if (File.Exists(outputPath) && !options.OverwriteOutput)
        {
            throw new IOException("Benchmark report already exists. Pass --force to overwrite it.");
        }

        await using var inputStream = File.OpenRead(inputPath);
        var serializerOptions = CreateSerializerOptions();
        var input = await JsonSerializer.DeserializeAsync<ComicGeometryCandidateDetectorBenchmarkInput>(
            inputStream,
            serializerOptions);
        if (input is null)
        {
            throw new InvalidDataException("Detector benchmark input must contain a JSON object.");
        }

        var report = new ComicGeometryCandidateDetectorBenchmarkAssembler().Build(input);
        var validation = new ComicGeometryCandidateDetectorBenchmarkValidator().Validate(report);
        var parentDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await using var outputStream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(
            outputStream,
            new ComicGeometryCandidateDetectorBenchmarkOutput(report, validation.Passed, validation.Errors),
            serializerOptions);

        if (!validation.Passed)
        {
            Console.Error.WriteLine($"Benchmark report was written but failed ADR-023 validation: {outputPath}");
            return 2;
        }

        Console.WriteLine($"Validated research-only benchmark report: {outputPath}");
        return 0;
    }
    catch (Exception exception) when (exception is ArgumentException
        or IOException
        or InvalidDataException
        or JsonException)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

static ComicGeometryCandidateDetectorBenchmarkCommandOptions ParseOptions(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    var overwriteOutput = false;

    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--input":
                inputPath = ReadOptionValue(args, ref index, "--input");
                break;
            case "--output":
                outputPath = ReadOptionValue(args, ref index, "--output");
                break;
            case "--force":
                overwriteOutput = true;
                break;
            default:
                throw new ArgumentException($"Unknown argument: {args[index]}");
        }
    }

    if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
    {
        throw new ArgumentException("Both --input and --output are required. Pass --help for usage.");
    }

    return new ComicGeometryCandidateDetectorBenchmarkCommandOptions(inputPath, outputPath, overwriteOutput);
}

static string ReadOptionValue(string[] args, ref int index, string optionName)
{
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"{optionName} requires a path.");
    }

    index++;
    return args[index];
}

static JsonSerializerOptions CreateSerializerOptions()
{
    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    options.Converters.Add(new BoundingBoxJsonConverter());
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
}

internal sealed record ComicGeometryCandidateDetectorBenchmarkCommandOptions(
    string InputPath,
    string OutputPath,
    bool OverwriteOutput);

internal sealed record ComicGeometryCandidateDetectorBenchmarkOutput(
    ComicGeometryCandidateDetectorBenchmarkReport Report,
    bool PassedAdr023Validation,
    IReadOnlyList<string> ValidationErrors);

internal sealed class BoundingBoxJsonConverter : JsonConverter<BoundingBox>
{
    public override BoundingBox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Bounding box must be a JSON object.");
        }

        int? x = null;
        int? y = null;
        int? width = null;
        int? height = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Bounding box property name is required.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Bounding box property must be an integer.");
            }

            var value = reader.GetInt32();
            if (string.Equals(propertyName, "x", StringComparison.OrdinalIgnoreCase))
            {
                x = value;
            }
            else if (string.Equals(propertyName, "y", StringComparison.OrdinalIgnoreCase))
            {
                y = value;
            }
            else if (string.Equals(propertyName, "width", StringComparison.OrdinalIgnoreCase))
            {
                width = value;
            }
            else if (string.Equals(propertyName, "height", StringComparison.OrdinalIgnoreCase))
            {
                height = value;
            }
        }

        if (x is null || y is null || width is null || height is null)
        {
            throw new JsonException("Bounding box requires x, y, width, and height.");
        }

        return new BoundingBox(x.Value, y.Value, width.Value, height.Value);
    }

    public override void Write(Utf8JsonWriter writer, BoundingBox value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }
}
