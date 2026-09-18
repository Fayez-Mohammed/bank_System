using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

public class AiTranscriptionService : IAiTranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiTranscriptionService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public AiTranscriptionService(HttpClient httpClient, IConfiguration configuration, ILogger<AiTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new ArgumentNullException("Gemini:ApiKey is missing");
        _model = configuration["Gemini:Model"] ?? "gemini-3.6-flash";
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<AiTranscriptionResult> TranscribeAsync(
        Stream audioStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream, cancellationToken);
        var base64Audio = Convert.ToBase64String(memoryStream.ToArray());

        // تطبيع الـ contentType لو كان aac
        var mimeType = contentType.Equals("audio/aac", StringComparison.OrdinalIgnoreCase) ? "audio/aac" : contentType;

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = base64Audio
                            }
                        },
                        new
                        {
                         text = """
                                Extract all Saudi vehicle license plates mentioned in this audio recording accurately into structured data.

                                Critical Saudi Plate Rules:
                                1. Merge the Arabic letters and digits together without spaces into 'plate_number' (e.g., دطص6614, حطص8435, مكا77).
                                2. Letter Count: Standard Saudi plates strictly consist of 3 Arabic letters. Always expect and extract 3 letters (e.g., write حطص8435 and NOT حص8435, write بهه4220 and NOT به4220).
                                3. Number Count: Saudi plates have between 1 to 4 digits (e.g., 6614, 77, 1034).
                                4. Phonetic & Letter Clarification:
                                   - Distinguish 'د' from 'ف' and 'ر' from 'و' carefully (e.g., دقد8519 not فقد8519, رون8593 not رهن8593).
                                   - Accurately recognize repeated letters like 'ااا', 'ددو', 'دطط'.
                                5. Vehicle Categories: If the speaker mentions vehicle types common in Saudi transport such as 'نقل', 'راس', 'كوستر', 'دينا', 'تريلا', assign it strictly to 'vehicle_type'.
                                6. Contextual Notes: Any non-plate descriptors such as 'مركونة', 'في جراج', 'استريا', 'واقفة' must be placed strictly in 'notes'.
                                """
                        }
                    }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.0,
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        recording_duration_ms = new { type = "INTEGER" },
                        timestamp_reference = new { type = "STRING" },
                        cars = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    plate_number = new { type = "STRING" },
                                    timestamp_ms = new { type = "INTEGER" },
                                    timestamp_start_ms = new { type = "INTEGER" },
                                    timestamp_end_ms = new { type = "INTEGER" },
                                    vehicle_type = new { type = "STRING", nullable = true },
                                    notes = new { type = "STRING", nullable = true }
                                },
                                required = new[] { "plate_number", "timestamp_ms" }
                            }
                        }
                    },
                    required = new[] { "cars" }
                }
            }
        };

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API Error: {StatusCode} - {Body}", response.StatusCode, responseJson);
            response.EnsureSuccessStatusCode();
        }

        // استخراج النص من رد Gemini
        using var doc = JsonDocument.Parse(responseJson);
        var textPayload = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? throw new InvalidOperationException("Gemini returned an empty text payload.");

        var raw = JsonSerializer.Deserialize<AiRawResponse>(textPayload, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Gemini structured JSON.");

        var cars = raw.Cars
            .Select(c => new AiCarResult(c.PlateNumber, c.TimestampMs, c.TimestampStartMs, c.TimestampEndMs, c.VehicleType, c.Notes))
            .ToList();

        _logger.LogInformation(
            "Gemini transcription returned {Count} plates for {FileName} (recording duration {DurationMs}ms)",
            cars.Count, fileName, raw.RecordingDurationMs);

        return new AiTranscriptionResult(raw.RecordingDurationMs, raw.TimestampReference ?? "recording_start", cars);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record AiRawResponse(
        [property: JsonPropertyName("recording_duration_ms")] int RecordingDurationMs,
        [property: JsonPropertyName("timestamp_reference")] string? TimestampReference,
        [property: JsonPropertyName("cars")] List<AiRawCar> Cars);

    private record AiRawCar(
        [property: JsonPropertyName("plate_number")] string PlateNumber,
        [property: JsonPropertyName("timestamp_ms")] int TimestampMs,
        [property: JsonPropertyName("timestamp_start_ms")] int? TimestampStartMs,
        [property: JsonPropertyName("timestamp_end_ms")] int? TimestampEndMs,
        [property: JsonPropertyName("vehicle_type")] string? VehicleType,
        [property: JsonPropertyName("notes")] string? Notes);
}

//using System.Net.Http.Headers;
//using System.Text.Json;
//using System.Text.Json.Serialization;
//using Bank.Services.Interfaces;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.Logging;

//namespace Bank.Services.Implementations;


//public class AiTranscriptionService : IAiTranscriptionService
//{
//    private readonly HttpClient _httpClient;
//    private readonly ILogger<AiTranscriptionService> _logger;

//    public AiTranscriptionService(HttpClient httpClient, IConfiguration configuration, ILogger<AiTranscriptionService> logger)
//    {
//        _httpClient = httpClient;
//        _logger = logger;

//        var baseUrl = configuration["AiService:BaseUrl"] ?? "http://localhost:8000";
//        _httpClient.BaseAddress = new Uri(baseUrl);
//        _httpClient.Timeout = TimeSpan.FromMinutes(5);
//    }

//    public async Task<AiTranscriptionResult> TranscribeAsync(
//        Stream audioStream, string fileName, string contentType, CancellationToken cancellationToken = default)
//    {
//        using var content = new MultipartFormDataContent();
//        using var streamContent = new StreamContent(audioStream);
//        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
//        content.Add(streamContent, "file", fileName);

//        var response = await _httpClient.PostAsync("/api/v1/process", content, cancellationToken);
//        response.EnsureSuccessStatusCode();

//        var json = await response.Content.ReadAsStringAsync(cancellationToken);
//        var raw = JsonSerializer.Deserialize<AiRawResponse>(json, JsonOptions)
//            ?? throw new InvalidOperationException("AI service returned an empty/invalid response.");

//        var cars = raw.Cars
//            .Select(c => new AiCarResult(c.PlateNumber, c.TimestampMs, c.TimestampStartMs, c.TimestampEndMs, c.VehicleType, c.Notes))
//            .ToList();

//        _logger.LogInformation(
//            "AI transcription returned {Count} plates for {FileName} (recording duration {DurationMs}ms)",
//            cars.Count, fileName, raw.RecordingDurationMs);

//        return new AiTranscriptionResult(raw.RecordingDurationMs, raw.TimestampReference, cars);
//    }

//    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

//    private record AiRawResponse(
//        [property: JsonPropertyName("recording_duration_ms")] int RecordingDurationMs,
//        [property: JsonPropertyName("timestamp_reference")] string TimestampReference,
//        [property: JsonPropertyName("cars")] List<AiRawCar> Cars);

//    private record AiRawCar(
//        [property: JsonPropertyName("plate_number")] string PlateNumber,
//        [property: JsonPropertyName("timestamp_ms")] int TimestampMs,
//        [property: JsonPropertyName("timestamp_start_ms")] int? TimestampStartMs,
//        [property: JsonPropertyName("timestamp_end_ms")] int? TimestampEndMs,
//        [property: JsonPropertyName("vehicle_type")] string? VehicleType,
//        [property: JsonPropertyName("notes")] string? Notes);
//}
