using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCallAssistent.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    /// <summary>
    /// Create a new appointment.
    /// If no employeeId is provided, an available employee will be auto-assigned.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequest request)
    {
        try
        {
            var result = await _appointmentService.CreateAppointmentAsync(request);
            return Created($"/api/appointment/{result.AppointmentId}", result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Check available time slots for a given company, appointment type, and date.
    /// </summary>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(AvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckAvailability(
        [FromQuery] short companyId,
        [FromQuery] string type,
        [FromQuery] DateOnly date)
    {
        try
        {
            var result = await _appointmentService.GetAvailabilityAsync(companyId, type, date);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Find the soonest available time slot for a given company and appointment type.
    /// Searches up to 60 days ahead.
    /// </summary>
    [HttpGet("soonest")]
    [ProducesResponseType(typeof(SoonestAvailableResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSoonestAvailable(
        [FromQuery] short companyId,
        [FromQuery] string type)
    {
        try
        {
            var result = await _appointmentService.GetSoonestAvailableAsync(companyId, type);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
