using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Services.Ingest;

/// <summary>
/// Converts queued uploads one at a time in the background. The queue is the Books table itself,
/// so nothing is lost if the app restarts mid-way.
/// </summary>
public sealed class ConversionWorker(IServiceScopeFactory scopes, ILogger<ConversionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueInterruptedAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            bool didWork;
            try
            {
                didWork = await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Conversion loop failed");
                didWork = false;
            }
            if (!didWork) await Task.Delay(IdleDelay, stoppingToken);
        }
    }

    private async Task RequeueInterruptedAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ObrlDbContext>();
            await db.Books.Where(b => b.Status == BookStatus.Processing)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookStatus.Queued), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not requeue interrupted conversions");
        }
    }

    /// <summary>Converts the oldest queued book. Returns false when the queue is empty.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObrlDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<BookProcessor>();

        var book = await db.Books.Where(b => b.Status == BookStatus.Queued).OrderBy(b => b.CreatedAt).FirstOrDefaultAsync(ct);
        if (book is null) return false;

        book.Status = BookStatus.Processing;
        book.StatusMessage = null;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Converting {Book} ({Format})", book.Id, book.OriginalFormat);

        try
        {
            await processor.ProcessAsync(book, ct);
            book.Status = BookStatus.Ready;
            book.ReadyAt = DateTimeOffset.UtcNow;
        }
        catch (IngestException ex)
        {
            book.Status = BookStatus.Failed;
            book.StatusMessage = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Conversion of {Book} failed", book.Id);
            book.Status = BookStatus.Failed;
            book.StatusMessage = "Unexpected error while converting. See the server log.";
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
