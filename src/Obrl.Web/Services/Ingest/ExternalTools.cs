using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Obrl.Web.Services.Ingest;

/// <summary>Runs LibreOffice and Calibre as separate processes, with a timeout.</summary>
public sealed class ExternalTools(IOptions<ObrlOptions> options, ILogger<ExternalTools> logger)
{
    private readonly ObrlOptions _o = options.Value;

    /// <summary>Converts a Word/ODT/RTF document to PDF with LibreOffice headless. Returns the PDF path.</summary>
    public async Task<string> ToPdfWithLibreOfficeAsync(string inputPath, string workDir, CancellationToken ct)
    {
        // A private profile folder lets several conversions run side by side.
        var profile = new Uri(Path.Combine(workDir, "lo-profile")).AbsoluteUri;
        await RunAsync(_o.LibreOfficePath,
            [$"-env:UserInstallation={profile}", "--headless", "--norestore", "--convert-to", "pdf", "--outdir", workDir, inputPath],
            workDir, ct);
        var pdf = Path.Combine(workDir, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        if (!File.Exists(pdf)) throw new IngestException("LibreOffice did not produce a PDF. Is the file damaged or password protected?");
        return pdf;
    }

    /// <summary>Converts MOBI/AZW/FB2 to EPUB with Calibre's ebook-convert. Returns the EPUB path.</summary>
    public async Task<string> ToEpubWithCalibreAsync(string inputPath, string workDir, CancellationToken ct)
    {
        var output = Path.Combine(workDir, "converted.epub");
        await RunAsync(_o.CalibreConvertPath, [inputPath, output], workDir, ct);
        if (!File.Exists(output)) throw new IngestException("Calibre did not produce an EPUB. DRM-protected e-books cannot be converted.");
        return output;
    }

    private async Task RunAsync(string fileName, string[] args, string workDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["HOME"] = workDir;

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new IngestException($"Could not start {fileName}.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new IngestException($"{fileName} is not installed on the server.");
        }

        using (process)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_o.ConversionTimeoutSeconds));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                if (ct.IsCancellationRequested) throw;
                throw new IngestException($"{Path.GetFileName(fileName)} took longer than {_o.ConversionTimeoutSeconds} seconds.");
            }

            if (process.ExitCode != 0)
            {
                logger.LogWarning("{Tool} exited with {Code}: {Out} {Err}", fileName, process.ExitCode, await stdout, await stderr);
                throw new IngestException($"{Path.GetFileName(fileName)} failed (exit code {process.ExitCode}).");
            }
        }
    }
}

/// <summary>A conversion problem that can be shown to the uploader.</summary>
public sealed class IngestException(string message) : Exception(message);
