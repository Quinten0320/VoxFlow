using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

public interface IAppointmentService
{
    Task<List<AppointmentTypeDto>> GetAppointmentTypesAsync(short companyId);
    Task<AppointmentResponse> CreateAppointmentAsync(CreateAppointmentRequest request);
    Task<AvailabilityResponse> GetAvailabilityAsync(short companyId, string type, DateOnly date);
    Task<SoonestAvailableResponse> GetSoonestAvailableAsync(short companyId, string type);

    /// <summary>Returns true if the appointment was found and deleted.</summary>
    Task<bool> CancelAppointmentAsync(long appointmentId, short companyId);
}
