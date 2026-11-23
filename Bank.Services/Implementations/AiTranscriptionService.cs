using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

public class AiTranscriptionService : IAiTranscriptionService
{
    private const int MaxVehicleTypeLength = 100;
    private const int MaxNotesLength = 255;

    private const string SystemPrompt = """
You are an expert Arabic speech-to-data extractor for Saudi vehicle license plates recorded in the field (often spoken in Egyptian or Gulf Arabic accent).
Listen carefully to the entire audio from start to finish and extract EVERY vehicle license plate into the JSON schema.

=== 1. PLATE NUMBER RULES (`plate_number`) ===
- Strict Format: MUST be EXACTLY 3 Arabic letters followed by EXACTLY 4 Western digits (0-9) with NO spaces (e.g., "دطص6614", "بصه8611", "حاه5719"). NEVER output 2 or 4 letters, and NEVER output 3 digits.
- ALLOWED ARABIC LETTERS ONLY (17 letters total):
  Every letter in `plate_number` MUST be chosen EXCLUSIVELY from this exact set of 17 letters:
  [ ا , ب , ح , د , ر , س , ص , ط , ع , ق , ك , ل , م , ن , ه , و , ي ]
- FORBIDDEN / EXCLUDED LETTERS (NEVER EXIST ON ANY PLATE):
  NEVER output any of these letters in `plate_number` under ANY circumstances:
  [ ج , خ , غ , ف , ث , ض , ت , ش , ز , ذ , ظ , ء ]
  Also NEVER output hamza forms (أ، إ، آ -> always use 'ا') or taa marbuta (ة -> always use 'ه') or alif maqsura (ى -> always use 'ي').
  If a spoken letter sounds similar to a forbidden letter, resolve it to the closest phonetically similar ALLOWED letter (e.g., ج/خ -> ح, غ -> ع, ش -> س, ض -> ص or د, ظ -> ط, ذ/ز -> د or ر, ت/ث -> ب or ط or س, ف -> ق or د).
- Spoken Letter Names: Convert spoken letter names to a SINGLE allowed Arabic letter (e.g., "ألف" -> "ا", "ب/باء" -> "ب", "حاء" -> "ح", "دال" -> "د", "راء" -> "ر", "سين" -> "س", "صاد" -> "ص", "طاء" -> "ط", "عين" -> "ع", "قاف" -> "ق", "كاف" -> "ك", "لام" -> "ل", "ميم" -> "م", "نون" -> "ن", "هاء" -> "ه", "واو" -> "و", "ياء/يه" -> "ي"). Never write words like "الفهم" or "ركعين"; write "اهم" or "ركع".
- Spoken Numbers: Convert ALL digits to Western digits (0-9), including "زيرو/صفر" -> "0", and compound spoken numbers like "حداشر" -> "11", "عشرين" -> "20".
- Phonetic Clarity: Distinguish 'د' vs 'ق', 'ر' vs 'و', 'ب' vs 'ن', 'س' vs 'ص', and accurately recognize repeated letters like 'ااا', 'ددو', 'ببك'.
- Ignore Fillers: Ignore hesitations ("آآآ") and filler/transition phrases like "رقم", "لوحة" (alone), "مكمل في الموقع", "نكمل". NEVER convert the word "رقم" into the letters 'رقم'.

=== 2. SELF-CORRECTIONS ("تعديل") ===
- If the speaker makes a mistake and says "تعديل", "غلط", "لا", or "تصحيح" and repeats the plate or digits (e.g., "ح م و، تعديل ح و م 1488" or "أ ع ر 944، تعديل أ ع ر 9466 محروقة"):
  Output ONLY ONE entry with the FINAL corrected plate number ("حوم1488", "اعر9466"). Discard the aborted/wrong attempt completely.

=== 3. VEHICLE TYPE (`vehicle_type`) ===
Extract the vehicle category, model, or commercial plate type if spoken for the plate (or null if none).
Common values (recognize spoken/dialect variants and similar vehicle types):
- Pickup / Trucks / Vans / Buses: وانيت (may sound like "ونيد"/"ونيت"), شاص, فان, H1 (spoken "اتش وان"), هاي اس (هايس), ستاريا, ستاركس (استراكس/ستار), دوكر, دباب, نقل, نقل كبير, قلاب, دينا, راس, تريلا, سطحة, مصندق, باص, اتوبيس, كوستر.
- Cars / Plate Types: صني, ديزاير, بيجاس, ريو, اي تن (i10), تاكسي, لوحة صفراء (spoken "لوحة صفرا").
- If multiple vehicle descriptors are spoken for the same plate (e.g., "لوحة صفرا صني"), keep both in `vehicle_type` (e.g., "لوحة صفراء - صني").

=== 4. CONTEXTUAL NOTES (`notes`) — CRITICAL, DO NOT SKIP ===
ALWAYS capture any location, parking spot, direction, or vehicle condition spoken for a plate into `notes` (return null ONLY if truly no note and no active zone applies).
A single plate frequently has BOTH `vehicle_type` AND `notes` (e.g., "شاص برحة يسار" -> vehicle_type: "شاص", notes: "برحة يسار"; "وانيت متحرك" -> vehicle_type: "وانيت", notes: "متحرك"; "اتش وان مركون" -> vehicle_type: "H1", notes: "مركون").

Common single-plate notes (and similar phrases):
- Locations / Spots: برحة يمين, برحة يسار, برحة 1 يمين, برحة 1 يسار, برحة يمين اول الموقع, برحة يمين اخر الموقع, برحة يسار اول الموقع, برحة يسار اخر الموقع, جراج يمين, جراج يسار, جراج 1 يمين, جراج 1 يسار, جراج يمين اول الموقع, جراج يمين اخر الموقع, جراج يسار اول الموقع, جراج يسار اخر الموقع, جراج منزل, جراج منزل يمين, جراج منزل يسار, امام فندق, داخل محطة.
- Vehicle States / Conditions: مركون, مركونة, متحرك, متحركة, متغطية, مترب, متربة, حادث, مصدومة, محروقة, واقفة.

=== 5. ACTIVE ZONE / RANGE NOTES ("بداية ... انتهت") ===
Maintain an `active_zone_note` state chronologically as you listen:
- START: Whenever the speaker says "بداية <place/note>" (e.g., "بداية برحة", "بداية برحة شمال", "بداية جراج يمين", "بداية جراج 1"), set `active_zone_note` to "<place/note>" (without the word "بداية", e.g., "برحة", "برحة شمال", "جراج يمين").
- APPLY: While `active_zone_note` is active, EVERY subsequent plate MUST include `active_zone_note` in its `notes` field — even if the speaker only says the plate and vehicle type without repeating the place.
  - If a plate inside the zone ALSO has its own note (e.g., "متغطية"), combine them: "<active_zone_note> - <plate_note>" (e.g., "برحة شمال - متغطية").
- END: When the speaker says "انتهت", "انتهت البرحة", "نهاية برحة", "نهاية الجراج", "خلصت البرحة", "انتهى", or any equivalent closing phrase, CLEAR `active_zone_note` immediately. Subsequent plates do NOT receive the zone note. Do NOT put transition phrases like "مكمل في الموقع" into `notes`.

=== 6. EXAMPLES ===
- Spoken: "ح أ هـ 5 7 1 9 لوحة صفرا برحة يمين"
  -> plate_number: "حاه5719", vehicle_type: "لوحة صفراء", notes: "برحة يمين"
- Spoken: "ر و ط 8 زيرو 3 زيرو شاص برحة يسار"
  -> plate_number: "روط8030", vehicle_type: "شاص", notes: "برحة يسار"
- Spoken: "ح م و، تعديل ح و م 1 4 8 8"
  -> plate_number: "حوم1488", vehicle_type: null, notes: null
- Spoken: "بداية برحة شمال ... د ص م 7 3 1 7 شاص ... ب س ع 3 6 8 6 فان متغطية ... انتهت البرحة مكمل في الموقع ... ر ص ي 7 0 4 6"
  -> "دصم7317": vehicle_type: "شاص", notes: "برحة شمال"
  -> "بسع3686": vehicle_type: "فان", notes: "برحة شمال - متغطية"
  -> "رصي7046": vehicle_type: null, notes: null
- Spoken: "د ن ب 7 6 7 1 وانيت متحرك"
  -> plate_number: "دنب7671", vehicle_type: "وانيت", notes: "متحرك"
""";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiTranscriptionService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public AiTranscriptionService(HttpClient httpClient, IConfiguration configuration, ILogger<AiTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new ArgumentNullException("Gemini:ApiKey is missing in configuration");
        _model = configuration["Gemini:Model"] ?? "gemini-3.5-flash";
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<AiTranscriptionResult> TranscribeAsync(
        Stream audioStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream, cancellationToken);
        var base64Audio = Convert.ToBase64String(memoryStream.ToArray());

        var mimeType = ResolveMimeType(fileName, contentType);

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = SystemPrompt }
                }
            },
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
                            text = "Transcribe and extract all license plates, vehicle types, and contextual/zone notes from this audio recording strictly following the system instructions."
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
                        recording_duration_ms = new
                        {
                            type = "INTEGER",
                            description = "Total audio duration in milliseconds."
                        },
                        timestamp_reference = new
                        {
                            type = "STRING",
                            description = "Always 'recording_start'."
                        },
                        cars = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    plate_number = new
                                    {
                                        type = "STRING",
                                        description = "Exactly 3 allowed Arabic letters ONLY from [ا،ب،ح،د،ر،س،ص،ط،ع،ق،ك،ل،م،ن،ه،و،ي] (NEVER ج،خ،غ،ف،ث،ض،ت،ش،ز،ذ،ظ،ء) followed by 4 Western digits (0-9) with no spaces."
                                    },
                                    timestamp_ms = new
                                    {
                                        type = "INTEGER",
                                        description = "Approximate timestamp in milliseconds from audio start."
                                    },
                                    timestamp_start_ms = new { type = "INTEGER", nullable = true },
                                    timestamp_end_ms = new { type = "INTEGER", nullable = true },
                                    vehicle_type = new
                                    {
                                        type = "STRING",
                                        nullable = true,
                                        description = "Vehicle category, model, or plate type if spoken (e.g., فان, شاص, وانيت, H1, صني, لوحة صفراء, سطحة). Null if not mentioned."
                                    },
                                    notes = new
                                    {
                                        type = "STRING",
                                        nullable = true,
                                        description = "Location/zone note (including active 'بداية ... انتهت' zone notes like برحة, برحة شمال, جراج يمين) and/or vehicle condition (مركون, متحرك, متغطية, متربة, حادث, مصدومة, محروقة). Null only if neither a zone note nor a plate note applies."
                                    }
                                },
                                required = new[] { "plate_number", "timestamp_ms", "vehicle_type", "notes" }
                            }
                        }
                    },
                    required = new[] { "cars" }
                }
            }
        };

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API Error ({Model}): {StatusCode} - {Body}", _model, response.StatusCode, responseJson);
            response.EnsureSuccessStatusCode();
        }

        var textPayload = ExtractTextPayload(responseJson);

        var raw = JsonSerializer.Deserialize<AiRawResponse>(textPayload, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Gemini structured JSON.");

        var cars = (raw.Cars ?? new List<AiRawCar>())
            .Select(c => new AiCarResult(
                SanitizePlateNumber(c.PlateNumber),
                c.TimestampMs,
                c.TimestampStartMs,
                c.TimestampEndMs,
                CleanOptionalText(c.VehicleType, MaxVehicleTypeLength),
                CleanOptionalText(c.Notes, MaxNotesLength)))
            .Where(c => !string.IsNullOrWhiteSpace(c.PlateNumber))
            .ToList();

        var durationMs = raw.RecordingDurationMs ?? 0;

        _logger.LogInformation(
            "Gemini ({Model}) transcription returned {Count} plates ({WithType} with type, {WithNotes} with notes) for {FileName} (duration {DurationMs}ms)",
            _model, cars.Count, cars.Count(c => c.VehicleType != null), cars.Count(c => c.Notes != null), fileName, durationMs);

        return new AiTranscriptionResult(durationMs, raw.TimestampReference ?? "recording_start", cars);
    }

    private string ExtractTextPayload(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts) &&
            parts.ValueKind == JsonValueKind.Array &&
            parts.GetArrayLength() > 0 &&
            parts[0].TryGetProperty("text", out var textElement))
        {
            var payload = textElement.GetString();
            if (!string.IsNullOrWhiteSpace(payload))
                return payload;
        }

        _logger.LogError("Gemini ({Model}) returned an unexpected/empty response: {Body}", _model, responseJson);
        throw new InvalidOperationException("Gemini returned an empty or blocked response.");
    }

    private static string ResolveMimeType(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return contentType.Equals("audio/aac", StringComparison.OrdinalIgnoreCase) ? "audio/aac" : contentType;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".mp4" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".webm" => "audio/webm",
            _ => "audio/aac"
        };
    }

    private static string SanitizePlateNumber(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return string.Empty;

        var sb = new StringBuilder(plate.Length);
        foreach (var ch in plate)
        {
            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '\u0640') continue;

            sb.Append(ch switch
            {
                >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' or 'ئ' => 'ي',
                'ؤ' => 'و',
                _ => ch
            });
        }
        return sb.ToString();
    }

    private static string? CleanOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (trimmed.Equals("null", StringComparison.OrdinalIgnoreCase) || trimmed == "-")
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private record AiRawResponse(
        [property: JsonPropertyName("recording_duration_ms")] int? RecordingDurationMs,
        [property: JsonPropertyName("timestamp_reference")] string? TimestampReference,
        [property: JsonPropertyName("cars")] List<AiRawCar>? Cars);

    private record AiRawCar(
        [property: JsonPropertyName("plate_number")] string PlateNumber,
        [property: JsonPropertyName("timestamp_ms")] int TimestampMs,
        [property: JsonPropertyName("timestamp_start_ms")] int? TimestampStartMs,
        [property: JsonPropertyName("timestamp_end_ms")] int? TimestampEndMs,
        [property: JsonPropertyName("vehicle_type")] string? VehicleType,
        [property: JsonPropertyName("notes")] string? Notes);
}

/////////////////////////////////////////////////////////////////////////////////


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
//        // Plate AI server key (sent as X-API-Key); empty = AI server has no key
//        var apiKey = configuration["AiService:ApiKey"];
//        if (!string.IsNullOrWhiteSpace(apiKey))
//            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
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

///////////
////using System.Net.Http.Headers;
////using System.Text;
////using System.Text.Json;
////using System.Text.Json.Serialization;
////using Bank.Services.Interfaces;
////using Microsoft.Extensions.Configuration;
////using Microsoft.Extensions.Logging;

////namespace Bank.Services.Implementations;

////public class AiTranscriptionService : IAiTranscriptionService
////{
////    private readonly HttpClient _httpClient;
////    private readonly ILogger<AiTranscriptionService> _logger;
////    private readonly string _apiKey;
////    private readonly string _model;

////    public AiTranscriptionService(HttpClient httpClient, IConfiguration configuration, ILogger<AiTranscriptionService> logger)
////    {
////        _httpClient = httpClient;
////        _logger = logger;
////        _apiKey = configuration["Gemini:ApiKey"] ?? throw new ArgumentNullException("Gemini:ApiKey is missing");
////        _model = configuration["Gemini:Model"] ?? "gemini-3.6-flash";
////        _httpClient.Timeout = TimeSpan.FromMinutes(5);
////    }

////    public async Task<AiTranscriptionResult> TranscribeAsync(
////        Stream audioStream, string fileName, string contentType, CancellationToken cancellationToken = default)
////    {
////        using var memoryStream = new MemoryStream();
////        await audioStream.CopyToAsync(memoryStream, cancellationToken);
////        var base64Audio = Convert.ToBase64String(memoryStream.ToArray());

////        // تطبيع الـ contentType لو كان aac
////        var mimeType = contentType.Equals("audio/aac", StringComparison.OrdinalIgnoreCase) ? "audio/aac" : contentType;

////        var requestBody = new
////        {
////            contents = new[]
////            {
////                new
////                {
////                    parts = new object[]
////                    {
////                        new
////                        {
////                            inline_data = new
////                            {
////                                mime_type = mimeType,
////                                data = base64Audio
////                            }
////                        },
////                        new
////                        {
////                         text = """
////                                Extract all Saudi vehicle license plates mentioned in this audio recording accurately into structured data.

////                                Critical Saudi Plate Rules:
////                                1. Merge the Arabic letters and digits together without spaces into 'plate_number' (e.g., دطص6614, حطص8435, مكا77).
////                                2. Letter Count: Standard Saudi plates strictly consist of 3 Arabic letters. Always expect and extract 3 letters (e.g., write حطص8435 and NOT حص8435, write بهه4220 and NOT به4220).
////                                3. Number Count: Saudi plates have between 1 to 4 digits (e.g., 6614, 77, 1034).
////                                4. Phonetic & Letter Clarification:
////                                   - Distinguish 'د' from 'ف' and 'ر' from 'و' carefully (e.g., دقد8519 not فقد8519, رون8593 not رهن8593).
////                                   - Accurately recognize repeated letters like 'ااا', 'ددو', 'دطط'.
////                                5. Vehicle Categories: If the speaker mentions vehicle types common in Saudi transport such as 'نقل', 'راس', 'كوستر', 'دينا', 'تريلا', assign it strictly to 'vehicle_type'.
////                                6. Contextual Notes: Any non-plate descriptors such as 'مركونة', 'في جراج', 'استريا', 'واقفة' must be placed strictly in 'notes'.
////                                """
////                        }
////                    }
////                }
////            },
////            generationConfig = new
////            {
////                response_mime_type = "application/json",
////                temperature = 0.0,
////                response_schema = new
////                {
////                    type = "OBJECT",
////                    properties = new
////                    {
////                        recording_duration_ms = new { type = "INTEGER" },
////                        timestamp_reference = new { type = "STRING" },
////                        cars = new
////                        {
////                            type = "ARRAY",
////                            items = new
////                            {
////                                type = "OBJECT",
////                                properties = new
////                                {
////                                    plate_number = new { type = "STRING" },
////                                    timestamp_ms = new { type = "INTEGER" },
////                                    timestamp_start_ms = new { type = "INTEGER" },
////                                    timestamp_end_ms = new { type = "INTEGER" },
////                                    vehicle_type = new { type = "STRING", nullable = true },
////                                    notes = new { type = "STRING", nullable = true }
////                                },
////                                required = new[] { "plate_number", "timestamp_ms" }
////                            }
////                        }
////                    },
////                    required = new[] { "cars" }
////                }
////            }
////        };

////        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
////        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

////        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
////        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

////        if (!response.IsSuccessStatusCode)
////        {
////            _logger.LogError("Gemini API Error: {StatusCode} - {Body}", response.StatusCode, responseJson);
////            response.EnsureSuccessStatusCode();
////        }

////        // استخراج النص من رد Gemini
////        using var doc = JsonDocument.Parse(responseJson);
////        var textPayload = doc.RootElement
////            .GetProperty("candidates")[0]
////            .GetProperty("content")
////            .GetProperty("parts")[0]
////            .GetProperty("text")
////            .GetString() ?? throw new InvalidOperationException("Gemini returned an empty text payload.");

////        var raw = JsonSerializer.Deserialize<AiRawResponse>(textPayload, JsonOptions)
////            ?? throw new InvalidOperationException("Failed to deserialize Gemini structured JSON.");

////        var cars = raw.Cars
////            .Select(c => new AiCarResult(c.PlateNumber, c.TimestampMs, c.TimestampStartMs, c.TimestampEndMs, c.VehicleType, c.Notes))
////            .ToList();

////        _logger.LogInformation(
////            "Gemini transcription returned {Count} plates for {FileName} (recording duration {DurationMs}ms)",
////            cars.Count, fileName, raw.RecordingDurationMs);

////        return new AiTranscriptionResult(raw.RecordingDurationMs, raw.TimestampReference ?? "recording_start", cars);
////    }

////    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

////    private record AiRawResponse(
////        [property: JsonPropertyName("recording_duration_ms")] int RecordingDurationMs,
////        [property: JsonPropertyName("timestamp_reference")] string? TimestampReference,
////        [property: JsonPropertyName("cars")] List<AiRawCar> Cars);

////    private record AiRawCar(
////        [property: JsonPropertyName("plate_number")] string PlateNumber,
////        [property: JsonPropertyName("timestamp_ms")] int TimestampMs,
////        [property: JsonPropertyName("timestamp_start_ms")] int? TimestampStartMs,
////        [property: JsonPropertyName("timestamp_end_ms")] int? TimestampEndMs,
////        [property: JsonPropertyName("vehicle_type")] string? VehicleType,
////        [property: JsonPropertyName("notes")] string? Notes);
////}



///////////////////////////////////////////////////////////////////////////////////


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
//        // Plate AI server key (sent as X-API-Key); empty = AI server has no key
//        var apiKey = configuration["AiService:ApiKey"];
//        if (!string.IsNullOrWhiteSpace(apiKey))
//            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
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
