using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/feedback")]
public class FeedbackController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitFeedbackRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Message))
            return BadRequest(new { error = "Bericht mag niet leeg zijn." });

        var (companyId, err) = await GetCompanyIdAsync();
        if (err != null) return err;

        var feedback = new AssistantFeedback
        {
            CompanyId = companyId,
            Message   = body.Message.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Db.AssistantFeedbacks.Add(feedback);
        await Db.SaveChangesAsync();

        return Ok(new { submitted = true });
    }
}

public record SubmitFeedbackRequest(string Message);
