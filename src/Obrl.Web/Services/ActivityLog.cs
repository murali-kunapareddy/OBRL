using System.Text.Json;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Services;

/// <summary>Writes rows to the append-only activity history.</summary>
public sealed class ActivityLog(ObrlDbContext db, IHttpContextAccessor http)
{
    /// <summary>Adds an event and saves it immediately.</summary>
    public async Task WriteAsync(string type, string? userId, Guid? bookId = null, object? data = null, CancellationToken ct = default)
    {
        Add(type, userId, bookId, data);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds an event to the current unit of work; it is saved with the caller's next SaveChanges.</summary>
    public void Add(string type, string? userId, Guid? bookId = null, object? data = null)
    {
        db.ActivityEvents.Add(new ActivityEvent
        {
            Type = type,
            UserId = userId,
            BookId = bookId,
            Data = data is null ? null : JsonSerializer.Serialize(data),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
    }
}
