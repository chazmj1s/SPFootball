namespace SaturdayPulse.AdminBlazor.Services.Models;

public record AdminUserSummaryDto(
    string UserId,
    string Handle,
    string? Email,
    bool IsAdmin,
    List<AdminEntitlementDto> Entitlements);

public record AdminEntitlementDto(
    string ProductKey,
    string Source,
    DateTime? ExpiryDate,
    int? PassYear,
    bool IsActive);
