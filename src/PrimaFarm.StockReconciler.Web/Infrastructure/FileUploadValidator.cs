using PrimaFarm.StockReconciler.Web.Services.Reconciliation;

namespace PrimaFarm.StockReconciler.Web.Infrastructure;

/// <summary>
/// OWASP File Upload checks: allow-listed extension, size limit, and content signature
/// (an .xlsx must really be a ZIP container; text files must not contain binary data).
/// Uploads are processed in memory and never written to disk or served back.
/// </summary>
public static class FileUploadValidator
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public static async Task<UploadedFile> ReadAsync(IFormFile file, long maxBytes, CancellationToken ct)
    {
        var name = Path.GetFileName(file.FileName);   // strip any client-supplied path
        var ext = Path.GetExtension(name).ToLowerInvariant();

        if (!StockFileReader.SupportedExtensions.Contains(ext))
            throw new BusinessRuleException($"'{name}': only {string.Join(", ", StockFileReader.SupportedExtensions)} files are accepted.");
        if (file.Length == 0)
            throw new BusinessRuleException($"'{name}' is empty.");
        if (file.Length > maxBytes)
            throw new BusinessRuleException($"'{name}' is larger than the {maxBytes / (1024 * 1024)} MB limit.");

        var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        var bytes = ms.GetBuffer().AsSpan(0, (int)ms.Length);

        bool isExcel = ext is ".xlsx" or ".xlsm";
        if (isExcel && !bytes.StartsWith(ZipSignature))
            throw new BusinessRuleException($"'{name}' is not a valid Excel workbook.");
        if (!isExcel && bytes[..Math.Min(bytes.Length, 8192)].Contains((byte)0))
            throw new BusinessRuleException($"'{name}' does not look like a text file.");

        ms.Position = 0;
        return new UploadedFile(name.Length > 200 ? name[..200] : name, ms);
    }
}
