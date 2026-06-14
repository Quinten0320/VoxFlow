using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.Infrastructure.Services;

public class DepartmentService(AppDbContext db) : IDepartmentService
{
    public Task<List<DepartmentDto>> GetDepartmentsAsync(short companyId)
        => db.CompanyDepartments
            .Where(d => d.CompanyId == companyId && d.IsActive)
            .OrderBy(d => d.DisplayName)
            .Select(d => new DepartmentDto { Name = d.Name, DisplayName = d.DisplayName })
            .ToListAsync();
}
