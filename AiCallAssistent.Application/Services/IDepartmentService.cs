using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

public interface IDepartmentService
{
    Task<List<DepartmentDto>> GetDepartmentsAsync(short companyId);
}
