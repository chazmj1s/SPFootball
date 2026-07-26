namespace SaturdayPulse.AdminBlazor.Services;

/// <summary>
/// Thrown by AdminApiService when a call returns a non-success status.
/// Message is the API response's "message" field when present, otherwise
/// falls back to "check API logs" - mirrors `err?.error?.message ?? 'check API logs'`
/// from the Angular service's step-runner pattern.
/// </summary>
public class AdminApiException(string message) : Exception(message);
