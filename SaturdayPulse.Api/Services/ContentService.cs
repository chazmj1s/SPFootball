using System.Text.Json;
using SaturdayPulse.Contracts;
using SaturdayPulse.Core.Content;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Reads/writes the single ApplicationContent row, serializing to/from
    /// ApplicationContentDocument. Controller stays a thin HTTP wrapper, same
    /// split as every other *Service in this codebase.
    /// </summary>
    public class ContentService(IUnitOfWork uow, ILogger<ContentService> logger)
    {
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Returns the current content document, or a blank (version 0) document
        /// if nothing has ever been saved - lets both the mobile app and the
        /// admin console load successfully before the first-ever save.
        /// </summary>
        public async Task<ApplicationContentDocument> GetContentAsync(CancellationToken token = default)
        {
            var row = await uow.ApplicationContent.GetAsync(token);
            if (row == null) return new ApplicationContentDocument();

            return JsonSerializer.Deserialize<ApplicationContentDocument>(row.ContentJson, JsonOpts)
                ?? new ApplicationContentDocument();
        }

        /// <summary>
        /// Persists the given document as the new single row, incrementing
        /// Version regardless of whether anything actually changed - simple
        /// and correct is preferred here over diffing, per the design doc's
        /// "favor simplicity" intent.
        /// </summary>
        public async Task<ApplicationContentDocument> SaveContentAsync(
            ApplicationContentDocument document, CancellationToken token = default)
        {
            var existing = await uow.ApplicationContent.GetAsync(token);
            var newVersion = (existing?.Version ?? 0) + 1;
            document.Version = newVersion;

            var json = JsonSerializer.Serialize(document, JsonOpts);
            var now = DateTime.UtcNow;

            if (existing == null)
            {
                await uow.ApplicationContent.CreateAsync(new ApplicationContent
                {
                    Version = newVersion,
                    ContentJson = json,
                    LastModifiedUtc = now
                }, token);
            }
            else
            {
                await uow.ApplicationContent.UpdateAsync(newVersion, json, now, token);
            }

            await uow.SaveChangesAsync(token);
            logger.LogInformation("Application content saved, version {Version}", newVersion);

            return document;
        }
    }
}
