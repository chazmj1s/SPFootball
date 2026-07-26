namespace SaturdayPulse.AdminBlazor.Services.Models;

public enum LogStatus { Info, Success, Error, Running }

public record LogEntry(string Time, string Message, LogStatus Status);
