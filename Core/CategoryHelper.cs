namespace ByteSplit.Core;

public static class CategoryHelper
{
    private static readonly Dictionary<string, HashSet<string>> CategoryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Compressed"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz",
            ".tgz", ".zst", ".cab", ".arj", ".lzh", ".ace", ".z"
        },
        ["Documents"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".txt", ".rtf", ".odt", ".ods", ".odp", ".csv", ".md",
            ".epub", ".djvu", ".chm"
        },
        ["Music"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma",
            ".opus", ".aiff", ".mid", ".midi"
        },
        ["Programs"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".msix", ".apk", ".deb", ".rpm", ".dmg",
            ".pkg", ".appimage", ".iso", ".img", ".bin", ".bat",
            ".cmd", ".ps1", ".sh"
        },
        ["Video"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
            ".m4v", ".mpeg", ".mpg", ".3gp", ".ts"
        }
    };

    public static string DetectCategory(string? fileNameOrUrl)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrUrl))
            return "Other";

        string clean = fileNameOrUrl.Split('?', '#')[0].Trim();
        string ext = Path.GetExtension(clean);
        if (string.IsNullOrEmpty(ext))
            return "Other";

        foreach (var (category, extensions) in CategoryExtensions)
        {
            if (extensions.Contains(ext))
                return category;
        }

        return "Other";
    }

    public static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "Other";

        return category.Trim().ToLowerInvariant() switch
        {
            "compressed" => "Compressed",
            "documents" => "Documents",
            "music" => "Music",
            "programs" => "Programs",
            "video" => "Video",
            _ => "Other"
        };
    }

    public static string GetCategoryDir(string baseDir, string category)
    {
        string normalized = NormalizeCategory(category);
        return normalized == "Other" ? baseDir : Path.Combine(baseDir, normalized);
    }

    public static bool IsDirectDownloadUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        string ext = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrEmpty(ext))
            return false;

        foreach (var extensions in CategoryExtensions.Values)
        {
            if (extensions.Contains(ext))
                return true;
        }

        return false;
    }
}