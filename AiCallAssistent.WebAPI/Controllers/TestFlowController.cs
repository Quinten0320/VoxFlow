using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.WebAPI.Controllers;

/// <summary>
/// Tests the full call flow without Twilio or Deepgram.
/// /run   — tests DB + service layer directly (no AI).
/// /gemini — sends a message to Gemini and lets it use function calling to handle the booking.
/// /voice  — sends a message to Gemini and returns both the text reply and synthesized ElevenLabs audio.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestFlowController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;
    private readonly IGeminiService _geminiService;
    private readonly IElevenLabsService _elevenLabsService;
    private readonly IAudioStore _audioStore;
    private readonly IConversationStore _conversations;
    private readonly AssistantSettings _assistant;
    private readonly AppDbContext _db;
    private readonly ILogger<TestFlowController> _logger;

    public TestFlowController(
        IAppointmentService appointmentService,
        IGeminiService geminiService,
        IElevenLabsService elevenLabsService,
        IAudioStore audioStore,
        IConversationStore conversations,
        IOptions<AssistantSettings> assistantSettings,
        AppDbContext db,
        ILogger<TestFlowController> logger)
    {
        _appointmentService = appointmentService;
        _geminiService = geminiService;
        _elevenLabsService = elevenLabsService;
        _audioStore = audioStore;
        _conversations = conversations;
        _assistant = assistantSettings.Value;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Mimics what happens when a real call is answered: seeds the conversation with
    /// appointment types and the welcome message, then returns a conversationId.
    /// Use that ID in subsequent /gemini or /voice calls to continue the conversation.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(StartConversationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartConversation([FromQuery] short companyId)
    {
        var conversationId = Guid.NewGuid().ToString();

        var companyName = "ons bedrijf";
        string? companyNameError = null;
        try
        {
            var name = await _db.Companies
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.CompanyName)
                .FirstOrDefaultAsync();
            if (name is { Length: > 0 }) companyName = name;
        }
        catch (Exception ex)
        {
            companyNameError = ex.ToString();
            _logger.LogWarning(ex, "StartConversation: failed to load company name for companyId {CompanyId}", companyId);
        }

        var welcomeText = _assistant.WelcomeMessage.Replace("{company}", companyName);

        List<AppointmentTypeDto> types = [];
        JsonNode? appointmentTypesNode = null;
        string? appointmentTypesError = null;
        try
        {
            types = await _appointmentService.GetAppointmentTypesAsync(companyId);
            appointmentTypesNode = JsonSerializer.SerializeToNode(types);
        }
        catch (Exception ex)
        {
            appointmentTypesError = ex.ToString();
            _logger.LogError(ex,
                "StartConversation: failed to load appointment types for companyId {CompanyId}", companyId);
        }

        _conversations.Initialize(conversationId,
            ConversationSeeder.BuildInitialTurns(welcomeText, appointmentTypesNode));

        return Ok(new StartConversationResponse
        {
            ConversationId = conversationId,
            WelcomeMessage = welcomeText,
            AppointmentTypesLoaded = appointmentTypesNode is not null,
            AppointmentTypes = types,
            AppointmentTypesError = appointmentTypesError,
            CompanyNameError = companyNameError
        });
    }

    /// <summary>
    /// Send a caller message to Gemini and watch it use function calling to book an appointment.
    /// Example message: "Ik wil graag een knipbeurt boeken voor morgen"
    /// </summary>
    [HttpPost("gemini")]
    [ProducesResponseType(typeof(GeminiTestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestGemini([FromBody] GeminiTestRequest request)
    {
        var context = new CallDispatchContext(request.CompanyId, null, null);
        var result = await _geminiService.RunConversationAsync(context, request.UserMessage, request.ConversationId);
        return Ok(result);
    }

    /// <summary>
    /// Tests the Gemini + ElevenLabs pipeline without Twilio or Deepgram.
    /// Sends a text message to Gemini, synthesizes the reply via ElevenLabs,
    /// and returns both the text response and a URL to stream the audio.
    /// GET the audioUrl in a browser or audio player to hear the response.
    /// </summary>
    [HttpPost("voice")]
    [ProducesResponseType(typeof(VoiceTestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestVoice([FromBody] VoiceTestRequest request)
    {
        try
        {
            var context = new CallDispatchContext(request.CompanyId, null, null);
            var geminiResult = await _geminiService.RunConversationAsync(
                context, request.UserMessage, request.ConversationId);

            if (!geminiResult.Success)
                return Ok(new VoiceTestResponse
                {
                    Success = false,
                    Error = $"Gemini failed: {geminiResult.Error}",
                    ConversationId = geminiResult.ConversationId,
                    FunctionCalls = geminiResult.FunctionCalls
                });

            byte[] audioBytes;
            try
            {
                audioBytes = await _elevenLabsService.SynthesizeAsync(geminiResult.FinalResponse);
            }
            catch (Exception ex)
            {
                return Ok(new VoiceTestResponse
                {
                    Success = false,
                    Error = $"ElevenLabs synthesis failed: {ex.Message}",
                    TextResponse = geminiResult.FinalResponse,
                    ConversationId = geminiResult.ConversationId,
                    FunctionCalls = geminiResult.FunctionCalls
                });
            }

            var audioId = _audioStore.Store(audioBytes, "audio/mpeg");

            return Ok(new VoiceTestResponse
            {
                Success = true,
                TextResponse = geminiResult.FinalResponse,
                AudioUrl = $"/api/twilio/audio/{audioId}",
                ConversationId = geminiResult.ConversationId,
                FunctionCalls = geminiResult.FunctionCalls
            });
        }
        catch (Exception ex)
        {
            return Ok(new VoiceTestResponse
            {
                Success = false,
                Error = $"Unexpected error: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Runs the full booking flow end-to-end and returns each step's result.
    /// Creates a real appointment — delete it from Supabase after testing.
    /// </summary>
    [HttpPost("run")]
    [ProducesResponseType(typeof(TestFlowResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunFlow([FromBody] TestFlowRequest request)
    {
        var response = new TestFlowResponse();

        // Step 1: get appointment types
        try
        {
            var types = await _appointmentService.GetAppointmentTypesAsync(request.CompanyId);
            response.Step1_AppointmentTypes = new FlowStep<List<AppointmentTypeDto>>
            {
                Success = true,
                Result = types
            };
        }
        catch (Exception ex)
        {
            response.Step1_AppointmentTypes = new FlowStep<List<AppointmentTypeDto>>
            {
                Success = false,
                Error = ex.Message
            };
            response.Status = "Failed at Step 1 (appointment types). Check DB connection and appointment_types table.";
            return Ok(response);
        }

        // Step 2: find soonest available slot
        SoonestAvailableResponse? soonest;
        try
        {
            soonest = await _appointmentService.GetSoonestAvailableAsync(request.CompanyId, request.Type);
            response.Step2_SoonestAvailable = new FlowStep<SoonestAvailableResponse>
            {
                Success = true,
                Result = soonest
            };
        }
        catch (Exception ex)
        {
            response.Step2_SoonestAvailable = new FlowStep<SoonestAvailableResponse>
            {
                Success = false,
                Error = ex.Message
            };
            response.Status = "Failed at Step 2 (soonest available). Check appointment type name and business hours config.";
            return Ok(response);
        }

        if (soonest.SoonestSlot == null)
        {
            response.Status = "Step 2 succeeded but no slots available in the next 60 days. Add more employees or check business hours.";
            return Ok(response);
        }

        // Step 3: create appointment at the soonest slot
        try
        {
            var createRequest = new CreateAppointmentRequest
            {
                CompanyId = request.CompanyId,
                EmployeeId = soonest.SoonestSlot.EmployeeId,
                Type = request.Type,
                StartTime = soonest.SoonestSlot.StartTime,
                Description = request.Description
            };

            var created = await _appointmentService.CreateAppointmentAsync(createRequest);
            response.Step3_CreatedAppointment = new FlowStep<AppointmentResponse>
            {
                Success = true,
                Result = created
            };
        }
        catch (Exception ex)
        {
            response.Step3_CreatedAppointment = new FlowStep<AppointmentResponse>
            {
                Success = false,
                Error = ex.Message
            };
            response.Status = "Failed at Step 3 (create appointment). Slot may have been taken between Step 2 and Step 3.";
            return Ok(response);
        }

        response.Status = "All steps succeeded. Remember to delete the test appointment from Supabase.";
        return Ok(response);
    }
}
