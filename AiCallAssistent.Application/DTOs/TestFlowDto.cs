namespace AiCallAssistent.Application.DTOs;

public class StartConversationResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public string WelcomeMessage { get; set; } = string.Empty;
    public bool AppointmentTypesLoaded { get; set; }
    public List<AppointmentTypeDto> AppointmentTypes { get; set; } = [];
    public string? AppointmentTypesError { get; set; }
    public string? CompanyNameError { get; set; }
}

public class TestFlowRequest
{
    public short CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = "Test appointment via flow test";
}

public class TestFlowResponse
{
    public string Status { get; set; } = string.Empty;
    public FlowStep<List<AppointmentTypeDto>> Step1_AppointmentTypes { get; set; } = new();
    public FlowStep<SoonestAvailableResponse> Step2_SoonestAvailable { get; set; } = new();
    public FlowStep<AppointmentResponse> Step3_CreatedAppointment { get; set; } = new();
}

public class FlowStep<T>
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public T? Result { get; set; }
}
