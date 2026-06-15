using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/knowledge-suggestions")]
public class KnowledgeSuggestionsController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var items = await Db.KnowledgeSuggestions
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new KnowledgeSuggestionDto(s.Id, s.Text, s.Status, s.CreatedAt))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateKnowledgeSuggestionRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.KnowledgeSuggestions
            .Where(s => s.Id == id && s.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, request.Status));

        if (rows == 0) return NotFound();
        return NoContent();
    }
}
