using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planning_MCP.Models.Planning;
using UglyToad.PdfPig;
using Xceed.Words.NET;
using Markdig;

namespace Planning_MCP.Services.Planning;

public class DocumentTaskParser
{
    private readonly ILogger<DocumentTaskParser> _logger;
    private readonly IConfiguration _configuration;

    // Configurable patterns
    private readonly string[] _actionVerbs;
    private readonly string[] _p1Keywords;
    private readonly string[] _p2Keywords;
    private readonly string[] _dependencyPhrases;

    public DocumentTaskParser(ILogger<DocumentTaskParser> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Load patterns from configuration or use defaults
        _actionVerbs = _configuration.GetSection("Planning:ActionVerbs").Get<string[]>() ?? new[]
        {
            "crear", "configurar", "migrar", "integrar", "desarrollar", "probar",
            "documentar", "automatizar", "validar", "desplegar", "implementar",
            "diseñar", "analizar", "revisar", "actualizar", "instalar"
        };

        _p1Keywords = _configuration.GetSection("Planning:P1Keywords").Get<string[]>() ?? new[]
        {
            "urgente", "crítico", "bloqueante", "go-live", "prioritario", "inmediato"
        };

        _p2Keywords = _configuration.GetSection("Planning:P2Keywords").Get<string[]>() ?? new[]
        {
            "alta", "prioritario", "importante", "necesario"
        };

        _dependencyPhrases = _configuration.GetSection("Planning:DependencyPhrases").Get<string[]>() ?? new[]
        {
            "depende de", "previamente", "antes de", "después de", "requiere", "necesita"
        };
    }

    public async Task<DocumentParseResult> ParseDocumentAsync(string path, string? language = null)
    {
        _logger.LogInformation($"Parsing document: {path}");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Document not found: {path}");
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var rawText = extension switch
        {
            ".pdf" => await ExtractTextFromPdfAsync(path),
            ".docx" => await ExtractTextFromDocxAsync(path),
            ".md" => await ExtractTextFromMarkdownAsync(path),
            ".txt" => await File.ReadAllTextAsync(path),
            _ => throw new NotSupportedException($"Unsupported file format: {extension}")
        };

        // Normalize text
        rawText = NormalizeText(rawText);

        // Calculate digest for idempotency
        var digest = CalculateSha256(rawText);

        // Extract tasks
        var tasks = ExtractTasks(rawText, language);

        _logger.LogInformation($"Extracted {tasks.Count} tasks from document (digest: {digest})");

        return new DocumentParseResult
        {
            Digest = digest,
            Tasks = tasks,
            RawText = rawText
        };
    }

    private async Task<string> ExtractTextFromPdfAsync(string path)
    {
        var text = new StringBuilder();
        
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }

        return text.ToString();
    }

    private async Task<string> ExtractTextFromDocxAsync(string path)
    {
        var text = new StringBuilder();

        using var document = DocX.Load(path);
        foreach (var paragraph in document.Paragraphs)
        {
            text.AppendLine(paragraph.Text);
        }

        return text.ToString();
    }

    private async Task<string> ExtractTextFromMarkdownAsync(string path)
    {
        var markdown = await File.ReadAllTextAsync(path);
        // Convert markdown to plain text (remove formatting)
        var plainText = Markdown.ToPlainText(markdown);
        return plainText;
    }

    private string NormalizeText(string text)
    {
        // Remove excessive whitespace
        text = Regex.Replace(text, @"\s+", " ");
        
        // Fix hyphenation at line breaks
        text = Regex.Replace(text, @"-\s+", "");
        
        // Normalize line breaks
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        return text.Trim();
    }

    private string CalculateSha256(string text)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLower();
    }

    private List<ParsedTask> ExtractTasks(string text, string? language)
    {
        var tasks = new List<ParsedTask>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // Skip empty or very short lines
            if (line.Length < 10) continue;

            // Check if line is a task (bullet, numbered, or starts with action verb)
            if (IsTaskLine(line))
            {
                var task = ParseTaskLine(line, i < lines.Length - 1 ? lines[i + 1] : null);
                if (task != null)
                {
                    tasks.Add(task);
                }
            }
        }

        return tasks;
    }

    private bool IsTaskLine(string line)
    {
        // Check for bullets: - * •
        if (Regex.IsMatch(line, @"^[-*•]\s+"))
            return true;

        // Check for numbered lists: 1. 1) 1-
        if (Regex.IsMatch(line, @"^\d+[\.\)]\s+"))
            return true;

        // Check if starts with action verb
        foreach (var verb in _actionVerbs)
        {
            if (Regex.IsMatch(line, $@"\b{verb}\b", RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    private ParsedTask? ParseTaskLine(string line, string? nextLine)
    {
        // Remove bullets and numbering
        var cleanLine = Regex.Replace(line, @"^[-*•]\s+", "");
        cleanLine = Regex.Replace(cleanLine, @"^\d+[\.\)]\s+", "");

        // If line is too short after cleaning, skip
        if (cleanLine.Length < 5)
            return null;

        var task = new ParsedTask
        {
            Title = cleanLine,
            Description = nextLine
        };

        // Extract priority
        task.Priority = DeterminePriority(cleanLine);

        // Extract estimate hours
        task.EstimateHours = ExtractEstimateHours(cleanLine);

        // Extract tags
        task.Tags = ExtractTags(cleanLine);

        // Extract dependencies
        task.DependsOn = ExtractDependencies(cleanLine, nextLine);

        return task;
    }

    private int DeterminePriority(string text)
    {
        var lowerText = text.ToLowerInvariant();

        // Check for P1 keywords
        foreach (var keyword in _p1Keywords)
        {
            if (lowerText.Contains(keyword.ToLowerInvariant()))
                return 1;
        }

        // Check for P2 keywords
        foreach (var keyword in _p2Keywords)
        {
            if (lowerText.Contains(keyword.ToLowerInvariant()))
                return 2;
        }

        // Default to P3
        return 3;
    }

    private double ExtractEstimateHours(string text)
    {
        // Pattern for explicit hours: ~8h, 8 h, 8 horas, 8h
        var hoursMatch = Regex.Match(text, @"[~]?\s*(\d+\.?\d*)\s*h(?:oras?)?", RegexOptions.IgnoreCase);
        if (hoursMatch.Success && double.TryParse(hoursMatch.Groups[1].Value, out var hours))
        {
            return hours;
        }

        // Heuristic based on task complexity
        var lowerText = text.ToLowerInvariant();

        // Simple tasks: 2h
        if (Regex.IsMatch(lowerText, @"\b(configurar|documentar|revisar|actualizar)\b"))
            return 2.0;

        // Complex tasks: 12h
        if (Regex.IsMatch(lowerText, @"\b(integrar|migrar|desarrollar|implementar|diseñar)\b"))
            return 12.0;

        // Medium tasks: 6h (default)
        return 6.0;
    }

    private List<string> ExtractTags(string text)
    {
        var tags = new List<string>();

        // Extract hashtags: #tag
        var hashtagMatches = Regex.Matches(text, @"#(\w+)");
        foreach (Match match in hashtagMatches)
        {
            tags.Add(match.Groups[1].Value);
        }

        // Extract [TAG:xxx] format
        var tagMatches = Regex.Matches(text, @"\[TAG:([^\]]+)\]", RegexOptions.IgnoreCase);
        foreach (Match match in tagMatches)
        {
            tags.Add(match.Groups[1].Value);
        }

        return tags.Distinct().ToList();
    }

    private List<string> ExtractDependencies(string line, string? nextLine)
    {
        var dependencies = new List<string>();
        var combinedText = line + " " + (nextLine ?? "");

        foreach (var phrase in _dependencyPhrases)
        {
            var pattern = $@"{phrase}\s+[""']?([^""'\n\.]+)[""']?";
            var matches = Regex.Matches(combinedText, pattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var dependency = match.Groups[1].Value.Trim();
                    if (dependency.Length > 3 && dependency.Length < 100)
                    {
                        dependencies.Add(dependency);
                    }
                }
            }
        }

        return dependencies.Distinct().ToList();
    }
}
