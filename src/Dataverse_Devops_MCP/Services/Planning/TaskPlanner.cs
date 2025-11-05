using Microsoft.Extensions.Logging;
using Dataverse_Devops_MCP.Models.Planning;

namespace Dataverse_Devops_MCP.Services.Planning;

public class TaskPlanner
{
    private readonly ILogger<TaskPlanner> _logger;

    public TaskPlanner(ILogger<TaskPlanner> logger)
    {
        _logger = logger;
    }

    public TaskPlan PlanTasksIntoIterations(List<ParsedTask> tasks, ProjectSchedule schedule)
    {
        _logger.LogInformation($"Planning {tasks.Count} tasks into {schedule.SprintCount} iterations");

        var plan = new TaskPlan();
        var startDate = DateTime.Parse(schedule.StartDate);

        // Create iterations
        for (int i = 0; i < schedule.SprintCount; i++)
        {
            var iterationStart = startDate.AddDays(i * schedule.SprintLengthDays);
            var iterationEnd = iterationStart.AddDays(schedule.SprintLengthDays - 1);

            plan.Iterations.Add(new IterationDef
            {
                Name = $"Sprint {i + 1}",
                Start = iterationStart.ToString("yyyy-MM-dd"),
                Finish = iterationEnd.ToString("yyyy-MM-dd"),
                Items = new List<TaskItem>(),
                LoadHours = 0
            });
        }

        // Convert ParsedTask to TaskItem and sort by priority and dependencies
        var taskItems = tasks.Select(t => new TaskItem
        {
            Title = t.Title,
            EstimateHours = t.EstimateHours,
            Priority = t.Priority,
            Description = t.Description,
            Tags = t.Tags,
            DependsOn = t.DependsOn
        }).ToList();

        // Build dependency graph
        var tasksByTitle = taskItems.ToDictionary(t => t.Title, t => t);
        var assignedTasks = new HashSet<string>();

        // Sort tasks: P1 first, then P2, then P3, considering dependencies
        var sortedTasks = TopologicalSort(taskItems);

        // Assign tasks to iterations
        int currentIterationIndex = 0;

        foreach (var task in sortedTasks)
        {
            var assigned = false;

            // Check if dependencies are satisfied in previous iterations
            var dependenciesSatisfied = true;
            var minIterationForTask = 0;

            foreach (var dependency in task.DependsOn)
            {
                // Find which iteration contains this dependency
                for (int i = 0; i < plan.Iterations.Count; i++)
                {
                    if (plan.Iterations[i].Items.Any(item => item.Title.Contains(dependency, StringComparison.OrdinalIgnoreCase)))
                    {
                        minIterationForTask = Math.Max(minIterationForTask, i + 1); // Must be in next iteration or later
                        break;
                    }
                }
            }

            // Start from the minimum iteration required by dependencies
            currentIterationIndex = Math.Max(currentIterationIndex, minIterationForTask);

            // Try to fit task in current or subsequent iterations
            for (int i = currentIterationIndex; i < plan.Iterations.Count; i++)
            {
                var iteration = plan.Iterations[i];
                var remainingCapacity = schedule.CapacityHoursPerSprint - iteration.LoadHours;

                if (task.EstimateHours <= remainingCapacity)
                {
                    // Assign task to this iteration
                    iteration.Items.Add(task);
                    iteration.LoadHours += task.EstimateHours;
                    assignedTasks.Add(task.Title);
                    assigned = true;

                    _logger.LogDebug($"Assigned task '{task.Title}' ({task.EstimateHours}h) to {iteration.Name}");
                    break;
                }
            }

            if (!assigned)
            {
                // Task couldn't be assigned (too large or no capacity)
                plan.Unplanned.Add(task);
                _logger.LogWarning($"Task '{task.Title}' ({task.EstimateHours}h) could not be assigned to any iteration");
            }
        }

        // Calculate summary
        plan.Summary.Items = assignedTasks.Count;
        plan.Summary.Hours = plan.Iterations.Sum(i => i.LoadHours);

        _logger.LogInformation($"Planning complete: {plan.Summary.Items} tasks assigned, {plan.Unplanned.Count} unplanned, {plan.Summary.Hours}h total");

        return plan;
    }

    private List<TaskItem> TopologicalSort(List<TaskItem> tasks)
    {
        // Sort tasks by priority first, then apply topological sort for dependencies
        var sorted = new List<TaskItem>();
        var visited = new HashSet<string>();
        var tasksByTitle = tasks.ToDictionary(t => t.Title, t => t);

        // Process by priority: P1, then P2, then P3
        for (int priority = 1; priority <= 3; priority++)
        {
            var tasksWithPriority = tasks.Where(t => t.Priority == priority).ToList();

            foreach (var task in tasksWithPriority)
            {
                if (!visited.Contains(task.Title))
                {
                    VisitTask(task, tasksByTitle, visited, sorted);
                }
            }
        }

        return sorted;
    }

    private void VisitTask(TaskItem task, Dictionary<string, TaskItem> tasksByTitle, HashSet<string> visited, List<TaskItem> sorted)
    {
        if (visited.Contains(task.Title))
            return;

        visited.Add(task.Title);

        // Visit dependencies first (if they exist in our task list)
        foreach (var dependency in task.DependsOn)
        {
            // Try to find matching task by title
            var dependentTask = tasksByTitle.Values.FirstOrDefault(t => 
                t.Title.Contains(dependency, StringComparison.OrdinalIgnoreCase) ||
                dependency.Contains(t.Title, StringComparison.OrdinalIgnoreCase));

            if (dependentTask != null && !visited.Contains(dependentTask.Title))
            {
                VisitTask(dependentTask, tasksByTitle, visited, sorted);
            }
        }

        sorted.Add(task);
    }
}
