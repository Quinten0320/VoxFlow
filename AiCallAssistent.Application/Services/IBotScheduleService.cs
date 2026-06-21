namespace AiCallAssistent.Application.Services;

public interface IBotScheduleService
{
    /// <summary>
    /// Returns true if the bot is allowed to answer at this moment.
    /// Always returns true when no windows are configured.
    /// </summary>
    Task<bool> IsBotActiveNowAsync(short companyId, int? profileId);
}
