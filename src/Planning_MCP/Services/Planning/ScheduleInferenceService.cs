using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planning_MCP.Models.Planning;

namespace Planning_MCP.Services.Planning;

public class ScheduleInferenceService
{
    private readonly ILogger<ScheduleInferenceService> _logger;
    private readonly IConfiguration _configuration;
    private readonly double _defaultCapacityHoursPerSprint;
    private readonly bool _startOnNextMonday;

    public ScheduleInferenceService(ILogger<ScheduleInferenceService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _defaultCapacityHoursPerSprint = _configuration.GetValue<double>("Planning:DefaultCapacityHoursPerSprint", 80.0);
        _startOnNextMonday = _configuration.GetValue<bool>("Planning:DefaultStartOnNextMonday", true);
    }

    public ProjectSchedule InferSchedule(string rawText, List<ParsedTask> tasks, double? capacityOverride = null)
    {
        _logger.LogInformation("Inferring project schedule from document and tasks");

        var capacity = capacityOverride ?? _defaultCapacityHoursPerSprint;
        var rationale = new List<string>();

        // Try to extract explicit schedule information from text
        var startDate = ExtractStartDate(rawText, rationale);
        var totalDurationDays = ExtractTotalDuration(rawText, rationale);
        var sprintLengthDays = ExtractSprintLength(rawText, rationale);

        // If no explicit dates, calculate from tasks
        if (!startDate.HasValue)
        {
            startDate = _startOnNextMonday ? GetNextMonday() : DateTime.Today;
            rationale.Add($"No se encontró fecha de inicio explícita. Usando: {startDate.Value:yyyy-MM-dd}");
        }

        // Calculate total hours needed
        var totalHours = tasks.Sum(t => t.EstimateHours);
        if (totalHours == 0)
        {
            // Estimate if tasks have no hours
            totalHours = tasks.Count * 6.0; // Average 6h per task
            rationale.Add($"No se encontraron estimaciones explícitas. Usando promedio de 6h por tarea: {totalHours}h totales");
        }
        else
        {
            rationale.Add($"Horas totales estimadas desde tareas: {totalHours}h");
        }

        // Calculate sprint count
        var sprintCount = (int)Math.Ceiling(totalHours / capacity);
        sprintCount = Math.Max(1, Math.Min(12, sprintCount)); // Between 1 and 12 sprints
        rationale.Add($"Sprints calculados: {sprintCount} (basado en {capacity}h por sprint)");

        // Determine sprint length if not explicit
        if (sprintLengthDays == 0)
        {
            sprintLengthDays = DetermineOptimalSprintLength(totalDurationDays, sprintCount, rawText, rationale);
        }

        // Calculate total duration if not explicit
        if (totalDurationDays == 0)
        {
            totalDurationDays = sprintCount * sprintLengthDays;
            rationale.Add($"Duración total calculada: {totalDurationDays} días ({sprintCount} sprints × {sprintLengthDays} días)");
        }

        return new ProjectSchedule
        {
            StartDate = startDate.Value.ToString("yyyy-MM-dd"),
            TotalDurationDays = totalDurationDays,
            SprintLengthDays = sprintLengthDays,
            SprintCount = sprintCount,
            CapacityHoursPerSprint = capacity,
            Rationale = string.Join(" | ", rationale)
        };
    }

    private DateTime? ExtractStartDate(string text, List<string> rationale)
    {
        // Look for explicit dates in various formats
        var patterns = new[]
        {
            @"fecha\s+de\s+inicio[:\s]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4})",
            @"inicio[:\s]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4})",
            @"comienza[:\s]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4})",
            @"start\s+date[:\s]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4})"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var dateStr = match.Groups[1].Value;
                if (TryParseDate(dateStr, out var date))
                {
                    rationale.Add($"Fecha de inicio extraída del documento: {date:yyyy-MM-dd}");
                    return date;
                }
            }
        }

        return null;
    }

    private int ExtractTotalDuration(string text, List<string> rationale)
    {
        // Look for mentions of project duration
        var patterns = new[]
        {
            @"duración[:\s]+(\d+)\s*(semanas?|weeks?)",
            @"duración[:\s]+(\d+)\s*(meses?|months?)",
            @"proyecto\s+de\s+(\d+)\s*(semanas?|weeks?)",
            @"proyecto\s+de\s+(\d+)\s*(meses?|months?)",
            @"(\d+)\s*(semanas?|weeks?)\s+de\s+duración",
            @"(\d+)\s*(meses?|months?)\s+de\s+duración"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var duration))
            {
                var unit = match.Groups[2].Value.ToLowerInvariant();
                var days = unit.Contains("mes") || unit.Contains("month") 
                    ? duration * 30 
                    : duration * 7;
                
                rationale.Add($"Duración extraída del documento: {duration} {unit} = {days} días");
                return days;
            }
        }

        return 0;
    }

    private int ExtractSprintLength(string text, List<string> rationale)
    {
        // Look for mentions of sprint length
        var patterns = new[]
        {
            @"sprints?\s+de\s+(\d+)\s*(semanas?|weeks?)",
            @"iteraciones?\s+de\s+(\d+)\s*(semanas?|weeks?)",
            @"sprint\s+length[:\s]+(\d+)\s*(semanas?|weeks?)",
            @"ciclos?\s+de\s+(\d+)\s*(semanas?|weeks?)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var weeks))
            {
                var days = weeks * 7;
                rationale.Add($"Longitud de sprint extraída del documento: {weeks} semanas = {days} días");
                return days;
            }
        }

        // Check for specific sprint length mentions
        if (Regex.IsMatch(text, @"\bsprint\s+semanal", RegexOptions.IgnoreCase))
        {
            rationale.Add("Sprint semanal detectado: 7 días");
            return 7;
        }

        if (Regex.IsMatch(text, @"\bsprint\s+quincenal", RegexOptions.IgnoreCase))
        {
            rationale.Add("Sprint quincenal detectado: 14 días");
            return 14;
        }

        return 0;
    }

    private int DetermineOptimalSprintLength(int totalDurationDays, int sprintCount, string text, List<string> rationale)
    {
        // If total duration is known, calculate optimal length
        if (totalDurationDays > 0)
        {
            var calculatedLength = totalDurationDays / sprintCount;
            var optimalLength = RoundToStandardSprintLength(calculatedLength);
            rationale.Add($"Longitud de sprint calculada: {calculatedLength} días → redondeado a {optimalLength} días");
            return optimalLength;
        }

        // Check text for hints about project scale
        var lowerText = text.ToLowerInvariant();

        if (lowerText.Contains("ágil") || lowerText.Contains("agile") || sprintCount >= 8)
        {
            rationale.Add("Proyecto ágil detectado: usando sprints de 14 días");
            return 14;
        }

        if (lowerText.Contains("rápido") || lowerText.Contains("quick") || sprintCount <= 3)
        {
            rationale.Add("Proyecto rápido detectado: usando sprints de 7 días");
            return 7;
        }

        // Default to 2-week sprints
        rationale.Add("Usando longitud de sprint estándar: 14 días");
        return 14;
    }

    private int RoundToStandardSprintLength(int days)
    {
        // Round to nearest standard sprint length: 7, 14, or 21 days
        if (days <= 10) return 7;
        if (days <= 17) return 14;
        return 21;
    }

    private DateTime GetNextMonday()
    {
        var today = DateTime.Today;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7; // If today is Monday, get next Monday
        return today.AddDays(daysUntilMonday);
    }

    private bool TryParseDate(string dateStr, out DateTime date)
    {
        var formats = new[]
        {
            "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "d-M-yyyy",
            "MM/dd/yyyy", "MM-dd-yyyy", "M/d/yyyy", "M-d-yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd"
        };

        return DateTime.TryParseExact(
            dateStr, 
            formats, 
            CultureInfo.InvariantCulture, 
            DateTimeStyles.None, 
            out date);
    }
}
